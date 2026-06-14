using System.IO;

namespace MarkdownViewer.Models;

/// <summary>How an opened file should land relative to the current tab.</summary>
public enum OpenMode
{
    /// <summary>Take over the active tab (don't create a new one).</summary>
    ReplaceCurrent,
    /// <summary>Open in a brand-new tab.</summary>
    NewTab,
}

/// <summary>
/// One tab's identity: which folder it's rooted at and which file (if any) is
/// open. Deliberately plain data with NO WPF / WebView2 / VaultService coupling —
/// the heavy per-tab vault + WebView wiring lives in the view layer and is created
/// or torn down as tabs activate. Keeping this pure is what makes
/// <see cref="Services.TabManager"/> unit-testable.
/// </summary>
public sealed class TabState
{
    /// <summary>The tab's folder root, or null for a blank ("open a folder") tab.</summary>
    public string? VaultRoot { get; set; }

    /// <summary>The open file's full path, or null when no file is shown.</summary>
    public string? File { get; set; }

    /// <summary>
    /// What the tab strip shows: the file name, else the folder name, else
    /// "New tab" for a blank tab.
    /// </summary>
    public string Title
    {
        get
        {
            if (!string.IsNullOrEmpty(File)) return Path.GetFileName(File);
            return FolderName is { Length: > 0 } f ? f : "New tab";
        }
    }

    /// <summary>
    /// Small top line in the tab strip: the folder's name — always shown when the
    /// tab has a folder (so a tab is always folder-over-file and same-named files
    /// stay distinguishable). Empty only for a blank tab.
    /// </summary>
    public string TopLabel => FolderName;

    /// <summary>
    /// Main (lower) line: the file name; blank for a folder-only tab so its height
    /// matches a file tab; "New tab" for a blank tab.
    /// </summary>
    public string MainLabel =>
        !string.IsNullOrEmpty(File) ? Path.GetFileName(File)
        : string.IsNullOrEmpty(VaultRoot) ? "New tab" : "";

    private string FolderName =>
        string.IsNullOrEmpty(VaultRoot)
            ? ""
            : Path.GetFileName(VaultRoot.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)) is { Length: > 0 } n ? n : VaultRoot!;
}

/// <summary>
/// Serializable snapshot of a tab for session restore. Mirrors the load-bearing
/// bits of <see cref="TabState"/> (root + file); transient view state (scroll,
/// expansion) is not persisted.
/// </summary>
public sealed class TabSession
{
    public string? VaultRoot { get; set; }
    public string? File { get; set; }
}
