using AriaSignature.MelezhHost;
using Serilog;

var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "AriaSignature",
    "logs");
Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logDir, "melezh-host-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true)
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = MelezhHostOptions.DefaultServiceName;
    });
    builder.Services.AddSerilog();
    builder.Services.AddSingleton(MelezhHostOptions.FromEnvironment());
    builder.Services.AddHostedService<MelezhProcessWorker>();
    builder.Build().Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Melezh host failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
