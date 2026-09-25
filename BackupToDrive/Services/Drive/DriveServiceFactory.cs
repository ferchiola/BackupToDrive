using BackupToDrive.Configuration;
using BackupToDrive.Infrastructure;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackupToDrive.Services.Drive;

public interface IDriveServiceFactory
{
    Task<DriveService> CreateAsync(CancellationToken cancellationToken);
}

public sealed class DriveServiceFactory : IDriveServiceFactory
{
    private readonly AppOptions _options;
    private readonly PathResolver _paths;
    private readonly ILogger<DriveServiceFactory> _logger;

    public DriveServiceFactory(
        IOptions<AppOptions> options,
        PathResolver paths,
        ILogger<DriveServiceFactory> logger)
    {
        _options = options.Value;
        _paths = paths;
        _logger = logger;
    }

    public async Task<DriveService> CreateAsync(CancellationToken cancellationToken)
    {
        var drive = _options.GoogleDrive;
        var initializer = new BaseClientService.Initializer
        {
            ApplicationName = string.IsNullOrWhiteSpace(drive.ApplicationName)
                ? "BackupToDrive"
                : drive.ApplicationName
        };

        if (drive.AuthMode.Equals("OAuth", StringComparison.OrdinalIgnoreCase))
        {
            initializer.HttpClientInitializer = await CreateOAuthCredentialAsync(drive, cancellationToken);
        }
        else
        {
            initializer.HttpClientInitializer = await CreateServiceAccountCredentialAsync(drive, cancellationToken);
        }

        return new DriveService(initializer);
    }

    private async Task<GoogleCredential> CreateServiceAccountCredentialAsync(
        GoogleDriveOptions drive,
        CancellationToken cancellationToken)
    {
        var path = _paths.Resolve(drive.CredentialsPath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "No se encontró el JSON de la cuenta de servicio. " +
                "En Google Cloud: creá un proyecto, habilitá Google Drive API, " +
                "creá un Service Account, descargá la clave JSON a " +
                $"'{path}' y compartí la carpeta de Drive con el email de esa cuenta.");
        }

        _logger.LogInformation("Auth Drive: Service Account ({Path})", path);
        var serviceAccount = await CredentialFactory.FromFileAsync<ServiceAccountCredential>(path, cancellationToken);
        return serviceAccount.ToGoogleCredential().CreateScoped(DriveService.Scope.Drive);
    }

    private async Task<UserCredential> CreateOAuthCredentialAsync(
        GoogleDriveOptions drive,
        CancellationToken cancellationToken)
    {
        var path = _paths.Resolve(drive.CredentialsPath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "No se encontró client_secret.json de OAuth. " +
                "En Google Cloud: creá credenciales de tipo 'Aplicación de escritorio' " +
                $"y guardá el archivo en '{path}'.");
        }

        var tokenStore = _paths.Resolve(drive.TokenStorePath);
        Directory.CreateDirectory(tokenStore);

        _logger.LogInformation("Auth Drive: OAuth ({Path})", path);
        var secrets = await GoogleClientSecrets.FromFileAsync(path, cancellationToken);
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            [DriveService.Scope.Drive],
            "user",
            cancellationToken,
            new FileDataStore(tokenStore, fullPath: true));
    }
}
