using Microsoft.Win32;

namespace AriaSignature.UI.Services;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "AriaSignature";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(EntryName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "AriaSignature.UI.exe";
            key.SetValue(EntryName, $"\"{exePath}\" --tray");
        }
        else
        {
            key.DeleteValue(EntryName, throwOnMissingValue: false);
        }
    }
}
