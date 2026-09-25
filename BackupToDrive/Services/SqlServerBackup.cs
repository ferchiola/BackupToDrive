using BackupToDrive.Configuration;
using BackupToDrive.Pipeline;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace BackupToDrive.Services;

public sealed class SqlServerBackup
{
    private readonly ILogger<SqlServerBackup> _logger;

    public SqlServerBackup(ILogger<SqlServerBackup> logger)
    {
        _logger = logger;
    }

    public async Task<BackupArtifact?> CreateAsync(
        JobOptions job,
        DateTime runAt,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ConnectionString))
        {
            throw new InvalidOperationException(
                $"Job {job.Name}: ConnectionString está vacía. " +
                "Completá Server, Database, User Id y Password en appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(job.SourceFolder))
        {
            throw new InvalidOperationException($"Job {job.Name}: SourceFolder no está configurado.");
        }

        var builder = new SqlConnectionStringBuilder(job.ConnectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            throw new InvalidOperationException(
                $"Job {job.Name}: la connection string no tiene Database.");
        }

        var database = builder.InitialCatalog;
        var fileName = $"{SanitizeFileName(database)}-{runAt:yyyyMMdd-HHmmss}.bak";
        var fullPath = Path.Combine(job.SourceFolder, fileName);

        if (dryRun)
        {
            _logger.LogInformation(
                "[dry-run] Backup de {Database} en {Server} hacia {Archivo}",
                database,
                builder.DataSource,
                fullPath);
            return null;
        }

        Directory.CreateDirectory(job.SourceFolder);

        _logger.LogInformation(
            "Backup de {Database} en {Server} hacia {Archivo}. El archivo lo escribe el servicio de SQL Server, en esa ruta del equipo donde corre la instancia.",
            database,
            builder.DataSource,
            fullPath);

        await using var connection = new SqlConnection(builder.ConnectionString);
        connection.InfoMessage += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Message))
            {
                _logger.LogInformation("{Mensaje}", e.Message.Trim());
            }
        };

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 0;
        command.CommandText =
            $"BACKUP DATABASE {QuoteIdentifier(database)} TO DISK = @path WITH CHECKSUM, STATS = 10";
        command.Parameters.Add(new SqlParameter("@path", fullPath));

        await command.ExecuteNonQueryAsync(cancellationToken);

        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length == 0)
        {
            throw new IOException(
                $"SQL Server terminó el backup pero no apareció el archivo '{fullPath}'. " +
                "Si la instancia no es local, esa ruta es del servidor, no de esta PC.");
        }

        _logger.LogInformation("Backup listo: {Archivo} ({Size:N0} bytes)", info.Name, info.Length);

        return new BackupArtifact
        {
            FullPath = info.FullName,
            FileName = info.Name,
            SizeBytes = info.Length,
            Detail = "Backup de SQL Server"
        };
    }

    private static string QuoteIdentifier(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "backup" : cleaned;
    }
}
