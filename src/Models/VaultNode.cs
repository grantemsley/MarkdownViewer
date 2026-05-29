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
