using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace AriaSignature.UI.Services;

/// <summary>
/// Связывает автозапуск панели с типом запуска обеих служб Windows (main + Melezh).
/// </summary>
public static class WindowsServicesAutostartCoordinator
{
    public const string MelezhServiceName = "AriaSignatureMelezhService";

    public static bool IsMainServiceBootAutomatic() =>
        IsServiceStartTypeAutomatic(WindowsServiceEnsure.ServiceName);

    public static bool IsMelezhServiceBootAutomatic() =>
        IsServiceStartTypeAutomatic(MelezhServiceName);

    public static bool AreAllServicesBootAutomatic() =>
        IsMainServiceBootAutomatic() && IsMelezhServiceBootAutomatic();

    /// <summary>
    /// Включает или выключает автозапуск обеих служб (требует elevation для sc config).
    /// </summary>
    public static (bool Ok, string? Error) ApplyServicesBootStart(bool enabled, bool startIfEnabled)
    {
        var startMode = enabled ? "auto" : "demand";
        var services = new[] { WindowsServiceEnsure.ServiceName, MelezhServiceName };

        foreach (var name in services)
        {
            if (!TryRunElevatedSc($"config {name} start= {startMode}", out var configErr))
            {
                return (false, configErr ?? $"Не удалось изменить автозапуск службы {name}.");
            }
        }

        if (!startIfEnabled || !enabled)
        {
            return (true, null);
        }

        foreach (var name in services)
        {
            if (!TryRunElevatedSc($"start {name}", out var startErr))
            {
                return (false, startErr ?? $"Не удалось запустить службу {name}.");
            }
        }

        return (true, null);
    }

    private static bool IsServiceStartTypeAutomatic(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            sc.Refresh();
            return sc.StartType == ServiceStartMode.Automatic;
        }
        catch
        {
            return false;
        }
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

            errorToUser = $"sc.exe ({args}) завершился с кодом {proc.ExitCode}.";
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            errorToUser = "Операция отменена (UAC).";
            return false;
        }
    }
}
