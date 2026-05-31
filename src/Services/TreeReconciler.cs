using System.Collections.Generic;
using System.Collections.ObjectModel;
using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

/// <summary>
/// Syncs a bound <see cref="ObservableCollection{T}"/> of tree children to a
/// target ordered list, reusing existing node instances (matched by reference by
/// the caller) so survivors keep their identity, expansion, and loaded subtrees.
/// Pure and UI-agnostic — the collection-mutation logic that the file-watcher
/// reconcile depends on, kept here so it can be unit-tested without a watcher.
/// </summary>
public static class TreeReconciler
{
    public static void Sync(ObservableCollection<VaultNode> current, IReadOnlyList<VaultNode> target)
    {
        // Drop nodes no longer present (reference identity — the caller passes the
        // surviving instances through into target).
        for (int i = current.Count - 1; i >= 0; i--)
            if (!Contains(target, current[i])) current.RemoveAt(i);

        // Insert new nodes and move survivors into the target order.
        for (int i = 0; i < target.Count; i++)
        {
            var node = target[i];
            int idx = IndexOf(current, node);
            if (idx < 0) current.Insert(i, node);
            else if (idx != i) current.Move(idx, i);
        }
    }

    private static bool Contains(IReadOnlyList<VaultNode> list, VaultNode node)
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], node)) return true;
        return false;
    }

    private static int IndexOf(ObservableCollection<VaultNode> list, VaultNode node)
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], node)) return i;
        return -1;
    }
}
