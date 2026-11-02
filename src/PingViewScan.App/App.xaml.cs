using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace PingViewScan.App;

/// <summary>
/// Application entry point. The main window is created in code rather than via
/// <c>StartupUri</c> so any XAML/startup failure can be logged and shown to the
/// user instead of the process silently disappearing.
/// </summary>
public partial class App : Application
{
    private bool _startupFailed;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        StartupLog.Write(
            $"Starting PingViewScan {typeof(App).Assembly.GetName().Version} " +
            $"({RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}, " +
            $"OS {Environment.OSVersion}, 64-bit process: {Environment.Is64BitProcess})");

        // Used by CI to prove the published app really shows its window on a
        // Windows host. Showing the window matters: data bindings only resolve
        // their markup culture once the window has a presentation source.
        if (e.Args.Any(arg => string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            RunSelfTest();
            return;
        }

        try
        {
            StartupLog.Write("Creating MainWindow…");
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            StartupLog.Write("Main window shown.");
        }
        catch (Exception ex)
        {
            _startupFailed = true;
            StartupLog.Write("Fatal error while starting the main window", ex);
            ShowFatalError(
                "PingViewScan could not start.",
                ex,
                StartupLog.Path);
            Shutdown(1);
        }
    }

    private void RunSelfTest()
    {
        DispatcherTimer? timeout = null;
        try
        {
            var window = new MainWindow();

            // ContentRendered only fires after the visual tree has been shown
            // and rendered, which is exactly when the en-US binding culture was
            // being resolved in the field.
            timeout = new DispatcherTimer(DispatcherPriority.ApplicationIdle, Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            timeout.Tick += (_, _) =>
            {
                timeout!.Stop();
                StartupLog.Write("Self-test failed: window did not render within 15 seconds.");
                Shutdown(3);
            };
            timeout.Start();

            window.ContentRendered += (_, _) =>
            {
                timeout!.Stop();
                StartupLog.Write("Self-test OK: MainWindow rendered and all tab views loaded.");
                window.Close();
                Shutdown(0);
            };

            window.Show();
        }
        catch (Exception ex)
        {
            timeout?.Stop();
            StartupLog.Write("Self-test failed", ex);
            Shutdown(2);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupLog.Write("Unhandled UI exception", e.Exception);

        // During startup the fatal-error dialog has already been shown; don't
        // stack another one for every binding the shutdown winds down.
        if (!_startupFailed)
        {
            ShowFatalError("PingViewScan hit an unexpected error.", e.Exception, StartupLog.Path);
        }

        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        StartupLog.Write("Unhandled application exception", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        StartupLog.Write("Unobserved background exception", e.Exception);
        e.SetObserved();
    }

    private static void ShowFatalError(string headline, Exception exception, string logPath)
    {
        try
        {
            MessageBox.Show(
                $"{headline}\n\n{exception.GetType().Name}: {exception.Message}\n\n" +
                $"Details were written to:\n{logPath}",
                "PingViewScan",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // If even the dialog fails, the log is still written.
        }
    }
}
