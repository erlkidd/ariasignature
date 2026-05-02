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

var builder = Host.CreateApplicationBuilder(args);
var startupStopwatch = Stopwatch.StartNew();
builder.Services.Configure<HostOptions>(options =>
{
    options.ServicesStartConcurrently = true;
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AriaSignatureService";
});

var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "AriaSignature",
    "logs");
Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logDir, "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true)
    .CreateLogger();

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
logger.LogInformation("Host built in {ElapsedMs} ms; running service startup pipeline", startupStopwatch.ElapsedMilliseconds);
host.Run();
