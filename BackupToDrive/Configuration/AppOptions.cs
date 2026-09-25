namespace BackupToDrive.Configuration;

public sealed class AppOptions
{
    public GoogleDriveOptions GoogleDrive { get; set; } = new();
    public List<JobOptions> Jobs { get; set; } = [];
    public MailOptions Mail { get; set; } = new();
}

public sealed class GoogleDriveOptions
{
    /// <summary>ServiceAccount u OAuth.</summary>
    public string AuthMode { get; set; } = "ServiceAccount";
    public string CredentialsPath { get; set; } = "credentials/service-account.json";
    public string TokenStorePath { get; set; } = "credentials/oauth-token";
    public string RootFolderId { get; set; } = "";
    public string ApplicationName { get; set; } = "BackupToDrive";

    /// <summary>
    /// Avisa si el espacio libre no alcanza para esta cantidad de backups diarios
    /// del tamaño del último archivo subido. 0 no avisa.
    /// </summary>
    public int SpaceAlertDays { get; set; } = 30;
}

public sealed class JobOptions
{
    public string Name { get; set; } = "Default";
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Servidor, base y usuario. Ejemplo:
    /// Server=localhost;Database=gead;User Id=usuario;Password=clave;TrustServerCertificate=True
    /// </summary>
    public string ConnectionString { get; set; } = "";

    public string SourceFolder { get; set; } = "";
    public string SearchPattern { get; set; } = "*.*";
    public bool Recursive { get; set; }
    public int MinAgeMinutes { get; set; } = 5;
    public bool SkipIfExistsOnDrive { get; set; } = true;
    public string DrivePathTemplate { get; set; } = "Backups/{JobName}/{yyyy}/{MM}";
    public bool DeleteLocalAfterUpload { get; set; }

    /// <summary>Días que se conserva un .rar local antes de generar. 0 borra los anteriores.</summary>
    public int LocalRetentionDays { get; set; } = 7;

    public JobStepsOptions Steps { get; set; } = new();
    public CompressOptions Compress { get; set; } = new();
}

public sealed class JobStepsOptions
{
    public bool CreateBackup { get; set; }
    public bool Compress { get; set; }
    public bool Upload { get; set; } = true;
    public bool Notify { get; set; }

    /// <summary>Antes de generar, borra .rar locales más viejos que LocalRetentionDays. 0 borra todos los previos.</summary>
    public bool CleanupLocal { get; set; }
}

public sealed class CompressOptions
{
    public string WinRarPath { get; set; } = @"C:\Program Files\WinRAR\Rar.exe";
    public string ExtraArguments { get; set; } = "-ep1 -m5 -y";
    public string OutputFolder { get; set; } = "";
    public int TimeoutMinutes { get; set; } = 120;
}

public sealed class MailOptions
{
    public bool Enabled { get; set; }
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public List<string> To { get; set; } = [];
    public bool NotifyOnSuccess { get; set; } = true;
    public bool NotifyOnFailure { get; set; } = true;

    /// <summary>Carpeta con los HTML de los mails. Relativa al .exe, o absoluta.</summary>
    public string TemplatesFolder { get; set; } = "templates";
    public string SuccessTemplate { get; set; } = "mail-ok.html";
    public string FailureTemplate { get; set; } = "mail-error.html";
    public string SpaceAlertTemplate { get; set; } = "mail-space.html";
}
