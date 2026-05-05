using System.IO;
using AriaSignature.Application;
using AriaSignature.Application.Abstractions;
using AriaSignature.Infrastructure;
using AriaSignature.Service.Jobs;
using Quartz;
using AriaSignature.Service;
using Serilog;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "AriaSignature",
    "logs");
Directory.CreateDirectory(logDir);
var fatalStartupLogPath = Path.Combine(logDir, "service-startup-fatal.log");

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build())
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logDir, "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true)
    .CreateLogger();

void WriteFatalStartupLog(Exception ex, string stage)
{
    try
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{DateTimeOffset.Now:O} marker=fatal stage={stage}");
        sb.AppendLine($"machine={Environment.MachineName}");
        sb.AppendLine($"user={Environment.UserName}");
        sb.AppendLine($"cwd={Environment.CurrentDirectory}");
        sb.AppendLine($"baseDir={AppContext.BaseDirectory}");
        sb.AppendLine(ex.ToString());
        File.AppendAllText(fatalStartupLogPath, sb.ToString() + Environment.NewLine, Encoding.UTF8);
    }
    catch
    {
        // Last-resort fallback: fatal logging must never crash process harder.
    }
}

var startupStopwatch = Stopwatch.StartNew();
Log.Information(
    "marker=startup-enter machine={Machine}; user={User}; cwd={CurrentDir}; baseDir={BaseDir}; logDir={LogDir}; os={Os}; framework={Framework}; processArch={ProcessArch}; osArch={OsArch}",
    Environment.MachineName,
    Environment.UserName,
    Environment.CurrentDirectory,
    AppContext.BaseDirectory,
    logDir,
    RuntimeInformation.OSDescription,
    RuntimeInformation.FrameworkDescription,
    RuntimeInformation.ProcessArchitecture,
    RuntimeInformation.OSArchitecture);
try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.Configure<HostOptions>(options =>
    {
        options.ServicesStartConcurrently = true;
        options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
    });
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "AriaSignatureService";
    });

    builder.Services.AddSerilog();
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure();
    builder.Services.AddHostedService<LocalApiHostedService>();
    builder.Services.AddHostedService<DatabaseInitializationHostedService>();
    builder.Services.AddHostedService<Worker>();
    builder.Services.AddHostedService<BackupSchedulerHostedService>();
    builder.Services.AddSingleton<ISmartRefreshCronApplier, QuartzSmartRefreshCronApplier>();
    builder.Services.AddSingleton<IOutboundSyncCronApplier, QuartzOutboundSyncCronApplier>();
    builder.Services.AddHostedService<SmartMonitoringCronSyncHostedService>();
    builder.Services.AddHostedService<OutboundSyncCronSyncHostedService>();
    builder.Services.AddHttpClient(OutboundSyncJob.HttpClientName, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(120);
    });
    builder.Services.AddQuartz(options =>
    {
        var smartJobKey = new JobKey("smart-refresh-job");
        var smartCron = builder.Configuration.GetValue<string>("SmartMonitoring:Cron") ?? "0 0 * * * ?";

        options.AddJob<SmartRefreshJob>(configure => configure.WithIdentity(smartJobKey));
        options.AddTrigger(configure => configure
            .ForJob(smartJobKey)
            .WithIdentity("smart-refresh-trigger")
            .WithCronSchedule(smartCron));

        var outboundJobKey = new JobKey("outbound-sync-job");
        var outboundCron = builder.Configuration.GetValue<string>("OutboundSync:Cron") ?? "0 0/30 * * * ?";

        options.AddJob<OutboundSyncJob>(configure => configure.WithIdentity(outboundJobKey));
        options.AddTrigger(configure => configure
            .ForJob(outboundJobKey)
            .WithIdentity("outbound-sync-trigger")
            .WithCronSchedule(outboundCron));
    });
    builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

    var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogInformation("marker=host-built elapsedMs={ElapsedMs}", startupStopwatch.ElapsedMilliseconds);
    logger.LogInformation("marker=run-enter elapsedMs={ElapsedMs}", startupStopwatch.ElapsedMilliseconds);
    host.Run();
}
catch (Exception ex)
{
    WriteFatalStartupLog(ex, "startup-bootstrap");
    Log.Fatal(ex, "marker=fatal stage=startup-bootstrap");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
