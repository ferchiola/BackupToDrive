using Microsoft.Extensions.Logging;

namespace BackupToDrive.Pipeline;

public sealed class BackupPipeline
{
    private readonly IReadOnlyList<IPipelineStep> _steps;
    private readonly ILogger<BackupPipeline> _logger;

    public BackupPipeline(IEnumerable<IPipelineStep> steps, ILogger<BackupPipeline> logger)
    {
        _steps = steps.OrderBy(s => s.Order).ToArray();
        _logger = logger;
    }

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        foreach (var step in _steps)
        {
            if (!step.IsEnabled(context.Job))
            {
                continue;
            }

            if (context.Failed && !step.RunOnFailure)
            {
                continue;
            }

            var name = step.GetType().Name;
            _logger.LogInformation("Paso {Paso} (job {Job})", name, context.Job.Name);
            await step.ExecuteAsync(context, cancellationToken);
        }
    }
}
