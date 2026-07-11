using System;
using System.IO;
using System.Runtime.InteropServices;
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
                var logPath = SettingsService.CrashLogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] {ex.ExceptionObject}\n\n");
            }
            catch { /* swallow */ }
        };

        string? initialArg = e.Args.Length > 0 ? e.Args[0] : null;

        // Single-instance: if another instance owns the mutex, hand it our file
        // argument and exit. If the hand-off fails (owner mid-exit), fall through
        // and launch normally — worst case is a second window, never a hang.
        var settings = SettingsService.Load();
        bool isInstanceOwner = false;
        if (settings.SingleInstance.Enabled)
        {
            _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceServer.MutexName, out bool createdNew);
            isInstanceOwner = createdNew;
            if (!createdNew)
            {
                // We (the just-launched second process) currently hold foreground
                // rights — grant them to the running instance so its Activate()
                // can take the foreground. Without this Windows' foreground-lock
                // blocks it (it only flashes the taskbar) AND leaves other windows'
                // focus in a stuck state.
                AllowSetForegroundWindow(ASFW_ANY);
                if (SingleInstanceServer.TrySignal(initialArg ?? ""))
                {
                    _instanceMutex.Dispose();
                    _instanceMutex = null;
                    Shutdown();
                    return;
                }
                // Couldn't reach the owner — keep the (non-owned) handle and
                // launch, but we are NOT the owner: do not start a pipe server
                // (the name is held by the owner, so a second server would throw
                // on every accept and spin the listen loop).
            }
        }

        var window = new MainWindow(initialArg);

        // Only the mutex owner runs the pipe server, so later launches route
        // their file to the one instance that actually listens.
        if (isInstanceOwner)
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

    // Lets the running instance call SetForegroundWindow despite the foreground
    // lock. ASFW_ANY (-1) = grant to any process (the owner takes it immediately).
    private const int ASFW_ANY = -1;
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
