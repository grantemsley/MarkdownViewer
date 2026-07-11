using System.Collections.Generic;

namespace MarkdownViewer.Models;

public static class SettingsSchema
{
    /// <summary>
    /// Bump this when the settings shape changes. On load, a mismatched
    /// (or missing) version causes the file to be discarded and rewritten
    /// from defaults — no migration ladder.
    /// </summary>
    public const int Current = 2;
}

public class AppSettings
{
    public int SchemaVersion { get; set; } = SettingsSchema.Current;
    public string Theme { get; set; } = "system"; // system | light | dark
    public FilePrefs Files { get; set; } = new();
    public SortPrefs Sorting { get; set; } = new();
    public ReadingPrefs Reading { get; set; } = new();
    public OutlinePrefs Outline { get; set; } = new();
    public WindowPrefs Window { get; set; } = new();
    public VaultPrefs Vaults { get; set; } = new();
    public TranscriptPrefs Transcripts { get; set; } = new();
    public UpdatePrefs Updates { get; set; } = new();
    public TabsPrefs Tabs { get; set; } = new();
    public SingleInstancePrefs SingleInstance { get; set; } = new();

    /// <summary>
    /// Coerce loaded values into valid ranges/sets and replace any null
    /// sub-objects (a JSON null would otherwise NRE downstream). Called after
    /// deserialize so a hand-edited or partly corrupt — but still parseable —
    /// file can't feed nonsense (e.g. a 99999px font) into the renderer.
    /// </summary>
    public void Normalize()
    {
        Files ??= new();
        Sorting ??= new();
        Reading ??= new();
        Outline ??= new();
        Window ??= new();
        Vaults ??= new();
        Transcripts ??= new();
        Updates ??= new();
        Tabs ??= new();
        SingleInstance ??= new();

        // Coalesce null collections too. A hand-edited (but still parseable) file
        // with e.g. "sessions": null passes the schema-version check, so without
        // this it NREs downstream (RestoreTabsFromSession, recents, etc.) and the
        // failure surfaces as a spurious "WebView2 init failed" dialog.
        Vaults.Pinned ??= new();
        Vaults.Recents ??= new();
        Vaults.LastFile ??= new();
        Tabs.Sessions ??= new();
        Transcripts.VisibleCategories ??= new();

        Theme = Theme is "light" or "dark" or "system" ? Theme : "system";
        Sorting.Normalize();
        Reading.Normalize();
    }
}

public class FilePrefs
{
    public bool ShowExtensions { get; set; } = true;
    public bool ShowNonMarkdown { get; set; } = false;
    public bool ShowHidden { get; set; } = false;
    public bool WrapSidebar { get; set; } = false;
}

public class SortPrefs
{
    // Sort key: name | created | modified | extension. Folders and files are
    // sorted independently; folders are always grouped above files in the tree.
    public string FolderKey { get; set; } = "name";
    public string FileKey { get; set; } = "name";
    // Direction: asc | desc.
    public string FolderDir { get; set; } = "asc";
    public string FileDir { get; set; } = "asc";

    // Keep these sets in sync with the ComboBox tags in PreferencesWindow.
    public void Normalize()
    {
        FolderKey = ValidKey(FolderKey);
        FileKey = ValidKey(FileKey);
        FolderDir = ValidDir(FolderDir);
        FileDir = ValidDir(FileDir);
    }

    private static string ValidKey(string k) =>
        k is "name" or "created" or "modified" or "extension" ? k : "name";
    private static string ValidDir(string d) => d is "asc" or "desc" ? d : "asc";
}

public class ReadingPrefs
{
    public string Typeface { get; set; } = "system"; // system | sans | serif | mono
    public int FontSize { get; set; } = 14;          // 11..22
    public int MarginPct { get; set; } = 85;         // 50..100
    public bool ShowLineNumbers { get; set; } = false;
    public string BodyStyle { get; set; } = "win11"; // win11 | github

    // Outline non-standard HTML tags (e.g. <example>, <thinking>) so their
    // boundaries are visible. The browser renders unknown tags' content but
    // drops the invisible wrapper; this surfaces it. Default on.
    public bool HighlightCustomTags { get; set; } = true;

    // Keep these ranges/sets in sync with the clamps in PreferencesWindow.Persist.
    public void Normalize()
    {
        Typeface = Typeface is "system" or "sans" or "serif" or "mono" ? Typeface : "system";
        BodyStyle = BodyStyle is "win11" or "github" ? BodyStyle : "win11";
        FontSize = System.Math.Clamp(FontSize, 11, 22);
        MarginPct = System.Math.Clamp(MarginPct, 50, 100);
    }
}

public class OutlinePrefs
{
    public int CollapseBelow { get; set; } = 7;       // 7 == never
    public string CollapseContaining { get; set; } = "";
}

public class WindowPrefs
{
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 1200;
    public double Height { get; set; } = 800;
    public double SidebarWidth { get; set; } = 240;

    // Vertical split inside the sidebar: fraction of available height
    // allocated to the Folder pane. Outline gets the rest. Range 0.1..0.9
    // (clamped on save so the user can't accidentally lose a section).
    public double SidebarFolderRatio { get; set; } = 0.5;
}

public class VaultPrefs
{
    public string Current { get; set; } = "";
    public List<string> Pinned { get; set; } = new();
    public List<string> Recents { get; set; } = new();
    public Dictionary<string, string> LastFile { get; set; } = new();
}

public class TabsPrefs
{
    // Tabbed viewing — optional, default on. Read at startup; toggling it takes
    // effect on next launch (the tab machinery is wired at construction).
    public bool Enabled { get; set; } = true;

    // Open tabs from the last session (root + file each) and which was active, so
    // a plain launch reopens them. Restored only when Enabled; tabs whose folder
    // no longer exists are dropped on restore.
    public List<TabSession> Sessions { get; set; } = new();
    public int ActiveIndex { get; set; }

    // A file opened into the already-running window (via single-instance) opens in
    // a new tab (default) or replaces the current tab. Only applies when tabs are
    // on; with tabs off the file always replaces.
    public bool OpenIncomingInNewTab { get; set; } = true;
}

public class SingleInstancePrefs
{
    // Reuse one window — a second launch hands its file to the running instance
    // and exits, instead of opening a new process. Default on; read at startup
    // (a startup-time decision, so toggling takes effect on next launch).
    public bool Enabled { get; set; } = true;
}

public class UpdatePrefs
{
    // Check GitHub Releases once at startup and surface a banner if a newer
    // version exists. Notify-only — nothing is downloaded or installed.
    public bool CheckForUpdates { get; set; } = true;

    // The version the user dismissed, so the same update isn't re-announced on
    // every launch. Empty = nothing dismissed yet.
    public string DismissedVersion { get; set; } = "";

    // UTC time of the last update check that actually reached GitHub. Throttles
    // checks to at most once per UpdateService.CheckInterval (a day). Default
    // (DateTime.MinValue) = never checked, so the first launch checks.
    public System.DateTime LastCheckUtc { get; set; }
}

public class TranscriptPrefs
{
    // Keys match TranscriptService category constants. Missing keys fall
    // through to the renderer's static defaults, so adding a new category
    // later doesn't require a settings migration.
    public Dictionary<string, bool> VisibleCategories { get; set; } = new()
    {
        ["conversation"] = true,
        ["tool"]         = true,
        ["thinking"]     = false,
        ["hook"]         = false,
        ["skill"]        = false,
        ["mcp"]          = false,
        ["toolsdelta"]   = false,
        ["queue"]        = false,
        ["meta"]         = false,
    };
}
