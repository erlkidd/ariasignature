using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.ServiceProcess;
using System.Text;

namespace AriaSignature.WinSvc;

/// <summary>
/// Регистрация и запуск службы через sc.exe в том же виде, что и установщик Inno и UI.
/// </summary>
public static class WindowsServiceInstaller
{
    public const string ServiceName = "AriaSignatureService";

    private const int ErrorAccessDenied = 5;

    /// <summary>Win32 ERROR_SERVICE_REQUEST_TIMEOUT.</summary>
    private const int ErrorServiceRequestTimeout = 1053;

    /// <summary>Служба уже запущена.</summary>
    private const int ErrorServiceAlreadyRunning = 1056;
    private static readonly HttpClient StartupProbeHttp = new() { Timeout = TimeSpan.FromSeconds(2.5) };

    static WindowsServiceInstaller()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Полная переустановка записи службы в SCM и запуск (stop/delete/create/start).
    /// Логирует построчно в <paramref name="logLine"/> если задано (например файл bootstrap).
    /// </summary>
    public static ServiceSetupResult TryInstallAndStart(string serviceExePath, Action<string>? logLine)
    {
        void Log(string msg)
        {
            logLine?.Invoke(msg);
        }

        try
        {
            if (string.IsNullOrWhiteSpace(serviceExePath) || !File.Exists(serviceExePath))
            {
                var msg = $"Файл сервиса не найден: {serviceExePath}";
                Log(msg);
                return new ServiceSetupResult(false, msg, ServiceSetupFailureCategory.MissingServiceBinary, null);
            }

            var scPath = GetScExePath();
            if (string.IsNullOrEmpty(scPath))
            {
                const string msg = "Не найден sc.exe";
                Log(msg);
                return new ServiceSetupResult(false, msg, ServiceSetupFailureCategory.Unknown, null);
            }

            RunScBestEffort(scPath, $"stop {ServiceName}", Log);
            RunScBestEffort(scPath, $"delete {ServiceName}", Log);
            Thread.Sleep(1000);

            var createAttempts = new[]
            {
                $"create {ServiceName} binPath= \"{serviceExePath}\" start= auto DisplayName= \"AriaSignature\" obj= LocalSystem",
                $"create {ServiceName} binPath= \"{serviceExePath}\" start= auto DisplayName= \"AriaSignature Service\" obj= LocalSystem",
                $"create {ServiceName} binPath= \"{serviceExePath}\" start= auto obj= LocalSystem"
            };

            var created = false;
            var lastCreateExit = -1;
            var lastCreateOutput = string.Empty;
            foreach (var createArgs in createAttempts)
            {
                Log($"sc {createArgs}");
                if (RunSc(scPath, createArgs, out var createExit, out var createOutput))
                {
                    created = true;
                    break;
                }

                lastCreateExit = createExit;
                lastCreateOutput = createOutput;
                Log($"sc exit={createExit}: {createOutput}");
                if (createExit == ErrorAccessDenied)
                {
                    return ClassifyScFailure(createExit, createOutput, ServiceSetupFailureCategory.AccessDenied);
                }

                if (createExit == 1073)
                {
                    created = true;
                    break;
                }

                if (createExit != 1078)
                {
                    break;
                }
            }

            if (!created)
            {
                var msg = $"Не удалось создать службу (sc create). Код: {lastCreateExit}. Вывод: {lastCreateOutput}";
                Log(msg);
                return ClassifyScFailure(lastCreateExit, lastCreateOutput, ServiceSetupFailureCategory.ScCommandFailed);
            }

            RunScBestEffort(scPath, $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000", Log);

            Log($"sc start {ServiceName}");
            var startOk = RunSc(scPath, $"start {ServiceName}", out var startExit, out var startOutput);
            Log($"sc start exit={startExit}: {startOutput}");

            if (!startOk && startExit == ErrorServiceAlreadyRunning)
            {
                startOk = true;
            }

            if (!startOk && startExit == ErrorAccessDenied)
            {
                return ClassifyScFailure(startExit, startOutput, ServiceSetupFailureCategory.AccessDenied);
            }

            if (!startOk && startExit == ErrorServiceRequestTimeout)
            {
                Log($"sc start returned {ErrorServiceRequestTimeout}; entering extended post-1053 verification");
                var post1053 = VerifyAfterScmTimeout1053(Log);
                if (post1053.Success)
                {
                    startOk = true;
                }
                else
                {
                    var msg = $"{post1053.ErrorMessage} Вывод sc: {startOutput}";
                    return new ServiceSetupResult(false, msg, post1053.Category, startExit);
                }
            }
            else if (!startOk)
            {
                var msg = $"Не удалось запустить службу (sc start). Код: {startExit}. Вывод: {startOutput}";
                Log(msg);
                return ClassifyScFailure(startExit, startOutput, ServiceSetupFailureCategory.ScCommandFailed);
            }

            using var sc = new ServiceController(ServiceName);
            try
            {
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(120));
            }
            catch (Exception ex)
            {
                var msg = $"Служба не перешла в Running после start: {ex.Message}";
                Log(msg);
                return new ServiceSetupResult(false, msg, ServiceSetupFailureCategory.ServiceStartTimeout, null);
            }

            Log("Служба Running.");
            return new ServiceSetupResult(true, null, ServiceSetupFailureCategory.None, null);
        }
        catch (Exception ex)
        {
            Log($"Исключение: {ex}");
            return new ServiceSetupResult(false, ex.Message, ServiceSetupFailureCategory.Unknown, null);
        }
    }

    public static string BuildUserHint(ServiceSetupResult result)
    {
        if (result.Success)
        {
            return string.Empty;
        }

        var baseHint = result.Category switch
        {
            ServiceSetupFailureCategory.AccessDenied =>
                "Недостаточно прав для управления службой. Подтвердите запрос UAC или запустите приложение от имени администратора; при необходимости откройте services.msc.",
            ServiceSetupFailureCategory.MissingServiceBinary =>
                "Не найден исполняемый файл службы. Переустановите приложение или проверьте целостность установки.",
            ServiceSetupFailureCategory.ServiceCrashedOnStart =>
                "Процесс службы завершился при старте. Проверьте журналы в %ProgramData%\\AriaSignature\\logs\\.",
            ServiceSetupFailureCategory.ServiceStartTimeout =>
                "Таймаут SCM (1053) при переходе службы в состояние «Работает». Выполнены дополнительное ожидание и probe API; возможно, холодный старт блокируют антивирус/диск.",
            ServiceSetupFailureCategory.NotFoundOrDeleted =>
                "Запись службы не найдена или удалена другим процессом. Перезапустите установщик или восстановление службы.",
            ServiceSetupFailureCategory.ScCommandFailed =>
                "Команда sc.exe завершилась с ошибкой.",
            _ =>
                "Не удалось запустить или зарегистрировать службу Windows."
        };

        var tail = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? string.Empty
            : $" Детали: {result.ErrorMessage}";

        return baseHint + tail;
    }

    private static ServiceSetupResult ClassifyScFailure(int exitCode, string output, ServiceSetupFailureCategory fallback)
    {
        var category = exitCode == ErrorAccessDenied
            ? ServiceSetupFailureCategory.AccessDenied
            : fallback;

        var msg = $"Команда sc завершилась с кодом {exitCode}. Вывод: {output}";
        return new ServiceSetupResult(false, msg, category, exitCode);
    }

    private static ServiceSetupResult VerifyAfterScmTimeout1053(Action<string>? logLine)
    {
        var startedAt = DateTime.UtcNow;
        var timeline = new List<string>();
        for (var i = 0; i < 24; i++)
        {
            Thread.Sleep(5000);
            ServiceControllerStatus? status = null;
            try
            {
                using var scProbe = new ServiceController(ServiceName);
                scProbe.Refresh();
                status = scProbe.Status;
            }
            catch
            {
                // ignore
            }

            var hostAlive = AnyAriaSignatureServiceHostProcessExists();
            var elapsed = (DateTime.UtcNow - startedAt).TotalSeconds;
            var row = $"t+{elapsed:F0}s status={(status?.ToString() ?? "unknown")} host={(hostAlive ? "alive" : "missing")}";
            timeline.Add(row);
            logLine?.Invoke("service status timeline: " + row);

            if (status == ServiceControllerStatus.Running)
            {
                var (ready, detail) = TryProbeApiReadyOnce("http://127.0.0.1:5160");
                logLine?.Invoke($"api probe result: ready={ready} detail={detail}");
                if (ready)
                {
                    return new ServiceSetupResult(true, null, ServiceSetupFailureCategory.None, null);
                }
            }

            if (status == ServiceControllerStatus.Stopped && !hostAlive)
            {
                var msg =
                    "stage=post-1053-check: служба остановлена и процесс AriaSignature.Service не найден (вероятно падение на холодном старте). "
                    + "См. %ProgramData%\\AriaSignature\\logs\\.";
                logLine?.Invoke(msg);
                return new ServiceSetupResult(false, msg, ServiceSetupFailureCategory.ServiceCrashedOnStart, ErrorServiceRequestTimeout);
            }
        }

        var timelineSummary = string.Join(" | ", timeline);
        var timeoutMsg =
            "stage=post-1053-check: дополнительная проверка не подтвердила рабочий запуск службы/API после таймаута SCM. "
            + $"timeline={timelineSummary}";
        logLine?.Invoke(timeoutMsg);
        return new ServiceSetupResult(false, timeoutMsg, ServiceSetupFailureCategory.ServiceStartTimeout, ErrorServiceRequestTimeout);
    }

    private static (bool Ready, string Detail) TryProbeApiReadyOnce(string baseUrl)
    {
        try
        {
            using var statusResponse = StartupProbeHttp.GetAsync($"{baseUrl.TrimEnd('/')}/api/v1/status").GetAwaiter().GetResult();
            if (!statusResponse.IsSuccessStatusCode)
            {
                return (false, $"/api/v1/status => {(int)statusResponse.StatusCode}");
            }

            using var rootResponse = StartupProbeHttp.GetAsync($"{baseUrl.TrimEnd('/')}/").GetAwaiter().GetResult();
            return rootResponse.IsSuccessStatusCode
                ? (true, "ready")
                : (false, $"/ => {(int)rootResponse.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static bool AnyAriaSignatureServiceHostProcessExists()
    {
        try
        {
            return Process.GetProcessesByName("AriaSignature.Service").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetScExePath()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            Path.Combine(winDir, "System32", "sc.exe"),
            Path.Combine(winDir, "Sysnative", "sc.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static void RunScBestEffort(string scPath, string args, Action<string>? logLine)
    {
        RunSc(scPath, args, out var exit, out var output);
        logLine?.Invoke($"sc {args} exit={exit}: {output}");
    }

    private static Encoding TryGetScOutputEncoding()
    {
        try
        {
            var cp = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            if (cp > 0)
            {
                return Encoding.GetEncoding(cp);
            }
        }
        catch
        {
            // fall through
        }

        try
        {
            return Encoding.GetEncoding(866);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static void ApplyScConsoleEncoding(ProcessStartInfo psi)
    {
        try
        {
            var enc = TryGetScOutputEncoding();
            psi.StandardOutputEncoding = enc;
            psi.StandardErrorEncoding = enc;
        }
        catch
        {
            // ignore
        }
    }

    internal static bool RunSc(string scPath, string args, out int exitCode, out string output)
    {
        output = string.Empty;
        var psi = new ProcessStartInfo
        {
            FileName = scPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        ApplyScConsoleEncoding(psi);
        using var proc = Process.Start(psi);
        if (proc is null)
        {
            exitCode = -1;
            return false;
        }

        var stdOut = proc.StandardOutput.ReadToEnd();
        var stdErr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(20000);
        exitCode = proc.ExitCode;
        output = string.IsNullOrWhiteSpace(stdErr)
            ? stdOut
            : string.IsNullOrWhiteSpace(stdOut)
                ? stdErr
                : $"{stdOut}{Environment.NewLine}{stdErr}";
        return exitCode == 0;
    }
}
