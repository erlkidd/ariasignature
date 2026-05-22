using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.ServiceProcess;

namespace AriaSignature.UI.Services;

/// <summary>
/// Восстановление службы AriaSignatureMelezhService после degraded install.
/// </summary>
public static class MelezhServiceRepair
{
    public const string ServiceName = "AriaSignatureMelezhService";
    private const int ErrorServiceDoesNotExist = 1060;
    private static readonly HttpClient ProbeHttp = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static (bool Ok, string? Error) TryRepairWithElevation()
    {
        var hostExe = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "melezh-host", "AriaSignature.MelezhHost.exe"));
        var melezhBat = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "melezh", "bin", "melezh.bat"));

        if (!File.Exists(hostExe))
        {
            return (false, $"Не найден Melezh host: {hostExe}. Переустановите AriaSignature 1.1.0.");
        }

        if (!File.Exists(melezhBat))
        {
            return (false, $"Не найден OInt bundle (melezh.bat): {melezhBat}. Переустановите с prepare-melezh.");
        }

        try
        {
            TryStopAndDeleteService();
            if (!TryRunElevatedSc(
                    $"create {ServiceName} binPath= \"{hostExe}\" start= auto DisplayName= \"AriaSignature Melezh\" obj= LocalSystem",
                    out var createErr))
            {
                return (false, createErr ?? "Не удалось зарегистрировать службу Melezh (sc create).");
            }

            if (!TryRunElevatedSc($"start {ServiceName}", out var startErr))
            {
                return (false, startErr ?? "Не удалось запустить службу Melezh (sc start).");
            }

            TryRunBootstrapOnly(hostExe);

            if (!WaitForMelezhUi())
            {
                return (false, "Служба Melezh запущена, но Web UI не отвечает на http://127.0.0.1:7788/ui.");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static void TryStopAndDeleteService()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            sc.Refresh();
            if (sc.Status != ServiceControllerStatus.Stopped)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }
        catch (InvalidOperationException ex) when (IsNativeError(ex, ErrorServiceDoesNotExist))
        {
            return;
        }
        catch
        {
            // continue with elevated delete
        }

        TryRunElevatedSc($"stop {ServiceName}", out _);
        TryRunElevatedSc($"delete {ServiceName}", out _);
        Thread.Sleep(1500);
    }

    public static void RunBootstrapOnlyBestEffort()
    {
        var hostExe = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "melezh-host", "AriaSignature.MelezhHost.exe"));
        if (!File.Exists(hostExe))
        {
            return;
        }

        TryRunBootstrapOnly(hostExe);
    }

    private static void TryRunBootstrapOnly(string hostExe)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = hostExe,
                Arguments = "--bootstrap-only",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(hostExe) ?? AppContext.BaseDirectory
            });
            proc?.WaitForExit(120_000);
        }
        catch
        {
            // best-effort; host also bootstraps on service start
        }
    }

    private static bool WaitForMelezhUi()
    {
        for (var i = 0; i < 24; i++)
        {
            try
            {
                using var response = ProbeHttp.GetAsync("http://127.0.0.1:7788/ui").GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // retry
            }

            Thread.Sleep(2500);
        }

        return false;
    }

    private static bool TryRunElevatedSc(string args, out string? errorToUser)
    {
        errorToUser = null;
        var scPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
        if (!File.Exists(scPath))
        {
            errorToUser = "Не найден sc.exe.";
            return false;
        }

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = scPath,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (proc is null)
            {
                errorToUser = "Не удалось запустить elevated sc.exe.";
                return false;
            }

            proc.WaitForExit(120_000);
            if (proc.ExitCode is 0 or 1056)
            {
                return true;
            }

            errorToUser = $"sc.exe завершился с кодом {proc.ExitCode}.";
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            errorToUser = "Операция отменена (UAC).";
            return false;
        }
    }

    private static bool IsNativeError(Exception ex, int code)
    {
        return ex.InnerException is System.ComponentModel.Win32Exception w && w.NativeErrorCode == code;
    }
}
