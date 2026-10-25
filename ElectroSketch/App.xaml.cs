using System.Windows;
using System.Windows.Threading;
using ElectroSketch.Core;

namespace ElectroSketch;

public partial class App : Application
{
    private DateTime _lastUiError = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        var registerOnly = e.Args.Any(arg => arg.Equals("--register-association-only", StringComparison.OrdinalIgnoreCase));
        DispatcherUnhandledException += HandleDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashReporter.Log(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown fatal error"), "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashReporter.Log(args.Exception, "Background task");
            args.SetObserved();
        };
        try
        {
            FileAssociation.Register();
        }
        catch (Exception ex)
        {
            CrashReporter.Log(ex, "File association");
        }
        if (registerOnly)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    private void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReporter.Log(e.Exception, "User interface");
        e.Handled = true;
        if (DateTime.UtcNow - _lastUiError < TimeSpan.FromSeconds(5)) return;
        _lastUiError = DateTime.UtcNow;
        MessageBox.Show(
            "ElectroSketch recovered from an unexpected interface error. Your topology is still open.\n\nA diagnostic log was saved at:\n" + CrashReporter.LogPath,
            "ElectroSketch recovered",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
