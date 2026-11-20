using System.Diagnostics;
using System.IO;
using System.Windows;
using TrueInjector.UI;

namespace TrueInjector;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // The legacy build demanded RealTime; High keeps the injection burst
            // snappy without starving shell audio/input threads.
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        }
        catch
        {
            // Priority is an optimisation, never a requirement.
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "last-crash.log"),
                    $"DOMAIN: {args.ExceptionObject}\n");
            }
            catch
            {
                // ignored
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;

            try
            {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "last-crash.log"),
                    $"DISPATCHER: {args.Exception}\n");
            }
            catch
            {
                // Logging must never compound a failure.
            }
        };

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Detached native-injection threads must not linger; the legacy tool
        // killed the process outright and we keep that guarantee, politely.
        Environment.Exit(e.ApplicationExitCode);
    }
}
