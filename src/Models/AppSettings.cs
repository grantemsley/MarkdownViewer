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
    public string SidebarTab { get; set; } = "folder"; // legacy; ignored on load.

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
