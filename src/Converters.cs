using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MarkdownViewer.Models;
using MarkdownViewer.Services;

namespace MarkdownViewer;

public class BoolToVisibility : IValueConverter
{
    public static readonly BoolToVisibility Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is Visibility v) && v == Visibility.Visible;
}

/// <summary>
/// MultiBinding converter for sidebar TextBlock MaxWidth: takes the
/// per-row Depth and the sidebar's overall row max-width, returns a
/// usable max that accounts for the TreeView's per-level indent. Without
/// this, deeper rows had MaxWidth larger than their visible area, so
/// long names overflowed the sidebar's right edge before wrapping /
/// before showing the trailing ellipsis.
/// </summary>
public class DepthAdjustedWidth : IMultiValueConverter
{
    public static readonly DepthAdjustedWidth Instance = new();
    // WPF TreeViewItem default template indents by ~19 px per level.
    private const double IndentPerLevel = 19;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return double.PositiveInfinity;
        int depth = values[0] is int d ? d : 0;
        double max = values[1] is double w ? w : 200;
        return Math.Max(50, max - depth * IndentPerLevel);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a VaultNode to its shell-icon ImageSource. The bound DataContext on
/// the icon Image is the whole VaultNode; the converter looks up the cached
/// system icon for that node's path/kind. Folder lookups use a single
/// shared icon — they all look the same in Explorer anyway.
/// </summary>
public class VaultNodeIcon : IValueConverter
{
    public static readonly VaultNodeIcon Instance = new();
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not VaultNode n) return null;
        return FileIconProvider.Get(n.FullPath, n.Kind == VaultNodeKind.Folder);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Trims from the START of a string, prepending "…" when it exceeds the
/// (character-count) parameter. Used for path display where the meaningful
/// part — the leaf folder — sits at the end. WPF's built-in TextTrimming
/// only trims the tail.
/// </summary>
public class StartEllipsis : IValueConverter
{
    public static readonly StartEllipsis Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value?.ToString() ?? "";
        int max = 60;
        if (parameter is string p && int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            max = n;
        if (s.Length <= max) return s;
        // Keep the last (max-1) chars to make room for the leading ellipsis.
        return "…" + s.Substring(s.Length - (max - 1));
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
