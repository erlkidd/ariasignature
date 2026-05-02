using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace AriaSignature.UI.Services;

/// <summary>
/// Синхронизирует автозапуск службы Windows с настройкой «при входе в Windows» в UI.
/// </summary>
public static class WindowsServiceAutostartConfigurator
{
    /// <summary>
    /// Служба настроена на старт при загрузке ОС (не «Вручную»).
    /// </summary>
    public static bool IsBootStartAutomatic()
    {
        try
        {
            using var sc = new ServiceController(WindowsServiceEnsure.ServiceName);
            sc.Refresh();
            return sc.StartType is ServiceStartMode.Automatic;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// И панель в трее (HKCU Run), и тип запуска службы — автоматически.
    /// </summary>
    public static bool IsFullAutostartEnabled(StartupRegistrationService startup) =>
        startup.IsEnabled() && IsBootStartAutomatic();

    /// <summary>
    /// Изменяет тип запуска службы. Требует подтверждения UAC.
    /// </summary>
    public static bool TrySetBootStartAutomatic(bool automatic, out string? error)
    {
        error = null;
        var serviceName = WindowsServiceEnsure.ServiceName;
        var mode = automatic ? "auto" : "demand";
        var scPath = Path.Combine(Environment.SystemDirectory, "sc.exe");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = scPath,
                Arguments = $"config {serviceName} start= {mode}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process? process = null;
            try
            {
                process = Process.Start(psi);
                if (process is null)
                {
                    error = "Не удалось запустить sc.exe с повышенными правами.";
                    return false;
                }

                if (!process.WaitForExit(120000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // ignore
                    }

                    error = "Превышено время ожидания изменения типа запуска службы.";
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    error = automatic
                        ? $"Не удалось включить автозапуск службы (код {process.ExitCode}). Подтвердите запрос контроля учётных записей."
                        : $"Не удалось отключить автозапуск службы (код {process.ExitCode}).";
                    return false;
                }
            }
            finally
            {
                process?.Dispose();
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        return true;
    }

    public static bool TryApplyFullAutostart(bool enabled, StartupRegistrationService startup, out string? error)
    {
        if (!TrySetBootStartAutomatic(enabled, out error))
        {
            return false;
        }

        startup.SetEnabled(enabled);
        return true;
    }
}
