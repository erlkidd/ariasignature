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
    if (args.Contains("--bootstrap-only", StringComparer.OrdinalIgnoreCase))
    {
        await RunBootstrapOnlyAsync();
        return;
    }

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

static async Task RunBootstrapOnlyAsync()
{
    var options = MelezhHostOptions.FromEnvironment();
    Directory.CreateDirectory(Path.GetDirectoryName(options.ProjectPath)!);

    if (!File.Exists(options.ProjectPath))
    {
        var exit = await MelezhBootstrapCli.RunAsync(
            options,
            MelezhCliCommands.BuildCreateProjectArgs(options.ProjectPath),
            CancellationToken.None);
        if (exit != 0)
        {
            throw new InvalidOperationException($"CreateProject failed exit={exit}");
        }
    }

    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
    var logger = loggerFactory.CreateLogger("MelezhBootstrapOnly");
    await MelezhProjectBootstrap.EnsureAsync(
        options,
        (args, ct) => MelezhBootstrapCli.RunAsync(options, args, ct),
        logger,
        CancellationToken.None);
    Console.WriteLine($"marker=melezh-bootstrap-only ok project={options.ProjectPath}");
}
