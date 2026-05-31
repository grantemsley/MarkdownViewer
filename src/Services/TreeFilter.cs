using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

public static class TreeFilter
{
    /// <summary>
    /// Apply visibility rules to a vault subtree. Returns true iff
    /// <paramref name="node"/> should be shown given the current prefs. Hidden
    /// files/folders (dotfiles) are filtered first; files we don't render
    /// specially (anything that's not markdown or a .jsonl transcript) are
    /// filtered second. Folders stay visible even when all their children are
    /// filtered out — the user is browsing a directory tree, not a search result.
    ///
    /// Only recurses into <b>loaded</b> folders: unloaded folders hold just a
    /// placeholder and are filtered later, when their children are loaded (the UI
    /// re-applies via <see cref="ApplyToChildren"/> on the FolderChildrenChanged
    /// event).
    /// </summary>
    public static bool Apply(VaultNode node, FilePrefs prefs) => Apply(node, prefs, isRoot: true);

    /// <summary>
    /// Filter just a folder's immediate children (each as a non-root), without
    /// touching the folder's own visibility. Used when a folder's children are
    /// newly materialized (lazy load or watcher reconcile).
    /// </summary>
    public static void ApplyToChildren(VaultNode folder, FilePrefs prefs)
    {
        foreach (var c in folder.Children) Apply(c, prefs, isRoot: false);
    }

    private static bool Apply(VaultNode node, FilePrefs prefs, bool isRoot)
    {
        // Placeholders are never shown and are replaced on load — leave them be.
        if (node.IsPlaceholder) return true;

        if (node.Kind == VaultNodeKind.File)
        {
            if (node.IsHidden && !prefs.ShowHidden) { node.IsVisible = false; return false; }
            if (!prefs.ShowNonMarkdown && !node.IsMarkdown && node.Extension != ".jsonl") { node.IsVisible = false; return false; }
            node.IsVisible = true;
            return true;
        }
        // Never hide the node the user explicitly opened (the top of this call)
        // by its own hidden flag — and returning early there would also skip
        // filtering its children, leaking unfiltered nodes into the tree.
        if (!isRoot && node.IsHidden && !prefs.ShowHidden) { node.IsVisible = false; return false; }
        if (node.ChildrenLoaded)
            foreach (var c in node.Children) Apply(c, prefs, isRoot: false);
        node.IsVisible = true;
        return true;
    }
}
