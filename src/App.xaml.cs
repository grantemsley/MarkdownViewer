using System;
using System.IO;
using System.Threading;
using System.Windows;
using MarkdownViewer.Services;

namespace MarkdownViewer;

public partial class App : Application
{
    // Held for the process lifetime by the owning (first) instance so later
    // launches see it and hand off instead of opening their own window.
    private Mutex? _instanceMutex;
    private SingleInstanceServer? _instanceServer;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MarkdownViewer");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:O}] {ex.ExceptionObject}\n\n");
            }
            catch { /* swallow */ }
        };

        string? initialArg = e.Args.Length > 0 ? e.Args[0] : null;

        // Single-instance: if another instance owns the mutex, hand it our file
        // argument and exit. If the hand-off fails (owner mid-exit), fall through
        // and launch normally — worst case is a second window, never a hang.
        var settings = SettingsService.Load();
        if (settings.SingleInstance.Enabled)
        {
            _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceServer.MutexName, out bool createdNew);
            if (!createdNew)
            {
                if (SingleInstanceServer.TrySignal(initialArg ?? ""))
                {
                    _instanceMutex.Dispose();
                    _instanceMutex = null;
                    Shutdown();
                    return;
                }
                // Couldn't reach the owner — keep the (non-owned) handle and launch.
            }
        }

        var window = new MainWindow(initialArg);

        // We own the instance (or single-instance is off but the mutex was taken):
        // run the pipe server so later launches route their file here.
        if (_instanceMutex != null)
        {
            _instanceServer = new SingleInstanceServer();
            _instanceServer.Received += path =>
                window.Dispatcher.BeginInvoke(() => window.HandleIncomingFile(path));
            _instanceServer.Start();
        }

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceServer?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
