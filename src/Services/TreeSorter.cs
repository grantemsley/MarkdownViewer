using System;
using System.Collections.Generic;
using System.Linq;
using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

/// <summary>
/// Orders a set of sibling <see cref="VaultNode"/>s by a sort key and direction.
/// Pure over node properties (no disk access) — timestamps are captured on the
/// node at scan time — so re-sorting on a preference change is cheap and the
/// logic is unit-testable without a real folder. Folders and files are sorted
/// independently by the caller (see <see cref="VaultService"/>); folders stay
/// grouped above files regardless of key.
/// </summary>
public static class TreeSorter
{
    /// <summary>
    /// Return the nodes ordered by <paramref name="key"/> (name | created |
    /// modified | extension) in the given direction. Name is always the
    /// tiebreaker (ascending, ordinal-insensitive) so the order is deterministic
    /// when the primary key ties (e.g. extension for folders, equal timestamps).
    /// </summary>
    public static List<VaultNode> Sort(IEnumerable<VaultNode> nodes, string key, bool descending)
    {
        var list = nodes.ToList();
        list.Sort((a, b) =>
        {
            int c = Compare(a, b, key);
            if (descending) c = -c;
            if (c != 0) return c;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    private static int Compare(VaultNode a, VaultNode b, string key) => key switch
    {
        "created"   => a.CreatedUtc.CompareTo(b.CreatedUtc),
        "modified"  => a.ModifiedUtc.CompareTo(b.ModifiedUtc),
        "extension" => string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase),
        _           => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
    };
}
