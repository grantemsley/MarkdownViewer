using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

public static class TreeFilter
{
    /// <summary>
    /// Recursively apply visibility rules to a vault subtree.
    /// Returns true iff <paramref name="node"/> should be shown given the
    /// current prefs. Hidden files/folders (dotfiles) are filtered first;
    /// files we don't render specially (anything that's not markdown or a
    /// .jsonl transcript) are filtered second. Folders stay visible even
    /// when all their children are filtered out — the user is browsing a
    /// directory tree, not a search result.
    /// </summary>
    public static bool Apply(VaultNode node, FilePrefs prefs)
    {
        if (node.Kind == VaultNodeKind.File)
        {
            if (node.IsHidden && !prefs.ShowHidden) { node.IsVisible = false; return false; }
            if (!prefs.ShowNonMarkdown && !node.IsMarkdown && node.Extension != ".jsonl") { node.IsVisible = false; return false; }
            node.IsVisible = true;
            return true;
        }
        if (node.IsHidden && !prefs.ShowHidden) { node.IsVisible = false; return false; }
        foreach (var c in node.Children) Apply(c, prefs);
        node.IsVisible = true;
        return true;
    }
}
