using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AriaSignature.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _trayDrawingIcon;
    private bool _isExitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var mainWindow = new MainWindow(this)
        {
            DataContext = Current.Resources["MainViewModel"]
        };
        MainWindow = mainWindow;

        var startInTray = e.Args.Any(arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase));
        if (!startInTray)
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
        base.OnExit(e);
    }

    private void InitializeTrayIcon()
    {
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (!System.IO.File.Exists(iconPath))
        {
            return;
        }

        _trayDrawingIcon = new Drawing.Icon(iconPath);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) =>
        {
            MainWindow?.Show();
            MainWindow!.WindowState = WindowState.Normal;
            MainWindow?.Activate();
        });
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
            MainWindow?.Show();
            MainWindow!.WindowState = WindowState.Normal;
            MainWindow?.Activate();
        };
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

