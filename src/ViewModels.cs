using System.Collections.Generic;
using System.Windows;

namespace MarkdownViewer;

public class HeadingViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public int Level { get; set; }
    public string Text { get; set; } = "";
    public string Id { get; set; } = "";
    /// <summary>
    /// Visual tree depth (root = 0). Different from Level: an H3 nested
    /// under an H1 is Level=3, Depth=1. Used for per-row MaxWidth.
    /// </summary>
    public int Depth { get; set; }
    public List<HeadingViewModel> Children { get; } = new();

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public class FolderRow
{
    public string Path { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsCurrent { get; set; }
    public bool IsPinned { get; set; }
    public string PinGlyph => IsPinned ? "📌" : "📍";
}

/// <summary>
/// One row in the sidebar search-results list: either a <b>file header</b>
/// (bold, no indent — clicking opens the file at the top) or a <b>match line</b>
/// (indented — clicking opens the file and scrolls to <see cref="Line"/>).
/// </summary>
public sealed class SearchRowVM
{
    public bool IsFileHeader { get; init; }
    public string Display { get; init; } = "";
    public string? ToolTip { get; init; }
    public string FullPath { get; init; } = "";
    /// <summary>1-based match line, or 0 for a header / filename-only match (open at top).</summary>
    public int Line { get; init; }

    public FontWeight Weight => IsFileHeader ? FontWeights.SemiBold : FontWeights.Normal;
    public Thickness Indent => IsFileHeader ? new Thickness(0, 2, 0, 0) : new Thickness(14, 0, 0, 0);
}
