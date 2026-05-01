using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AriaSignature.UI;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\AriaSignature.UI.SingleInstance.v1";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    private Forms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _trayDrawingIcon;
    private bool _isExitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            SingleInstanceActivator.TryBringExistingToForeground();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _ownsSingleInstanceMutex = true;

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var mainWindow = new MainWindow(this);
        MainWindow = mainWindow;

        var startInTray = e.Args.Any(arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase));
        if (startInTray)
        {
            mainWindow.ShowInTaskbar = false;
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.ShowActivated = false;
            mainWindow.Opacity = 0;
            mainWindow.Show();
            mainWindow.Hide();
            mainWindow.Opacity = 1;
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.ShowActivated = true;
            mainWindow.ShowInTaskbar = true;
        }
        else
        {
            mainWindow.Show();
        }

        InitializeTrayIcon();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _trayDrawingIcon?.Dispose();

        if (_ownsSingleInstanceMutex && _singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch
            {
                // ignore
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private void InitializeTrayIcon()
    {
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        _trayDrawingIcon = LoadTrayIcon(iconPath);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => RestoreMainWindow());
        menu.Items.Add("Выход", null, (_, _) => ExitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "AriaSignature",
            Icon = _trayDrawingIcon,
            Visible = true,
            ContextMenuStrip = menu
        };

        _trayIcon.DoubleClick += (_, _) =>
        {
            RestoreMainWindow();
        };
    }

    private void RestoreMainWindow()
    {
        if (MainWindow is not Window window)
        {
            return;
        }

        window.ShowActivated = true;
        window.ShowInTaskbar = true;

        // WPF падает, если пытаться Show() у скрытого окна с ShowActivated=false и WindowState=Maximized.
        var wasMaximized = window.WindowState == WindowState.Maximized;
        if (wasMaximized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (wasMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
        else if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private static Drawing.Icon LoadTrayIcon(string iconPath)
    {
        try
        {
            if (System.IO.File.Exists(iconPath))
            {
                return new Drawing.Icon(iconPath);
            }
        }
        catch
        {
            // ignore
        }

        return Drawing.SystemIcons.Application;
    }

    public bool CanCloseToTray()
    {
        return !_isExitRequested;
    }

    public void ExitApplication()
    {
        _isExitRequested = true;
        MainWindow?.Close();
        Shutdown();
    }
}
