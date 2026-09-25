using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using BackupToDrive.Configuration;
using BackupToDrive.Infrastructure;
using BackupToDrive.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackupToDrive.Services;

public sealed class SmtpNotifier
{
    private static readonly Regex Placeholder = new(@"\{\{[A-Za-z]+\}\}", RegexOptions.Compiled);

    private readonly MailOptions _mail;
    private readonly PathResolver _paths;
    private readonly ILogger<SmtpNotifier> _logger;

    public SmtpNotifier(IOptions<AppOptions> options, PathResolver paths, ILogger<SmtpNotifier> logger)
    {
        _mail = options.Value.Mail;
        _paths = paths;
        _logger = logger;
    }

    public async Task NotifyAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (!_mail.Enabled)
        {
            return;
        }

        var recipients = _mail.To.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
        if (string.IsNullOrWhiteSpace(_mail.SmtpHost) ||
            string.IsNullOrWhiteSpace(_mail.From) ||
            recipients.Length == 0)
        {
            _logger.LogWarning("Mail habilitado pero SmtpHost / From / To están incompletos.");
            return;
        }

        var failed = context.Failed || context.Artifacts.Any(a => a.Status == ArtifactStatus.Failed);
        if (failed && !_mail.NotifyOnFailure)
        {
            return;
        }

        if (!failed && !_mail.NotifyOnSuccess)
        {
            return;
        }

        var subject = failed
            ? $"[BackupToDrive] ERROR — {context.Job.Name}"
            : $"[BackupToDrive] OK — {context.Job.Name}";

        var templateName = failed ? _mail.FailureTemplate : _mail.SuccessTemplate;
        await SendTemplateAsync(context, subject, templateName, cancellationToken);
    }

    public async Task SendTemplateAsync(
        PipelineContext context,
        string subject,
        string templateName,
        CancellationToken cancellationToken)
    {
        if (!_mail.Enabled)
        {
            _logger.LogWarning("Mail deshabilitado. No se envió '{Asunto}'.", subject);
            return;
        }

        var recipients = _mail.To.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
        if (string.IsNullOrWhiteSpace(_mail.SmtpHost) ||
            string.IsNullOrWhiteSpace(_mail.From) ||
            recipients.Length == 0)
        {
            _logger.LogWarning("Mail habilitado pero SmtpHost / From / To están incompletos.");
            return;
        }

        var templatePath = ResolveTemplatePath(templateName);
        var failed = context.Failed || context.Artifacts.Any(a => a.Status == ArtifactStatus.Failed);
        var body = ApplyPlaceholders(File.ReadAllText(templatePath), context, failed);

        if (context.DryRun)
        {
            _logger.LogInformation(
                "[dry-run] Enviaría mail '{Asunto}' a {To} con {Plantilla}",
                subject,
                string.Join(", ", recipients),
                templatePath);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_mail.From),
            Subject = subject,
            Body = body,
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8
        };

        foreach (var to in recipients)
        {
            message.To.Add(to);
        }

        using var client = new SmtpClient(_mail.SmtpHost, _mail.SmtpPort)
        {
            EnableSsl = _mail.UseSsl
        };

        if (!string.IsNullOrWhiteSpace(_mail.User))
        {
            client.Credentials = new NetworkCredential(_mail.User, _mail.Password);
        }

        await client.SendMailAsync(message, cancellationToken);
        _logger.LogInformation("Mail enviado a {To} con {Plantilla}", string.Join(", ", recipients), templatePath);
    }

    private string ResolveTemplatePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(_mail.TemplatesFolder))
        {
            throw new InvalidOperationException("Mail.TemplatesFolder está vacío.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("El nombre de la plantilla de mail está vacío.");
        }

        var folder = _paths.Resolve(_mail.TemplatesFolder);
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No se encontró la plantilla de mail '{path}'.", path);
        }

        return path;
    }

    private static string ApplyPlaceholders(string html, PipelineContext context, bool failed)
    {
        var destino = DrivePathTemplate.Render(
            context.Job.DrivePathTemplate,
            context.Job.Name,
            context.RunAt);

        var mensaje = context.Messages.Count == 0
            ? "—"
            : string.Join("<br>", context.Messages.Select(WebUtility.HtmlEncode));

        var archivos = context.Artifacts.Count == 0
            ? "—"
            : string.Join("<br>", context.Artifacts.Select(FormatArtifact));

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{Job}}"] = WebUtility.HtmlEncode(context.Job.Name),
            ["{{Fecha}}"] = WebUtility.HtmlEncode(context.RunAt.ToString("yyyy-MM-dd HH:mm:ss")),
            ["{{Resultado}}"] = failed ? "ERROR" : "OK",
            ["{{Origen}}"] = WebUtility.HtmlEncode(context.Job.SourceFolder),
            ["{{Destino}}"] = WebUtility.HtmlEncode(destino),
            ["{{Mensaje}}"] = mensaje,
            ["{{Archivos}}"] = archivos,
            ["{{EspacioUsado}}"] = WebUtility.HtmlEncode(context.DriveSpace?.Used ?? "—"),
            ["{{EspacioTotal}}"] = WebUtility.HtmlEncode(context.DriveSpace?.Total ?? "—"),
            ["{{EspacioLibre}}"] = WebUtility.HtmlEncode(context.DriveSpace?.Free ?? "—"),
            ["{{DiasEstimados}}"] = WebUtility.HtmlEncode(context.DriveSpace?.Days ?? "—"),
            ["{{ArchivoReferencia}}"] = WebUtility.HtmlEncode(context.DriveSpace?.LastFile ?? "—"),
            ["{{UmbralDias}}"] = WebUtility.HtmlEncode(
                context.DriveSpace is null ? "—" : context.DriveSpace.ThresholdDays.ToString())
        };

        return Placeholder.Replace(html, match =>
            values.TryGetValue(match.Value, out var value) ? value : match.Value);
    }

    private static string FormatArtifact(BackupArtifact artifact)
    {
        var line = $"{artifact.FileName} · {StatusLabel(artifact.Status)} · {FormatSize(artifact.SizeBytes)}";
        if (!string.IsNullOrWhiteSpace(artifact.Detail))
        {
            line += $" · {artifact.Detail}";
        }

        return WebUtility.HtmlEncode(line);
    }

    private static string StatusLabel(ArtifactStatus status) => status switch
    {
        ArtifactStatus.Pending => "Pendiente",
        ArtifactStatus.Compressed => "Comprimido",
        ArtifactStatus.Uploaded => "Subido",
        ArtifactStatus.Skipped => "Omitido",
        ArtifactStatus.Failed => "Falló",
        _ => status.ToString()
    };

    private static string FormatSize(long bytes)
    {
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
