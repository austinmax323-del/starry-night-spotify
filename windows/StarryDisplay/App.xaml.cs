using System.Threading;
using System.Windows;
using StarryDisplay.Services;

namespace StarryDisplay;

public partial class App : System.Windows.Application
{
    // One instance only: a second copy would fight over the tray, the Ctrl+Alt+L hotkey
    // and the OAuth callback port.
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\StarryDisplay.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Log.Write("Second instance started; exiting.");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("Unhandled UI exception (kept running).", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Write("Fatal exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        Log.Write($"Starting Starry Display {typeof(App).Assembly.GetName().Version}.");
        var window = new MainWindow();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstance is not null)
        {
            try { _singleInstance.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }
}
