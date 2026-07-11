using System;
using System.Collections.Generic;

namespace MarkdownViewer.Services;

public static class OutlineBuilder
{
    /// <summary>
    /// Build the nested outline tree from a flat heading list. Each heading
    /// becomes a child of the most recent heading with a strictly lower level
    /// still on the stack; equal or higher levels pop back up first, so equal
    /// levels land as siblings under the same parent. Depth is the number of
    /// ancestors (root = 0), which can differ from Level when levels are
    /// skipped (e.g. an H1 followed directly by an H4 sits at Depth 1).
    /// </summary>
    public static List<HeadingViewModel> BuildTree(IEnumerable<HeadingEntry> headings)
    {
        var roots = new List<HeadingViewModel>();
        var stack = new Stack<HeadingViewModel>();
        foreach (var entry in headings)
        {
            var h = new HeadingViewModel
            {
                Level = entry.Level,
                Text = entry.Text,
                Id = entry.Id,
            };
            while (stack.Count > 0 && stack.Peek().Level >= h.Level) stack.Pop();
            h.Depth = stack.Count; // visual depth = number of ancestors
            if (stack.Count == 0) roots.Add(h);
            else stack.Peek().Children.Add(h);
            stack.Push(h);
        }
        return roots;
    }

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
