using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AriaSignature.UI.Services;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "AriaSignature";
    public const string StartupShortcutFileName = "AriaSignature.lnk";

    private static string StartupShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutFileName);

    /// <summary>
    /// Включён ли автозапуск панели: запись HKCU Run и/или ярлык в shell:startup (любой из механизмов).
    /// </summary>
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var run = key?.GetValue(EntryName) is string value && !string.IsNullOrWhiteSpace(value);
        var lnk = File.Exists(StartupShortcutPath);
        return run || lnk;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                return;
            }

            var workDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            key.SetValue(EntryName, $"\"{exePath}\" --tray");
            TryCreateStartupShortcut(exePath, workDir);
        }
        else
        {
            key.DeleteValue(EntryName, throwOnMissingValue: false);
            TryDeleteStartupShortcut();
        }
    }

    private static void TryCreateStartupShortcut(string targetExe, string workingDirectory)
    {
        try
        {
            var path = StartupShortcutPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }

            var shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return;
            }

            try
            {
                var shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { path });
                if (shortcut is null)
                {
                    return;
                }

                var st = shortcut.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe });
                st.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { "--tray" });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                Marshal.FinalReleaseComObject(shortcut);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static void TryDeleteStartupShortcut()
    {
        try
        {
            if (File.Exists(StartupShortcutPath))
            {
                File.Delete(StartupShortcutPath);
            }
        }
        catch
        {
            // best-effort
        }
    }
}
