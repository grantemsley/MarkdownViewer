using System.Collections.Generic;

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
