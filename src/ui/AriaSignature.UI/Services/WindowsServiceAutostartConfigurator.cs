using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text.Json;

namespace AriaSignature.UI.Services;

/// <summary>
/// Автозапуск панели (HKCU Run) и тип запуска службы Windows (sc config, UAC).
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
    /// Включает или выключает автозапуск: сначала реестр пользователя, затем тип запуска службы (UAC).
    /// Запись Run применяется сразу; при ошибке <c>sc config</c> панель всё равно может стартовать при входе.
    /// </summary>
    /// <param name="serviceConfigWarning">Не null, если не удалось изменить тип запуска службы.</param>
    public static void ApplyAutostart(bool enabled, StartupRegistrationService startup, out string? serviceConfigWarning)
    {
        serviceConfigWarning = null;
        if (enabled)
        {
            startup.SetEnabled(true);
            if (!TrySetBootStartAutomatic(true, out var err))
            {
                serviceConfigWarning = err;
            }
        }
        else
        {
            startup.SetEnabled(false);
            if (!TrySetBootStartAutomatic(false, out var err))
            {
                serviceConfigWarning = err;
            }
        }
    }

    /// <summary>
    /// JSON для WebView2: чекбокс по факту HKCU Run; <c>serviceBootAuto</c> для подсказки о службе.
    /// </summary>
    public static string SerializeAutostartWebMessage(StartupRegistrationService startup) =>
        JsonSerializer.Serialize(new
        {
            action = "autostart",
            enabled = startup.IsEnabled(),
            serviceBootAuto = IsBootStartAutomatic()
        });

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
}
