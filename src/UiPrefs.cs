using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MarkdownViewer;

/// <summary>
/// Singleton bound from XAML for prefs that need to drive bindings on every
/// tree row at once (e.g. ShowExtensions changes 1000 labels; wrap toggles
/// 1000 TextBlocks). Updated by MainWindow whenever AppSettings changes.
/// </summary>
public class UiPrefs : INotifyPropertyChanged
{
    public static UiPrefs Instance { get; } = new();

    private bool _showExtensions = true;
    public bool ShowExtensions
    {
        get => _showExtensions;
        set { if (_showExtensions != value) { _showExtensions = value; OnChanged(); } }
    }

    private bool _tabsEnabled = true;
    /// <summary>Whether tabbed viewing is on — gates the "Open in new tab" sidebar
    /// context-menu items. Set once at startup from AppSettings.</summary>
    public bool TabsEnabled
    {
        get => _tabsEnabled;
        set { if (_tabsEnabled != value) { _tabsEnabled = value; OnChanged(); } }
    }

    private bool _wrap;
    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (_wrap == value) return;
            _wrap = value;
            OnChanged();
            OnChanged(nameof(SidebarWrap));
            OnChanged(nameof(SidebarTrim));
        }
    }

    public TextWrapping SidebarWrap => _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
    public TextTrimming SidebarTrim => _wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis;

    private double _rowMaxWidth = 200;
    /// <summary>
    /// Max width for a sidebar row's label. The WPF TreeViewItem template
    /// puts the row content in an Auto-width column, so TextBlocks have no
    /// natural width constraint and won't wrap or ellipsize. MainWindow
    /// keeps this in sync with the sidebar column's actual width and the
    /// templates bind their TextBlock MaxWidth to it.
    /// </summary>
    public double SidebarRowMaxWidth
    {
        get => _rowMaxWidth;
        set { if (_rowMaxWidth != value) { _rowMaxWidth = value; OnChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
