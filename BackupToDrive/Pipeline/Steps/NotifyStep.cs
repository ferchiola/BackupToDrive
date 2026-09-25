using BackupToDrive.Configuration;
using BackupToDrive.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackupToDrive.Pipeline.Steps;

public sealed class NotifyStep : IPipelineStep
{
    private readonly SmtpNotifier _notifier;
    private readonly MailOptions _mail;
    private readonly ILogger<NotifyStep> _logger;

    public NotifyStep(SmtpNotifier notifier, IOptions<AppOptions> options, ILogger<NotifyStep> logger)
    {
        _notifier = notifier;
        _mail = options.Value.Mail;
        _logger = logger;
    }

    public int Order => 40;
    public bool RunOnFailure => true;

    public bool IsEnabled(JobOptions job) => job.Steps.Notify && _mail.Enabled;

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        try
        {
            await _notifier.NotifyAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo enviar el mail de notificación.");
            context.Messages.Add($"Error al enviar mail: {ex.Message}");
        }
    }
}
