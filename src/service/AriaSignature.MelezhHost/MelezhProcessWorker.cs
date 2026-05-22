using System.Diagnostics;
using System.Text;

namespace AriaSignature.MelezhHost;

public sealed class MelezhProcessWorker : BackgroundService
{
    private readonly MelezhHostOptions _options;
    private readonly ILogger<MelezhProcessWorker> _logger;
    private volatile bool _requestMelezhProcessRecycle;

    public MelezhProcessWorker(MelezhHostOptions options, ILogger<MelezhProcessWorker> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Melezh host starting. oscript={Oscript}; app={App}; project={Project}; port={Port}",
            _options.OscriptExePath,
            _options.MelezhAppOsPath,
            _options.ProjectPath,
            _options.Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!IsRuntimeReady())
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            try
            {
                await EnsureProjectExistsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to initialize Melezh project");
                await Task.Delay(_options.RestartDelay, stoppingToken);
                continue;
            }

            using var process = StartMelezhProcess();
            if (process is null)
            {
                await Task.Delay(_options.RestartDelay, stoppingToken);
                continue;
            }

            try
            {
                await process.WaitForExitAsync(stoppingToken);
                _logger.LogWarning("melezh exited with code {ExitCode}", process.ExitCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                TryStopProcess(process);
                break;
            }

            if (_requestMelezhProcessRecycle)
            {
                _requestMelezhProcessRecycle = false;
                _logger.LogInformation("Recycling melezh process after bootstrap upgrade");
                continue;
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_options.RestartDelay, stoppingToken);
            }
        }
    }

    private bool IsRuntimeReady()
    {
        if (!File.Exists(_options.OscriptExePath))
        {
            _logger.LogError("oscript.exe not found at {Path}", _options.OscriptExePath);
            return false;
        }

        if (!File.Exists(_options.MelezhAppOsPath))
        {
            _logger.LogError("Melezh app.os not found at {Path}", _options.MelezhAppOsPath);
            return false;
        }

        if (!File.Exists(_options.MelezhExePath))
        {
            _logger.LogWarning("Melezh launcher not found at {Path} (oscript path will be used)", _options.MelezhExePath);
        }

        return true;
    }

    private async Task EnsureProjectExistsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_options.ProjectPath))
        {
            var templatePath = ResolveBundledTemplatePath();
            if (templatePath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_options.ProjectPath)!);
                File.Copy(templatePath, _options.ProjectPath, overwrite: false);
                _logger.LogInformation("Copied Melezh project template from {Template}", templatePath);
            }
            else
            {
                _logger.LogInformation("Creating Melezh project at {Path}", _options.ProjectPath);
                var args = MelezhCliCommands.BuildCreateProjectArgs(_options.ProjectPath);
                var exitCode = await RunMelezhCliAsync(args, cancellationToken);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"{MelezhCliCommands.CreateProjectMethod} failed with exit code {exitCode}");
                }
            }
        }

        var bootstrapResult = await MelezhProjectBootstrap.EnsureAsync(
            _options,
            RunMelezhCliAsync,
            _logger,
            cancellationToken);

        if (bootstrapResult.Upgraded)
        {
            _requestMelezhProcessRecycle = true;
            _logger.LogInformation(
                "Melezh bootstrap upgraded {From} -> {To}; melezh process will recycle to reset OInt HTTP client",
                bootstrapResult.PreviousVersion,
                bootstrapResult.CurrentVersion);
        }
    }

    private string? ResolveBundledTemplatePath()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var installRoot = Path.GetFullPath(Path.Combine(baseDir, ".."));
        var candidate = Path.Combine(installRoot, "melezh", "templates", "AriaSignature.melezh");
        return File.Exists(candidate) ? candidate : null;
    }

    private Process? StartMelezhProcess()
    {
        var args = MelezhCliCommands.BuildRunProjectArgs(_options.ProjectPath, _options.Port);
        _logger.LogInformation("Starting melezh via oscript: {Args}", args);
        try
        {
            var (fileName, arguments, workingDirectory) = MelezhCliCommands.ResolveOscriptInvocation(
                _options.OscriptExePath,
                _options.MelezhAppOsPath,
                args);
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _logger.LogInformation("melezh> {Line}", e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _logger.LogWarning("melezh! {Line}", e.Data);
                }
            };

            if (!process.Start())
            {
                _logger.LogError("Failed to start melezh process");
                process.Dispose();
                return null;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch melezh");
            return null;
        }
    }

    private Task<int> RunMelezhCliAsync(string cliArguments, CancellationToken cancellationToken) =>
        MelezhBootstrapCli.RunAsync(_options, cliArguments, cancellationToken);

    private static void TryStopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore shutdown races
        }
    }
}
