using System;
using Velopack;

namespace MarkdownViewer;

/// <summary>
/// Explicit entry point (csproj StartupObject) replacing the WPF-generated
/// Main. Exists solely so VelopackApp runs before any WPF code: during
/// install/update/uninstall Velopack relaunches the exe with hook arguments
/// and Run() must handle them (and exit) before a window ever appears.
/// Normal launches fall straight through to the same App startup the
/// generated Main performed (InitializeComponent wires App.xaml's Startup
/// event and resources; Run enters the dispatcher loop).
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
