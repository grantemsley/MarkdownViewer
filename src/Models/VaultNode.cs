using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace MarkdownViewer.Models;

public enum VaultNodeKind { Folder, File }

public class VaultNode : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public VaultNodeKind Kind { get; init; }
    public ObservableCollection<VaultNode> Children { get; } = new();

    // Filesystem timestamps captured at scan time (UTC). Used by TreeSorter for
    // the created/modified sort keys without re-statting on every re-sort. Stay
    // default for placeholders (never sorted).
    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }

    // ── Lazy-loading state ────────────────────────────────────────────
    // The tree is scanned one folder level at a time. A folder is loaded
    // (its real children materialized) only when it's expanded or revealed.

    /// <summary>
    /// True once this folder's immediate children have been scanned. Files are
    /// always "loaded". Guards re-scanning and tells the filter/reconcile code
    /// whether <see cref="Children"/> reflects disk or is just a placeholder.
    /// </summary>
    public bool ChildrenLoaded { get; set; }

    /// <summary>
    /// Whether this folder has any on-disk entries (sub-folders or files),
    /// determined by a cheap peek at scan time. Drives whether an expand arrow
    /// shows: an unloaded folder with children holds a single placeholder child
    /// so WPF renders the toggle; loading replaces it with the real children.
    /// </summary>
    public bool HasChildren { get; set; }

    /// <summary>
    /// A throwaway child inserted under an unloaded folder purely so the
    /// TreeView shows an expand arrow. Never rendered (the parent is collapsed)
    /// and always replaced on first load. Skipped by filtering/selection.
    /// </summary>
    public bool IsPlaceholder { get; init; }

    public static VaultNode MakePlaceholder(int depth) =>
        new() { Name = "", Kind = VaultNodeKind.File, Depth = depth, IsPlaceholder = true };

    /// <summary>
    /// Visual nesting depth in the tree (root = 0). Used to compute the
    /// per-row MaxWidth in XAML so deep rows don't overflow the sidebar.
    /// </summary>
    public int Depth { get; init; }

    // Convenience flags used by the sidebar context menu to gate items.
    public bool IsRootFolder => Kind == VaultNodeKind.Folder && Depth == 0;
    public bool IsNonRootFolder => Kind == VaultNodeKind.Folder && Depth > 0;
    public bool IsFile => Kind == VaultNodeKind.File;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnChanged(); } }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnChanged(); } }
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible != value) { _isVisible = value; OnChanged(); } }
    }

    /// <summary>
    /// What the sidebar shows for this row. Folders and non-md files always
    /// keep their full name (you can't tell `notes.md` from `notes.png`
    /// otherwise). Markdown files drop the `.md` when ShowExtensions is off.
    /// Listens to UiPrefs.ShowExtensions and renotifies on change.
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (Kind == VaultNodeKind.Folder) return Name;
            if (!UiPrefs.Instance.ShowExtensions &&
                Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                return Name[..^3];
            return Name;
        }
    }

    /// <summary>
    /// Re-raise PropertyChanged for DisplayName on this node and all children.
    /// Called by MainWindow after UiPrefs.ShowExtensions changes.
    /// </summary>
    public void RefreshDisplay()
    {
        OnChanged(nameof(DisplayName));
        foreach (var c in Children) c.RefreshDisplay();
    }

    public string Extension =>
        Kind == VaultNodeKind.File ? Path.GetExtension(Name).ToLowerInvariant() : "";

    public bool IsMarkdown =>
        Kind == VaultNodeKind.File && (Extension is ".md" or ".markdown" or ".mdown" or ".mkd");

    // Hidden if dot-prefixed (Unix convention) OR carrying the Windows
    // hidden attribute. The attribute lookup is a syscall, so cache it for
    // the node's lifetime (nodes are rebuilt on rescan, so it can't go stale).
    private bool? _hasHiddenAttr;
    public bool IsHidden =>
        Name.StartsWith(".") || (_hasHiddenAttr ??= HasHiddenAttribute(FullPath));

    private static bool HasHiddenAttribute(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try { return (File.GetAttributes(path) & FileAttributes.Hidden) == FileAttributes.Hidden; }
        catch { return false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class HeadingNode
{
    public int Level { get; init; }
    public string Text { get; init; } = "";
    public string Id { get; init; } = "";
    public List<HeadingNode> Children { get; } = new();
    public bool IsExpanded { get; set; } = true;
}
