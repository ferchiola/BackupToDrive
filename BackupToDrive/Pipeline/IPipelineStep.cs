using BackupToDrive.Configuration;

namespace BackupToDrive.Pipeline;

public interface IPipelineStep
{
    int Order { get; }
    bool RunOnFailure { get; }
    bool IsEnabled(JobOptions job);
    Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken);
}
