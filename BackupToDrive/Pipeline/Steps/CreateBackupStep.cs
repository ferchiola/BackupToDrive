using BackupToDrive.Configuration;
using BackupToDrive.Services;
using Microsoft.Extensions.Logging;

namespace BackupToDrive.Pipeline.Steps;

public sealed class CreateBackupStep : IPipelineStep
{
    private readonly SqlServerBackup _backup;
    private readonly ILogger<CreateBackupStep> _logger;

    public CreateBackupStep(SqlServerBackup backup, ILogger<CreateBackupStep> logger)
    {
        _backup = backup;
        _logger = logger;
    }

    public int Order => 10;
    public bool RunOnFailure => false;

    public bool IsEnabled(JobOptions job) => job.Steps.CreateBackup;

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        try
        {
            var artifact = await _backup.CreateAsync(
                context.Job,
                context.RunAt,
                context.DryRun,
                cancellationToken);

            if (artifact is not null)
            {
                context.Artifacts.Add(artifact);
            }
        }
        catch (Exception ex)
        {
            var message = $"Error al crear el backup del job {context.Job.Name}: {ex.Message}";
            context.Fail(message);
            _logger.LogError(ex, "Error al crear el backup del job {Job}", context.Job.Name);
        }
    }
}
