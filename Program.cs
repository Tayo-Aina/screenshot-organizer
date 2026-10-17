using System.Windows.Forms;

namespace ScreenshotOrganizer;

internal class Program
{
    private static readonly string MutexName = @"Global\ScreenshotOrganizer_Instance";
    private static Mutex? _mutex;
    private static ScreenshotWatcher? _watcher;
    private static TrayIconManager? _trayIcon;
    private static bool _cleanupComplete;

    [STAThread]
    public static void Main(string[] args)
    {
        var shouldInstall = args.Contains("--install", StringComparer.OrdinalIgnoreCase);
        var shouldUninstall = args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase);

        if (shouldUninstall)
        {
            StartupManager.Unregister();
            Logger.Info("Uninstall complete");
            return;
        }

        if (shouldInstall)
        {
            StartupManager.Register();
            Logger.Info("Install complete");
        }

        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            Logger.Warn("Another instance is already running");
            return;
        }

        try
        {
            Logger.Info("Screenshot Organizer starting");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _watcher = new ScreenshotWatcher();
            _watcher.Start();

            _trayIcon = new TrayIconManager(_watcher);

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            Application.Run(new ApplicationContext());
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal error", ex);
        }
        finally
        {
            Cleanup();
        }
    }

    private static void OnProcessExit(object? sender, EventArgs e) => Cleanup();

    private static void Cleanup()
    {
        if (_cleanupComplete)
            return;

        _cleanupComplete = true;

        _watcher?.Dispose();
        _watcher = null;

        _trayIcon?.Dispose();
        _trayIcon = null;

        if (_mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _mutex.Dispose();
            _mutex = null;
        }

        Logger.Info("Screenshot Organizer stopped");
    }
}
