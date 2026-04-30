using AriaSignature.Service;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AriaSignature Service";
});

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Services.AddSerilog();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<LocalApiHostedService>();

var host = builder.Build();
host.Run();
