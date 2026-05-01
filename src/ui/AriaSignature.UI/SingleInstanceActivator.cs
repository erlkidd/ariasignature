using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AriaSignature.UI;

internal static class SingleInstanceActivator
{
    private const int SwRestore = 9;
    private const uint AsfwAny = 0xFFFFFFFF;

    public static void TryBringExistingToForeground()
    {
        var currentPid = Environment.ProcessId;
        foreach (var proc in Process.GetProcessesByName("AriaSignature.UI"))
        {
            if (proc.Id == currentPid)
            {
                continue;
            }

            var hWnd = proc.MainWindowHandle;
            if (hWnd == IntPtr.Zero)
            {
                hWnd = FindMainWindowHwnd(proc.Id);
            }

            if (hWnd == IntPtr.Zero)
            {
                continue;
            }

            _ = ShowWindow(hWnd, SwRestore);
            _ = AllowSetForegroundWindow(AsfwAny);
            _ = SetForegroundWindow(hWnd);
            return;
        }
    }

    private static IntPtr FindMainWindowHwnd(int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(
            (IntPtr hWnd, IntPtr lParam) =>
            {
                _ = GetWindowThreadProcessId(hWnd, out var pid);
                if ((int)pid != processId)
                {
                    return true;
                }

                var title = GetWindowTitle(hWnd);
                if (title.Length > 0 && title.Contains("AriaSignature", StringComparison.Ordinal))
                {
                    found = hWnd;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);

        return found;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(512);
        return GetWindowText(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
}
