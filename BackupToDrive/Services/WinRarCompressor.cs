using System.Diagnostics;
using System.Text;
using BackupToDrive.Configuration;
using Microsoft.Extensions.Logging;

namespace BackupToDrive.Services;

public sealed class WinRarCompressor
{
    private readonly ILogger<WinRarCompressor> _logger;

    public WinRarCompressor(ILogger<WinRarCompressor> logger)
    {
        _logger = logger;
    }

    public async Task<string> CompressAsync(
        string sourceFile,
        CompressOptions options,
        DateTime runAt,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(options.WinRarPath))
        {
            throw new FileNotFoundException(
                $"No se encontró WinRAR en '{options.WinRarPath}'. Instalá WinRAR o ajustá Compress.WinRarPath.");
        }

        var outputFolder = string.IsNullOrWhiteSpace(options.OutputFolder)
            ? Path.GetDirectoryName(sourceFile) ?? Directory.GetCurrentDirectory()
            : options.OutputFolder;

        Directory.CreateDirectory(outputFolder);

        var stem = Path.GetFileNameWithoutExtension(sourceFile);
        var stamp = runAt.ToString("yyyyMMdd-HHmmss");
        if (!stem.EndsWith("-" + stamp, StringComparison.Ordinal))
        {
            stem += "-" + stamp;
        }

        var rarName = stem + ".rar";
        var rarPath = Path.Combine(outputFolder, rarName);

        var arguments = new StringBuilder("a ");
        if (!string.IsNullOrWhiteSpace(options.ExtraArguments))
        {
            arguments.Append(options.ExtraArguments.Trim()).Append(' ');
        }

        // -df: WinRAR borra el origen solo después de compactarlo bien.
        if (!HasSwitch(options.ExtraArguments, "df"))
        {
            arguments.Append("-df ");
        }

        arguments.Append('"').Append(rarPath).Append("\" \"").Append(sourceFile).Append('"');

        _logger.LogInformation("WinRAR: {Exe} {Args}", options.WinRarPath, arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = options.WinRarPath,
            Arguments = arguments.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("No se pudo iniciar WinRAR.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, options.TimeoutMinutes)));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"WinRAR superó el timeout de {options.TimeoutMinutes} minutos para '{sourceFile}'.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var output = await stdout;
        var error = await stderr;

        // 0 = OK, 1 = warning (archivo bloqueado / no fatal). Ambos se consideran éxito.
        if (process.ExitCode > 1)
        {
            _logger.LogError("WinRAR falló ({Code}). {Error}{Output}", process.ExitCode, error, output);
            throw new InvalidOperationException($"WinRAR salió con código {process.ExitCode}.");
        }

        var rarInfo = new FileInfo(rarPath);
        if (!rarInfo.Exists || rarInfo.Length == 0)
        {
            throw new FileNotFoundException("WinRAR terminó pero no se encontró el .rar de salida.", rarPath);
        }

        if (process.ExitCode == 0)
        {
            DeleteCompressedSource(sourceFile);
        }
        else
        {
            _logger.LogWarning(
                "WinRAR terminó con advertencia ({Code}). Se conserva {Archivo}.",
                process.ExitCode,
                Path.GetFileName(sourceFile));
        }

        return rarPath;
    }

    private void DeleteCompressedSource(string sourceFile)
    {
        if (!File.Exists(sourceFile))
        {
            _logger.LogInformation(
                "WinRAR eliminó el archivo compactado: {Archivo}",
                Path.GetFileName(sourceFile));
            return;
        }

        File.Delete(sourceFile);
        _logger.LogInformation("Eliminado el archivo compactado: {Archivo}", Path.GetFileName(sourceFile));
    }

    private static bool HasSwitch(string? arguments, string name)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        return arguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(token =>
                token.Equals("-" + name, StringComparison.OrdinalIgnoreCase) ||
                token.Equals("/" + name, StringComparison.OrdinalIgnoreCase));
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }
    }
}
