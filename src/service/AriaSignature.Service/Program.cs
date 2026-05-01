using AriaSignature.Application;
using AriaSignature.Infrastructure;
using AriaSignature.Service.Jobs;
using Quartz;
using AriaSignature.Service;
using Serilog;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<HostOptions>(options =>
{
    options.ServicesStartConcurrently = false;
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AriaSignatureService";
});

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Services.AddSerilog();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddHostedService<DatabaseInitializationHostedService>();
builder.Services.AddHostedService<TelemetryWarmupHostedService>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<BackupSchedulerHostedService>();
builder.Services.AddHostedService<LocalApiHostedService>();
builder.Services.AddQuartz(options =>
{
    var jobKey = new JobKey("smart-refresh-job");
    var cron = builder.Configuration.GetValue<string>("SmartMonitoring:Cron") ?? "0 */1 * * * ?";

    options.AddJob<SmartRefreshJob>(configure => configure.WithIdentity(jobKey));
    options.AddTrigger(configure => configure
        .ForJob(jobKey)
        .WithIdentity("smart-refresh-trigger")
        .WithCronSchedule(cron));
});
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

var host = builder.Build();
host.Run();
