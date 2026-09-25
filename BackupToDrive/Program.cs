using BackupToDrive;
using BackupToDrive.Configuration;
using BackupToDrive.Infrastructure;
using BackupToDrive.Pipeline;
using BackupToDrive.Pipeline.Steps;
using BackupToDrive.Services;
using BackupToDrive.Services.Drive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

var dryRun = HasFlag(args, "--dry-run");
var jobName = GetOption(args, "--job");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(AppContext.BaseDirectory, "logs", "backup-to-drive-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

var exitCode = 0;
try
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });
    builder.Services.AddSerilog();
    builder.Services.Configure<AppOptions>(builder.Configuration);

    builder.Services.AddSingleton<PathResolver>();
    builder.Services.AddSingleton<IDriveServiceFactory, DriveServiceFactory>();
    builder.Services.AddSingleton<IGoogleDriveUploader, GoogleDriveUploader>();
    builder.Services.AddSingleton<WinRarCompressor>();
    builder.Services.AddSingleton<SqlServerBackup>();
    builder.Services.AddSingleton<SmtpNotifier>();
    builder.Services.AddSingleton<BackupPipeline>();
    builder.Services.AddSingleton<JobRunner>();

    builder.Services.AddSingleton<IPipelineStep, CreateBackupStep>();
    builder.Services.AddSingleton<IPipelineStep, DiscoverFilesStep>();
    builder.Services.AddSingleton<IPipelineStep, CompressStep>();
    builder.Services.AddSingleton<IPipelineStep, UploadStep>();
    builder.Services.AddSingleton<IPipelineStep, CheckDriveSpaceStep>();
    builder.Services.AddSingleton<IPipelineStep, CleanupLocalStep>();
    builder.Services.AddSingleton<IPipelineStep, NotifyStep>();

    using var host = builder.Build();
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var runner = host.Services.GetRequiredService<JobRunner>();
    exitCode = await runner.RunAsync(dryRun, jobName, cts.Token);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Error fatal al iniciar BackupToDrive");
    exitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// El flujo de OAuth de Google (Google.Apis.Auth) deja un HttpListener en un hilo
// foreground esperando el redirect del navegador; aunque el login ya haya terminado,
// ese hilo puede seguir vivo y evitar que el proceso termine solo al llegar acá.
// Environment.Exit fuerza el cierre real del proceso, haya terminado bien o mal.
Environment.Exit(exitCode);

static bool HasFlag(string[] args, string flag) =>
    args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
        {
            return args[i + 1];
        }

        return null;
    }

    return null;
}
