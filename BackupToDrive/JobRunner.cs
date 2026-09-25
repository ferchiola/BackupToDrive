using BackupToDrive.Configuration;
using BackupToDrive.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackupToDrive;

public sealed class JobRunner
{
    private readonly AppOptions _options;
    private readonly BackupPipeline _pipeline;
    private readonly ILogger<JobRunner> _logger;

    public JobRunner(IOptions<AppOptions> options, BackupPipeline pipeline, ILogger<JobRunner> logger)
    {
        _options = options.Value;
        _pipeline = pipeline;
        _logger = logger;
    }

    public async Task<int> RunAsync(bool dryRun, string? jobName, CancellationToken cancellationToken)
    {
        var jobs = _options.Jobs
            .Where(j => j.Enabled)
            .Where(j => string.IsNullOrWhiteSpace(jobName) ||
                        j.Name.Equals(jobName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (jobs.Count == 0)
        {
            _logger.LogError(
                string.IsNullOrWhiteSpace(jobName)
                    ? "No hay jobs habilitados en appsettings.json."
                    : "No hay un job habilitado llamado '{Job}'.",
                jobName);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(_options.GoogleDrive.RootFolderId) &&
            jobs.Any(j => j.Steps.Upload) &&
            !dryRun)
        {
            _logger.LogWarning(
                "GoogleDrive.RootFolderId está vacío. " +
                "Para Service Account, compartí una carpeta de Drive con el email de la cuenta " +
                "y pegá el ID de esa carpeta (aparece en la URL: /folders/ID).");
        }

        var anyFailed = false;
        var runAt = DateTime.Now;

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("=== Job {Job} ===", job.Name);

            if (string.IsNullOrWhiteSpace(job.SourceFolder))
            {
                _logger.LogError("Job {Job}: SourceFolder no está configurado.", job.Name);
                anyFailed = true;
                continue;
            }

            var context = new PipelineContext
            {
                Job = job,
                RunAt = runAt,
                DryRun = dryRun
            };

            try
            {
                await _pipeline.ExecuteAsync(context, cancellationToken);
            }
            catch (Exception ex)
            {
                context.Fail(ex.Message);
                _logger.LogError(ex, "Job {Job} falló con una excepción no controlada.", job.Name);
            }

            LogSummary(context);
            anyFailed |= context.Failed;
        }

        return anyFailed ? 1 : 0;
    }

    private void LogSummary(PipelineContext context)
    {
        var uploaded = context.Artifacts.Count(a => a.Status == ArtifactStatus.Uploaded);
        var skipped = context.Artifacts.Count(a => a.Status == ArtifactStatus.Skipped);
        var failed = context.Artifacts.Count(a => a.Status == ArtifactStatus.Failed);

        _logger.LogInformation(
            "Job {Job}: {Estado} — subidos {Uploaded}, omitidos {Skipped}, fallidos {Failed}, total {Total}",
            context.Job.Name,
            context.Failed ? "ERROR" : "OK",
            uploaded,
            skipped,
            failed,
            context.Artifacts.Count);
    }
}
