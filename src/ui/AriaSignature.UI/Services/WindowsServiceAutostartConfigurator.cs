using System.IO;
using System.ServiceProcess;
using System.Text.Json;

namespace AriaSignature.UI.Services;

/// <summary>
/// Автозапуск панели: HKCU Run + ярлык shell:startup. Тип запуска служб — через <see cref="WindowsServicesAutostartCoordinator"/>.
/// </summary>
public static class WindowsServiceAutostartConfigurator
{
    private static readonly string AutostartDefaultMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AriaSignature",
        ".autostart-default-once");

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

    public static bool IsBootStartAutomatic() => WindowsServicesAutostartCoordinator.IsMainServiceBootAutomatic();

    public static void ApplyAutostart(bool enabled, StartupRegistrationService startup) =>
        startup.SetEnabled(enabled);

    public static string SerializeAutostartWebMessage(StartupRegistrationService startup) =>
        JsonSerializer.Serialize(new
        {
            action = "autostart",
            enabled = startup.IsEnabled(),
            serviceBootAuto = WindowsServicesAutostartCoordinator.IsMainServiceBootAutomatic(),
            melezhServiceBootAuto = WindowsServicesAutostartCoordinator.IsMelezhServiceBootAutomatic(),
            allServicesBootAuto = WindowsServicesAutostartCoordinator.AreAllServicesBootAutomatic()
        });
}
