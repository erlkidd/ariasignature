using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Text;

namespace AriaSignature.UI.Services;

/// <summary>
/// Гарантирует, что фоновая служба с API запущена (имя совпадает с установщиком Inno Setup).
/// </summary>
public static class WindowsServiceEnsure
{
    public const string ServiceName = "AriaSignatureService";

    /// <summary>Win32 ERROR_SERVICE_REQUEST_TIMEOUT — SCM не дождался ответа службы за отведённое время.</summary>
    private const int ErrorServiceRequestTimeout = 1053;

    /// <summary>Служба уже запущена.</summary>
    private const int ErrorServiceAlreadyRunning = 1056;

    static WindowsServiceEnsure()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static void TryStartOrFallback(string serviceExePath, TimeSpan wait, out string? warningMessage)
    {
        warningMessage = null;
        try
        {
            KillOrphanServiceProcessesBestEffort();
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                return;
            }

            if (sc.Status == ServiceControllerStatus.StartPending)
            {
                sc.WaitForStatus(ServiceControllerStatus.Running, wait);
                return;
            }

            try
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, wait);
                return;
            }
            catch (InvalidOperationException ex) when (TryFindNativeError(ex, out var code) && code == ErrorServiceRequestTimeout)
            {
                using var scAfter1053 = new ServiceController(ServiceName);
                scAfter1053.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(120));
                return;
            }
        }
        catch (InvalidOperationException ex)
        {
            if (TryInstallAndStartWindowsService(serviceExePath, out var installError))
            {
                warningMessage = null;
                return;
            }

            warningMessage =
                $"Не удалось запустить фоновую службу.\n{ex.Message}\n{installError}";
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            warningMessage = "Превышено время ожидания запуска фонового сервиса. Проверьте оснастку «Службы» (services.msc).";
        }
        catch (Exception ex)
        {
            warningMessage = $"Не удалось запустить фоновый сервис (службу): {ex.Message}";
        }
    }

    /// <summary>
    /// Запускает службу и ждёт Running. Если SCM вернёт 1053 (таймаут ответа службы), дополнительно ждём до 120 с —
    /// процесс мог успеть перейти в Running после сообщения об ошибке.
    /// </summary>
    public static void StartServiceAllowingScmTimeout1053(ServiceController sc, TimeSpan primaryWait)
    {
        try
        {
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, primaryWait);
        }
        catch (InvalidOperationException ex) when (TryFindNativeError(ex, out var code) && code == ErrorServiceRequestTimeout)
        {
            using var fresh = new ServiceController(ServiceName);
            fresh.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(120));
        }
    }

    /// <summary>Процесс исполняемого файла службы (не обязательно совпадает с PID из SCM).</summary>
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

    private static bool TryFindNativeError(Exception ex, out int nativeErrorCode)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is Win32Exception w32)
            {
                nativeErrorCode = w32.NativeErrorCode;
                return true;
            }
        }

        nativeErrorCode = 0;
        return false;
    }

    private static void KillOrphanServiceProcessesBestEffort()
    {
        try
        {
            var activeServicePid = TryGetServiceProcessId();
            var currentPid = Process.GetCurrentProcess().Id;
            foreach (var process in Process.GetProcessesByName("AriaSignature.Service"))
            {
                try
                {
                    if (process.Id == currentPid)
                    {
                        continue;
                    }

                    if (activeServicePid.HasValue && process.Id == activeServicePid.Value)
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best-effort
                }
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static bool TryInstallAndStartWindowsService(string serviceExePath, out string? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(serviceExePath) || !File.Exists(serviceExePath))
            {
                error = $"Файл сервиса не найден: {serviceExePath}";
                return false;
            }

            var scPath = GetScExePath();
            if (string.IsNullOrEmpty(scPath))
            {
                error = "Не найден sc.exe";
                return false;
            }

            RunScBestEffort(scPath, $"stop {ServiceName}");
            RunScBestEffort(scPath, $"delete {ServiceName}");
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
                if (RunSc(scPath, createArgs, out var createExit, out var createOutput))
                {
                    created = true;
                    break;
                }

                lastCreateExit = createExit;
                lastCreateOutput = createOutput;
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
                error = $"sc create failed ({lastCreateExit}): {lastCreateOutput}";
                return false;
            }

            RunScBestEffort(scPath, $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000");
            var startOk = RunSc(scPath, $"start {ServiceName}", out var startExit, out var startOutput);
            if (!startOk && startExit == ErrorServiceAlreadyRunning)
            {
                startOk = true;
            }

            if (!startOk && startExit == ErrorServiceRequestTimeout)
            {
                using var scProbe = new ServiceController(ServiceName);
                scProbe.Refresh();
                if (scProbe.Status == ServiceControllerStatus.Running)
                {
                    startOk = true;
                }
                else if (
                    scProbe.Status == ServiceControllerStatus.Stopped &&
                    !AnyAriaSignatureServiceHostProcessExists())
                {
                    error =
                        $"sc start вернул {ErrorServiceRequestTimeout}: служба остановлена и процесс AriaSignature.Service не найден (вероятно падение при старте). См. %ProgramData%\\AriaSignature\\logs\\. Вывод sc: {startOutput}";
                    return false;
                }
                else
                {
                    try
                    {
                        scProbe.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(120));
                        startOk = true;
                    }
                    catch (Exception waitEx)
                    {
                        error =
                            $"sc start вернул {ErrorServiceRequestTimeout} (таймаут SCM). Дополнительное ожидание Running не удалось: {waitEx.Message}. Вывод sc: {startOutput}";
                        return false;
                    }
                }
            }
            else if (!startOk)
            {
                error = $"sc start failed ({startExit}): {startOutput}";
                return false;
            }

            using var sc = new ServiceController(ServiceName);
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(120));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
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

    private static void RunScBestEffort(string scPath, string args)
    {
        RunSc(scPath, args, out _, out _);
    }

    private static int? TryGetServiceProcessId()
    {
        try
        {
            var scPath = GetScExePath();
            if (string.IsNullOrEmpty(scPath))
            {
                return null;
            }

            var psi = new ProcessStartInfo
            {
                FileName = scPath,
                Arguments = "queryex " + ServiceName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            ApplyScConsoleEncoding(psi);
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("PID", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var idx = line.IndexOf(':');
                if (idx < 0)
                {
                    continue;
                }

                var pidText = line[(idx + 1)..].Trim();
                if (int.TryParse(pidText, out var pid) && pid > 0)
                {
                    return pid;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
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

    private static bool RunSc(string scPath, string args, out int exitCode, out string output)
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
