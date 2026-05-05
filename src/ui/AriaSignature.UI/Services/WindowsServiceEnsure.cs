using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using AriaSignature.WinSvc;

namespace AriaSignature.UI.Services;

/// <summary>
/// Гарантирует, что фоновая служба с API запущена (имя совпадает с установщиком Inno Setup).
/// </summary>
public static class WindowsServiceEnsure
{
    public const string ServiceName = WindowsServiceInstaller.ServiceName;

    /// <summary>Win32 ERROR_SERVICE_REQUEST_TIMEOUT — SCM не дождался ответа службы за отведённое время.</summary>
    private const int ErrorServiceRequestTimeout = 1053;

    /// <summary>Win32 ERROR_ACCESS_DENIED.</summary>
    private const int ErrorAccessDenied = 5;

    /// <summary>Пользователь отменил повышение прав (UAC).</summary>
    private const int ErrorCancelled = 1223;

    private static int _elevatedBootstrapLaunched;

    static WindowsServiceEnsure()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <param name="bootstrapExePath">Путь к <c>AriaSignature.ServiceBootstrap.exe</c> для одного запроса UAC при отказе прав SCM.</param>
    public static void TryStartOrFallback(string serviceExePath, TimeSpan wait, string? bootstrapExePath, out string? warningMessage)
    {
        TryStartOrFallbackCore(serviceExePath, wait, bootstrapExePath, allowElevatedRecovery: true, out warningMessage);
    }

    private static void TryStartOrFallbackCore(
        string serviceExePath,
        TimeSpan wait,
        string? bootstrapExePath,
        bool allowElevatedRecovery,
        out string? warningMessage)
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

            InvalidOperationException? startFailure = null;
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
            catch (InvalidOperationException ex)
            {
                startFailure = ex;
            }

            var setupResult = WindowsServiceInstaller.TryInstallAndStart(serviceExePath, logLine: null);
            if (setupResult.Success)
            {
                return;
            }

            string? elevationFailure = null;
            if (allowElevatedRecovery
                && TryRecoverViaElevatedBootstrap(
                    serviceExePath,
                    bootstrapExePath,
                    setupResult,
                    startFailure,
                    out elevationFailure))
            {
                TryStartOrFallbackCore(serviceExePath, wait, bootstrapExePath, allowElevatedRecovery: false, out warningMessage);
                return;
            }

            warningMessage =
                WindowsServiceInstaller.BuildUserHint(setupResult)
                + (startFailure is null ? string.Empty : $"\nИсходная ошибка SCM: {startFailure.Message}")
                + (string.IsNullOrEmpty(elevationFailure) ? string.Empty : $"\n{elevationFailure}");
        }
        catch (InvalidOperationException ex)
        {
            // На случай если служба отсутствует до первого вызова TryInstall внутри другого потока контекста.
            var setupResult = WindowsServiceInstaller.TryInstallAndStart(serviceExePath, logLine: null);
            if (setupResult.Success)
            {
                return;
            }

            string? outerElevationFailure = null;
            if (allowElevatedRecovery && TryRecoverViaElevatedBootstrap(
                    serviceExePath,
                    bootstrapExePath,
                    setupResult,
                    ex,
                    out outerElevationFailure))
            {
                TryStartOrFallbackCore(serviceExePath, wait, bootstrapExePath, allowElevatedRecovery: false, out warningMessage);
                return;
            }

            warningMessage =
                WindowsServiceInstaller.BuildUserHint(setupResult)
                + $"\nИсходная ошибка SCM: {ex.Message}"
                + (string.IsNullOrEmpty(outerElevationFailure) ? string.Empty : $"\n{outerElevationFailure}");
        }
        catch (System.TimeoutException)
        {
            warningMessage =
                WindowsServiceInstaller.BuildUserHint(
                    new ServiceSetupResult(false, "stage=scm-timeout-1053", ServiceSetupFailureCategory.ServiceStartTimeout, ErrorServiceRequestTimeout));
        }
        catch (Exception ex)
        {
            var hint = WindowsServiceInstaller.BuildUserHint(
                new ServiceSetupResult(false, ex.Message, ServiceSetupFailureCategory.Unknown, null));
            warningMessage = hint;
        }
    }

    private static bool ShouldOfferElevatedBootstrap(ServiceSetupResult setupResult, InvalidOperationException? scmException)
    {
        if (setupResult.Category == ServiceSetupFailureCategory.AccessDenied
            || setupResult.LastNonZeroExitCode == ErrorAccessDenied)
        {
            return true;
        }

        if (scmException is not null && TryFindNativeError(scmException, out var code) && code == ErrorAccessDenied)
        {
            return true;
        }

        return false;
    }

    private static bool TryRecoverViaElevatedBootstrap(
        string serviceExePath,
        string? bootstrapExePath,
        ServiceSetupResult setupResult,
        InvalidOperationException? scmException,
        out string? elevationFailureMessage)
    {
        elevationFailureMessage = null;
        if (!ShouldOfferElevatedBootstrap(setupResult, scmException))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(bootstrapExePath) || !File.Exists(bootstrapExePath))
        {
            elevationFailureMessage =
                "Рядом с AriaSignature.UI не найден AriaSignature.ServiceBootstrap.exe — переустановите приложение или запустите его от имени администратора.";
            return false;
        }

        if (Interlocked.CompareExchange(ref _elevatedBootstrapLaunched, 1, 0) != 0)
        {
            return false;
        }

        if (!TryRunElevatedBootstrapProcess(bootstrapExePath, serviceExePath, out var err))
        {
            elevationFailureMessage = err;
            return false;
        }

        return true;
    }

    private static bool TryRunElevatedBootstrapProcess(string bootstrapExePath, string serviceExePath, out string? errorToUser)
    {
        errorToUser = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = bootstrapExePath,
                Arguments = $"\"{serviceExePath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                errorToUser = "Не удалось запустить восстановление службы с правами администратора.";
                return false;
            }

            if (!proc.WaitForExit(180_000))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }

                errorToUser =
                    "Истекло время ожидания восстановления службы (запрос прав администратора). Проверьте журнал bootstrap в %ProgramData%\\AriaSignature\\logs\\.";
                return false;
            }

            if (proc.ExitCode != 0)
            {
                errorToUser =
                    $"Восстановление службы завершилось с кодом {proc.ExitCode}. Подробности — в журнале bootstrap в %ProgramData%\\AriaSignature\\logs\\.";
                return false;
            }

            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            errorToUser =
                "Запрос прав администратора отменён. Запустите AriaSignature от имени администратора один раз или включите службу вручную в services.msc.";
            return false;
        }
        catch (Exception ex)
        {
            errorToUser = ex.Message;
            return false;
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
}
