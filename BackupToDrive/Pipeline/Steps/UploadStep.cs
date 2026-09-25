using BackupToDrive.Configuration;
using BackupToDrive.Infrastructure;
using BackupToDrive.Services.Drive;
using Microsoft.Extensions.Logging;

namespace BackupToDrive.Pipeline.Steps;

public sealed class UploadStep : IPipelineStep
{
    private readonly IGoogleDriveUploader _uploader;
    private readonly ILogger<UploadStep> _logger;

    public UploadStep(IGoogleDriveUploader uploader, ILogger<UploadStep> logger)
    {
        _uploader = uploader;
        _logger = logger;
    }

    public int Order => 30;
    public bool RunOnFailure => false;

    public bool IsEnabled(JobOptions job) => job.Steps.Upload;

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        var artifacts = context.Artifacts
            .Where(a => a.Status is ArtifactStatus.Pending or ArtifactStatus.Compressed)
            .ToList();

        if (artifacts.Count == 0)
        {
            _logger.LogInformation("Nada para subir.");
            return;
        }

        var relativePath = DrivePathTemplate.Render(
            context.Job.DrivePathTemplate,
            context.Job.Name,
            context.RunAt);

        if (context.DryRun)
        {
            foreach (var artifact in artifacts)
            {
                _logger.LogInformation(
                    "[dry-run] Subiría {Archivo} ({Size:N0} bytes) a {Ruta}",
                    artifact.FileName,
                    artifact.SizeBytes,
                    relativePath);
            }

            return;
        }

        var folderId = await _uploader.EnsureFolderPathAsync(relativePath, cancellationToken);
        _logger.LogInformation("Destino Drive: {Ruta} ({FolderId})", relativePath, folderId);

        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (context.Job.SkipIfExistsOnDrive)
                {
                    var (existingId, existingSize) = await _uploader.FindFileAsync(
                        folderId,
                        artifact.FileName,
                        cancellationToken);

                    if (existingId is not null && existingSize == artifact.SizeBytes)
                    {
                        artifact.Status = ArtifactStatus.Skipped;
                        artifact.DriveFileId = existingId;
                        artifact.Detail = "Ya existe en Drive (mismo tamaño)";
                        _logger.LogInformation("Ya está en Drive, se omite: {Archivo}", artifact.FileName);
                        continue;
                    }
                }

                _logger.LogInformation(
                    "Subiendo {Archivo} ({Size:N0} bytes)...",
                    artifact.FileName,
                    artifact.SizeBytes);

                artifact.DriveFileId = await _uploader.UploadAsync(
                    folderId,
                    artifact.FullPath,
                    cancellationToken);

                artifact.Status = ArtifactStatus.Uploaded;
                artifact.Detail = "Subido";
                _logger.LogInformation("Subido: {Archivo} (id {Id})", artifact.FileName, artifact.DriveFileId);

                if (context.Job.DeleteLocalAfterUpload)
                {
                    File.Delete(artifact.FullPath);
                    _logger.LogInformation("Eliminado en local: {Archivo}", artifact.FileName);
                }
            }
            catch (Exception ex)
            {
                artifact.Status = ArtifactStatus.Failed;
                artifact.Detail = ex.Message;
                context.Fail($"Error al subir {artifact.FileName}: {ex.Message}");
                _logger.LogError(ex, "Error al subir {Archivo}", artifact.FileName);
            }
        }
    }
}
