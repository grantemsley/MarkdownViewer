using System;
using System.IO;
using System.Windows;

namespace MarkdownViewer;

public partial class App : Application
{
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
        var window = new MainWindow(initialArg);
        window.Show();
    }
}
