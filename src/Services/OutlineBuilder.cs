using System;
using System.Collections.Generic;

namespace MarkdownViewer.Services;

public static class OutlineBuilder
{
    /// <summary>
    /// Apply collapse rules to an outline tree.
    ///   - level &gt;= <paramref name="threshold"/> → start collapsed
    ///     (a threshold of 7 means never collapse by level, since max is 6).
    ///   - text contains <paramref name="needle"/> (if non-empty, case-insensitive)
    ///     → start collapsed.
    /// Recurses into children.
    /// </summary>
    public static void ApplyCollapse(IEnumerable<HeadingViewModel> nodes, int threshold, string needle)
    {
        foreach (var n in nodes)
        {
            var collapseByLevel = n.Level >= threshold;
            var collapseByText = needle.Length > 0 &&
                n.Text.Contains(needle, StringComparison.OrdinalIgnoreCase);
            n.IsExpanded = !(collapseByLevel || collapseByText);
            ApplyCollapse(n.Children, threshold, needle);
        }
    }
}
