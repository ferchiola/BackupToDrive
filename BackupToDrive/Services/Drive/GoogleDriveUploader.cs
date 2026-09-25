using BackupToDrive.Configuration;
using Google.Apis.Drive.v3;
using Google.Apis.Upload;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace BackupToDrive.Services.Drive;

public interface IGoogleDriveUploader
{
    Task<string> EnsureFolderPathAsync(string relativePath, CancellationToken cancellationToken);

    Task<(string? Id, long Size)> FindFileAsync(
        string folderId,
        string fileName,
        CancellationToken cancellationToken);

    Task<string> UploadAsync(
        string folderId,
        string localPath,
        CancellationToken cancellationToken);

    Task<DriveQuota> GetQuotaAsync(CancellationToken cancellationToken);
}

public sealed class DriveQuota
{
    public long UsageBytes { get; init; }
    public long? LimitBytes { get; init; }
}

public sealed class GoogleDriveUploader : IGoogleDriveUploader, IAsyncDisposable
{
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const long ProgressLogEveryBytes = 32L * 1024 * 1024;

    private readonly IDriveServiceFactory _factory;
    private readonly GoogleDriveOptions _options;
    private readonly ILogger<GoogleDriveUploader> _logger;
    private DriveService? _service;

    public GoogleDriveUploader(
        IDriveServiceFactory factory,
        IOptions<AppOptions> options,
        ILogger<GoogleDriveUploader> logger)
    {
        _factory = factory;
        _options = options.Value.GoogleDrive;
        _logger = logger;
    }

    public async Task<string> EnsureFolderPathAsync(string relativePath, CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        var parentId = string.IsNullOrWhiteSpace(_options.RootFolderId) ? "root" : _options.RootFolderId.Trim();

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return parentId;
        }

        foreach (var segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            parentId = await FindOrCreateFolderAsync(service, parentId, segment, cancellationToken);
        }

        return parentId;
    }

    public async Task<(string? Id, long Size)> FindFileAsync(
        string folderId,
        string fileName,
        CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        var request = service.Files.List();
        request.Q = $"name = '{EscapeQuery(fileName)}' and '{folderId}' in parents and trashed = false";
        request.Fields = "files(id, name, size)";
        request.PageSize = 10;
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;

        var result = await request.ExecuteAsync(cancellationToken);
        var file = result.Files?.FirstOrDefault();
        if (file is null)
        {
            return (null, 0);
        }

        return (file.Id, file.Size ?? 0);
    }

    public async Task<string> UploadAsync(
        string folderId,
        string localPath,
        CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        var info = new FileInfo(localPath);

        var metadata = new DriveFile
        {
            Name = info.Name,
            Parents = [folderId]
        };

        await using var stream = new FileStream(
            localPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);

        var request = service.Files.Create(metadata, stream, "application/octet-stream");
        request.Fields = "id, name, size";
        request.SupportsAllDrives = true;
        request.ChunkSize = ResumableUpload.MinimumChunkSize * 4;

        var lastLogged = 0L;
        request.ProgressChanged += progress =>
        {
            if (progress.Status != UploadStatus.Uploading)
            {
                return;
            }

            if (progress.BytesSent - lastLogged < ProgressLogEveryBytes)
            {
                return;
            }

            lastLogged = progress.BytesSent;
            var percent = info.Length == 0 ? 100 : progress.BytesSent * 100 / info.Length;
            _logger.LogInformation(
                "  {Archivo}: {Enviados:N0} / {Total:N0} bytes ({Percent}%)",
                info.Name,
                progress.BytesSent,
                info.Length,
                percent);
        };

        var result = await request.UploadAsync(cancellationToken);
        if (result.Status != UploadStatus.Completed)
        {
            throw new InvalidOperationException(
                result.Exception?.Message ?? $"La subida de '{info.Name}' no se completó ({result.Status}).");
        }

        return request.ResponseBody?.Id
            ?? throw new InvalidOperationException($"Drive no devolvió ID para '{info.Name}'.");
    }

    public async Task<DriveQuota> GetQuotaAsync(CancellationToken cancellationToken)
    {
        var service = await GetServiceAsync(cancellationToken);
        var request = service.About.Get();
        request.Fields = "storageQuota(limit,usage)";
        var about = await request.ExecuteAsync(cancellationToken);
        var quota = about.StorageQuota
            ?? throw new InvalidOperationException("Drive no devolvió la cuota de almacenamiento.");

        return new DriveQuota
        {
            UsageBytes = quota.Usage ?? 0,
            LimitBytes = quota.Limit
        };
    }

    public ValueTask DisposeAsync()
    {
        _service?.Dispose();
        _service = null;
        return ValueTask.CompletedTask;
    }

    private async Task<DriveService> GetServiceAsync(CancellationToken cancellationToken)
    {
        _service ??= await _factory.CreateAsync(cancellationToken);
        return _service;
    }

    private async Task<string> FindOrCreateFolderAsync(
        DriveService service,
        string parentId,
        string name,
        CancellationToken cancellationToken)
    {
        var list = service.Files.List();
        list.Q =
            $"name = '{EscapeQuery(name)}' and '{parentId}' in parents " +
            $"and mimeType = '{FolderMime}' and trashed = false";
        list.Fields = "files(id, name)";
        list.PageSize = 5;
        list.SupportsAllDrives = true;
        list.IncludeItemsFromAllDrives = true;

        var existing = await list.ExecuteAsync(cancellationToken);
        var found = existing.Files?.FirstOrDefault();
        if (found?.Id is not null)
        {
            return found.Id;
        }

        var folder = new DriveFile
        {
            Name = name,
            MimeType = FolderMime,
            Parents = [parentId]
        };

        var create = service.Files.Create(folder);
        create.Fields = "id";
        create.SupportsAllDrives = true;
        var created = await create.ExecuteAsync(cancellationToken);
        _logger.LogInformation("Carpeta Drive creada: {Nombre} ({Id})", name, created.Id);
        return created.Id ?? throw new InvalidOperationException($"No se pudo crear la carpeta '{name}'.");
    }

    private static string EscapeQuery(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}
