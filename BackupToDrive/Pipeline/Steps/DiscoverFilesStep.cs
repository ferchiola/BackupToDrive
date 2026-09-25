using BackupToDrive.Configuration;
using Microsoft.Extensions.Logging;

namespace BackupToDrive.Pipeline.Steps;

public sealed class DiscoverFilesStep : IPipelineStep
{
    private readonly ILogger<DiscoverFilesStep> _logger;

    public DiscoverFilesStep(ILogger<DiscoverFilesStep> logger)
    {
        _logger = logger;
    }

    public int Order => 15;
    public bool RunOnFailure => false;

    public bool IsEnabled(JobOptions job) => true;

    public Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        var job = context.Job;
        if (!Directory.Exists(job.SourceFolder))
        {
            var message = $"No existe la carpeta origen '{job.SourceFolder}'.";
            _logger.LogError("{Mensaje}", message);
            context.Fail(message);
            return Task.CompletedTask;
        }

        var option = job.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var patterns = job.SearchPattern
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (patterns.Length == 0)
        {
            patterns = ["*.*"];
        }

        var minWriteTime = job.MinAgeMinutes > 0
            ? DateTime.Now.AddMinutes(-job.MinAgeMinutes)
            : DateTime.MaxValue;

        var seen = new HashSet<string>(
            context.Artifacts.Select(a => a.FullPath),
            StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in patterns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in Directory.EnumerateFiles(job.SourceFolder, pattern, option))
            {
                if (!seen.Add(path))
                {
                    continue;
                }

                // El .rar lo produce este job. Si se vuelve a tomar, WinRAR lo compacta de nuevo,
                // borra el anterior con -df y se sube otra vez.
                if (job.Steps.Compress &&
                    path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Se deja el .rar ya generado: {Archivo}", Path.GetFileName(path));
                    continue;
                }

                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0)
                {
                    _logger.LogDebug("Se ignora vacío: {Archivo}", path);
                    continue;
                }

                if (job.MinAgeMinutes > 0 && info.LastWriteTime > minWriteTime)
                {
                    _logger.LogInformation(
                        "Todavía reciente ({Minutos} min): {Archivo}",
                        job.MinAgeMinutes,
                        info.Name);
                    continue;
                }

                if (!IsReadable(path))
                {
                    _logger.LogWarning("Archivo en uso, se omite: {Archivo}", info.Name);
                    continue;
                }

                context.Artifacts.Add(new BackupArtifact
                {
                    FullPath = info.FullName,
                    FileName = info.Name,
                    SizeBytes = info.Length
                });
            }
        }

        _logger.LogInformation(
            "Job {Job}: {Cantidad} archivo(s) para procesar.",
            job.Name,
            context.Artifacts.Count);

        if (context.Artifacts.Count == 0)
        {
            context.Info("No hay archivos para subir.");
        }

        return Task.CompletedTask;
    }

    private static bool IsReadable(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
