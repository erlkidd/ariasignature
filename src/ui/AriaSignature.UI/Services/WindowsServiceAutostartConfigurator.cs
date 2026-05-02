using System.IO;
using System.ServiceProcess;
using System.Text.Json;

namespace AriaSignature.UI.Services;

/// <summary>
/// Автозапуск панели: HKCU Run + ярлык shell:startup с --tray. Тип запуска службы задаётся при установке.
/// </summary>
public static class WindowsServiceAutostartConfigurator
{
    private static readonly string AutostartDefaultMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AriaSignature",
        ".autostart-default-once");

    /// <summary>
    /// После установки без ярлыка в Startup: один раз включить автозапуск, если пользователь ещё ничего не настроил.
    /// </summary>
    public static void EnsureDefaultAutostartApplied(StartupRegistrationService startup)
    {
        try
        {
            if (startup.IsEnabled())
            {
                return;
            }

            if (File.Exists(AutostartDefaultMarkerPath))
            {
                return;
            }

            var dir = Path.GetDirectoryName(AutostartDefaultMarkerPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            startup.SetEnabled(true);
            File.WriteAllText(AutostartDefaultMarkerPath, "1");
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Служба настроена на старт при загрузке ОС (тип «Автоматически» в оснастке служб).
    /// </summary>
    public static bool IsBootStartAutomatic()
    {
        try
        {
            using var sc = new ServiceController(WindowsServiceEnsure.ServiceName);
            sc.Refresh();
            return sc.StartType == ServiceStartMode.Automatic;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Включает или выключает автозапуск панели (реестр + ярлык автозагрузки). Без UAC.
    /// </summary>
    public static void ApplyAutostart(bool enabled, StartupRegistrationService startup) =>
        startup.SetEnabled(enabled);

    /// <summary>
    /// JSON для WebView2: чекбокс по факту HKCU Run / ярлыка; <c>serviceBootAuto</c> — диагностика службы.
    /// </summary>
    public static string SerializeAutostartWebMessage(StartupRegistrationService startup) =>
        JsonSerializer.Serialize(new
        {
            action = "autostart",
            enabled = startup.IsEnabled(),
            serviceBootAuto = IsBootStartAutomatic()
        });
}
