using BackupToDrive.Configuration;
using BackupToDrive.Services;
using BackupToDrive.Services.Drive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackupToDrive.Pipeline.Steps;

public sealed class CheckDriveSpaceStep : IPipelineStep
{
    private readonly IGoogleDriveUploader _uploader;
    private readonly SmtpNotifier _notifier;
    private readonly GoogleDriveOptions _drive;
    private readonly MailOptions _mail;
    private readonly ILogger<CheckDriveSpaceStep> _logger;

    public CheckDriveSpaceStep(
        IGoogleDriveUploader uploader,
        SmtpNotifier notifier,
        IOptions<AppOptions> options,
        ILogger<CheckDriveSpaceStep> logger)
    {
        _uploader = uploader;
        _notifier = notifier;
        _drive = options.Value.GoogleDrive;
        _mail = options.Value.Mail;
        _logger = logger;
    }

    public int Order => 32;
    public bool RunOnFailure => true;

    public bool IsEnabled(JobOptions job) => job.Steps.Upload;

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        DriveQuota quota;
        try
        {
            quota = await _uploader.GetQuotaAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo consultar el espacio de Drive.");
            context.Info($"No se pudo consultar el espacio de Drive: {ex.Message}");
            return;
        }

        var last = context.Artifacts.LastOrDefault(a => a.Status == ArtifactStatus.Uploaded)
            ?? context.Artifacts.LastOrDefault(a => a.Status == ArtifactStatus.Skipped)
            ?? (context.DryRun
                ? context.Artifacts.LastOrDefault(a =>
                    a.Status is ArtifactStatus.Pending or ArtifactStatus.Compressed)
                : null);

        long? free = quota.LimitBytes is null ? null : quota.LimitBytes.Value - quota.UsageBytes;
        int? days = free is null || last is null || last.SizeBytes <= 0
            ? null
            : (int)Math.Max(0, free.Value / last.SizeBytes);

        var alert = days is not null && _drive.SpaceAlertDays > 0 && days.Value < _drive.SpaceAlertDays;
        var report = new DriveSpaceReport
        {
            Used = FormatSize(quota.UsageBytes),
            Total = quota.LimitBytes is null ? "sin tope" : FormatSize(quota.LimitBytes.Value),
            Free = free is null ? "sin tope" : FormatSize(Math.Max(0, free.Value)),
            Days = days is null ? "—" : days.Value.ToString(),
            LastFile = last is null ? "—" : $"{last.FileName} ({FormatSize(last.SizeBytes)})",
            ThresholdDays = _drive.SpaceAlertDays,
            Alert = alert
        };
        context.DriveSpace = report;

        _logger.LogInformation(
            "Drive: usado {Usado} de {Total}, libre {Libre}. Último archivo {Archivo}: alcanza para {Dias} días.",
            report.Used,
            report.Total,
            report.Free,
            report.LastFile,
            report.Days);

        if (!alert)
        {
            return;
        }

        _logger.LogWarning(
            "Espacio de Drive bajo: {Dias} días, umbral {Umbral}.",
            report.Days,
            report.ThresholdDays);

        try
        {
            await _notifier.SendTemplateAsync(
                context,
                $"[BackupToDrive] ESPACIO — {context.Job.Name}",
                _mail.SpaceAlertTemplate,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo enviar el mail de alerta de espacio.");
            context.Info($"Error al enviar la alerta de espacio: {ex.Message}");
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d / 1024d:0.#} GB";
        }

        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:0.#} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:0.#} KB";
        }

        return $"{bytes} bytes";
    }
}
