using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MarkdownViewer.Services;

/// <summary>
/// Per-user (HKCU) Windows shell integration: file-type associations for
/// <c>.md</c> / <c>.jsonl</c> and the Explorer "Open in MarkdownViewer" folder
/// context-menu verbs. Everything lives under <c>HKCU\Software\Classes</c>, so
/// no admin rights are needed and nothing the app does can affect other users.
///
/// This is the in-app replacement for the old <c>installer\*.ps1</c> scripts.
/// Registration deliberately does NOT seize the current default handler —
/// Windows guards that with a per-user UserChoice hash; the user opts in via
/// "Open with → Choose another app → Always".
/// </summary>
public static class WindowsIntegrationService
{
    public const string ProgId = "MarkdownViewer.Document";
    public const string ContextVerb = "Open in MarkdownViewer";
    private const string FriendlyName = "Markdown / transcript document";
    private const string ClassesRoot = @"Software\Classes";

    /// <summary>Extensions associated by default.</summary>
    public static readonly string[] DefaultExtensions = { ".md", ".jsonl" };

    private static readonly string[] ContextMenuRoots =
    {
        @"Directory\shell",             // right-click a folder
        @"Directory\Background\shell",  // right-click empty space inside a folder
    };

    /// <summary>
    /// Absolute path of the running executable. Uses <see cref="Environment.ProcessPath"/>
    /// (the apphost, correct even for a single-file publish) with a MainModule fallback.
    /// </summary>
    public static string ExePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("Could not determine the executable path.");

    // ─── Pure helpers (unit-tested) ──────────────────────────────────────────

    /// <summary>Trim and ensure a single leading dot. Empty input stays empty.</summary>
    public static string NormalizeExtension(string ext)
    {
        ext = (ext ?? "").Trim();
        if (ext.Length == 0) return "";
        return ext.StartsWith('.') ? ext : "." + ext;
    }

    /// <summary>A shell open command: <c>"exe" "token"</c> (token is %1 for files, %V for folders).</summary>
    public static string BuildCommand(string exe, string token) => $"\"{exe}\" \"{token}\"";

    // ─── File associations ───────────────────────────────────────────────────

    public static void RegisterFileAssociations(string exe, IEnumerable<string>? extensions = null)
    {
        extensions ??= DefaultExtensions;

        using (var prog = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ProgId}"))
        {
            prog.SetValue(null, FriendlyName);
            prog.SetValue("FriendlyTypeName", FriendlyName);
            using (var icon = prog.CreateSubKey("DefaultIcon"))
                icon.SetValue(null, $"\"{exe}\",0");
            using (var cmd = prog.CreateSubKey(@"shell\open\command"))
                cmd.SetValue(null, BuildCommand(exe, "%1"));
        }

        foreach (var raw in extensions)
        {
            var ext = NormalizeExtension(raw);
            if (ext.Length == 0) continue;
            using var owp = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ext}\OpenWithProgids");
            // Empty REG_SZ named after the ProgId — the marker Explorer reads to
            // offer this handler in the "Open with" list without hijacking the default.
            owp.SetValue(ProgId, "", RegistryValueKind.String);
        }

        NotifyShellChanged();
    }

    public static void UnregisterFileAssociations(IEnumerable<string>? extensions = null)
    {
        extensions ??= DefaultExtensions;

        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{ProgId}", throwOnMissingSubKey: false);

        foreach (var raw in extensions)
        {
            var ext = NormalizeExtension(raw);
            if (ext.Length == 0) continue;
            using var owp = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ext}\OpenWithProgids", writable: true);
            if (owp?.GetValue(ProgId) != null)
                owp.DeleteValue(ProgId, throwOnMissingValue: false);
        }

        NotifyShellChanged();
    }

    /// <summary>True when our ProgId's open command points at <paramref name="exe"/>.</summary>
    public static bool AreFileAssociationsRegistered(string exe)
    {
        using var cmd = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ProgId}\shell\open\command");
        return (cmd?.GetValue(null) as string) == BuildCommand(exe, "%1");
    }

    // ─── Explorer context menu ───────────────────────────────────────────────

    public static void RegisterContextMenu(string exe)
    {
        foreach (var root in ContextMenuRoots)
        {
            using var verb = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{root}\{ContextVerb}");
            verb.SetValue(null, ContextVerb);
            verb.SetValue("Icon", exe);
            using var cmd = verb.CreateSubKey("command");
            cmd.SetValue(null, BuildCommand(exe, "%V"));
        }

        NotifyShellChanged();
    }

    public static void UnregisterContextMenu()
    {
        foreach (var root in ContextMenuRoots)
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{root}\{ContextVerb}", throwOnMissingSubKey: false);

        NotifyShellChanged();
    }

    /// <summary>True when the folder-verb command points at <paramref name="exe"/>.</summary>
    public static bool IsContextMenuRegistered(string exe)
    {
        using var cmd = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\Directory\shell\{ContextVerb}\command");
        return (cmd?.GetValue(null) as string) == BuildCommand(exe, "%V");
    }

    // ─── Shell notification ──────────────────────────────────────────────────

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>Tell Explorer associations changed so icons/menus refresh immediately.</summary>
    public static void NotifyShellChanged() =>
        SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
}
