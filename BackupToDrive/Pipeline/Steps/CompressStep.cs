using BackupToDrive.Configuration;
using BackupToDrive.Services;
using Microsoft.Extensions.Logging;

namespace BackupToDrive.Pipeline.Steps;

public sealed class CompressStep : IPipelineStep
{
    private readonly WinRarCompressor _compressor;
    private readonly ILogger<CompressStep> _logger;

    public CompressStep(WinRarCompressor compressor, ILogger<CompressStep> logger)
    {
        _compressor = compressor;
        _logger = logger;
    }

    public int Order => 20;
    public bool RunOnFailure => false;

    public bool IsEnabled(JobOptions job) => job.Steps.Compress;

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        var pending = context.Artifacts
            .Where(a => a.Status is ArtifactStatus.Pending)
            .ToList();

        if (pending.Count == 0)
        {
            _logger.LogInformation("Nada para comprimir.");
            return;
        }

        if (context.DryRun)
        {
            foreach (var artifact in pending)
            {
                _logger.LogInformation(
                    "[dry-run] Comprimiría {Archivo} con WinRAR y borraría el original",
                    artifact.FileName);
            }

            return;
        }

        foreach (var artifact in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var rarPath = await _compressor.CompressAsync(
                    artifact.FullPath,
                    context.Job.Compress,
                    context.RunAt,
                    cancellationToken);

                var info = new FileInfo(rarPath);
                artifact.FullPath = info.FullName;
                artifact.FileName = info.Name;
                artifact.SizeBytes = info.Length;
                artifact.Status = ArtifactStatus.Compressed;
                artifact.Detail = "Comprimido con WinRAR";
                _logger.LogInformation("Comprimido: {Archivo} ({Size:N0} bytes)", info.Name, info.Length);
            }
            catch (Exception ex)
            {
                artifact.Status = ArtifactStatus.Failed;
                artifact.Detail = ex.Message;
                context.Fail($"Error al comprimir {artifact.FileName}: {ex.Message}");
                _logger.LogError(ex, "Error al comprimir {Archivo}", artifact.FileName);
            }
        }
    }
}
