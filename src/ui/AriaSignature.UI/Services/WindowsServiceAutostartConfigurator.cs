using System.ServiceProcess;
using System.Text.Json;

namespace AriaSignature.UI.Services;

/// <summary>
/// Автозапуск панели в профиле пользователя (HKCU Run). Тип запуска службы задаётся при установке.
/// </summary>
public static class WindowsServiceAutostartConfigurator
{
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
    /// Включает или выключает автозапуск панели (реестр текущего пользователя). Без UAC.
    /// </summary>
    public static void ApplyAutostart(bool enabled, StartupRegistrationService startup) =>
        startup.SetEnabled(enabled);

    /// <summary>
    /// JSON для WebView2: чекбокс по факту HKCU Run; <c>serviceBootAuto</c> — диагностика службы.
    /// </summary>
    public static string SerializeAutostartWebMessage(StartupRegistrationService startup) =>
        JsonSerializer.Serialize(new
        {
            action = "autostart",
            enabled = startup.IsEnabled(),
            serviceBootAuto = IsBootStartAutomatic()
        });
}
