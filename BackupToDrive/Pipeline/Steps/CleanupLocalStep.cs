using BackupToDrive.Configuration;
using Microsoft.Extensions.Logging;

namespace BackupToDrive.Pipeline.Steps;

public sealed class CleanupLocalStep : IPipelineStep
{
    private readonly ILogger<CleanupLocalStep> _logger;

    public CleanupLocalStep(ILogger<CleanupLocalStep> logger)
    {
        _logger = logger;
    }

    public int Order => 5;
    public bool RunOnFailure => false;

    public bool IsEnabled(JobOptions job) => job.Steps.CleanupLocal;

    public Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        var job = context.Job;
        if (job.LocalRetentionDays < 0)
        {
            _logger.LogWarning(
                "LocalRetentionDays no puede ser negativo ({Dias}). No se limpia.",
                job.LocalRetentionDays);
            return Task.CompletedTask;
        }

        var folder = string.IsNullOrWhiteSpace(job.Compress.OutputFolder)
            ? job.SourceFolder
            : job.Compress.OutputFolder;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            _logger.LogInformation("Limpieza local: no hay carpeta '{Carpeta}'.", folder);
            return Task.CompletedTask;
        }

        var cutoff = context.RunAt.AddDays(-job.LocalRetentionDays);
        var reason = job.LocalRetentionDays == 0
            ? "anteriores a esta corrida"
            : $"con más de {job.LocalRetentionDays} días";

        var option = job.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var deleted = 0;

        foreach (var path in Directory.EnumerateFiles(folder, "*.rar", option))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new FileInfo(path);
            if (!info.Exists || info.LastWriteTime >= cutoff)
            {
                continue;
            }

            if (context.DryRun)
            {
                _logger.LogInformation("[dry-run] Borraría {Archivo} ({Motivo})", info.Name, reason);
                continue;
            }

            try
            {
                info.Delete();
                deleted++;
                _logger.LogInformation("Limpieza local: eliminado {Archivo} ({Motivo})", info.Name, reason);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo eliminar {Archivo}", info.Name);
            }
        }

        if (!context.DryRun)
        {
            context.Info($"Limpieza local: {deleted} .rar eliminado(s), {reason}.");
        }

        return Task.CompletedTask;
    }
}