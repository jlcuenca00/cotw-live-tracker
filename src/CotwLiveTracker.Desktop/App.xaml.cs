using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace CotwLiveTracker.Desktop;

public partial class App : Application
{
    private static string CrashLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WildTrace",
            "crash.log");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        ReportCrash(e.Exception);
        e.Handled = false;
    }

    private static void OnUnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            ReportCrash(exception);
        }
    }

    private static void ReportCrash(Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(CrashLogPath)!;
            Directory.CreateDirectory(directory);

            File.WriteAllText(
                CrashLogPath,
                $"WildTrace startup/runtime crash{Environment.NewLine}" +
                $"UTC: {DateTime.UtcNow:O}{Environment.NewLine}{Environment.NewLine}" +
                exception);
        }
        catch
        {
            // Crash reporting must never mask the original exception.
        }

        try
        {
            MessageBox.Show(
                $"WildTrace encountered an error and must close.{Environment.NewLine}{Environment.NewLine}" +
                $"{exception.GetType().Name}: {exception.Message}{Environment.NewLine}{Environment.NewLine}" +
                $"Crash log: {CrashLogPath}",
                "WildTrace",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // The UI may not be available if startup failed very early.
        }
    }
}
