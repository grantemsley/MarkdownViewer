using MarkdownViewer.Models;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class TreeFilterTests
{
    private static VaultNode File(string name) =>
        new() { Name = name, FullPath = name, Kind = VaultNodeKind.File };

    private static VaultNode Folder(string name, params VaultNode[] children)
    {
        var n = new VaultNode { Name = name, FullPath = name, Kind = VaultNodeKind.Folder };
        foreach (var c in children) n.Children.Add(c);
        return n;
    }

    [Fact]
    public void Apply_HidesDotFiles_WhenShowHiddenFalse()
    {
        var node = File(".secret");
        var prefs = new FilePrefs { ShowHidden = false, ShowNonMarkdown = true };
        var visible = TreeFilter.Apply(node, prefs);
        Assert.False(visible);
        Assert.False(node.IsVisible);
    }

    [Fact]
    public void Apply_ShowsDotFiles_WhenShowHiddenTrue()
    {
        var node = File(".secret.md");
        var prefs = new FilePrefs { ShowHidden = true, ShowNonMarkdown = false };
        var visible = TreeFilter.Apply(node, prefs);
        Assert.True(visible);
        Assert.True(node.IsVisible);
    }

    [Fact]
    public void Apply_HidesNonMarkdownFiles_WhenShowNonMarkdownFalse()
    {
        var node = File("script.ps1");
        var prefs = new FilePrefs { ShowHidden = false, ShowNonMarkdown = false };
        var visible = TreeFilter.Apply(node, prefs);
        Assert.False(visible);
        Assert.False(node.IsVisible);
    }

    [Fact]
    public void Apply_ShowsNonMarkdownFiles_WhenShowNonMarkdownTrue()
    {
        var node = File("script.ps1");
        var prefs = new FilePrefs { ShowHidden = false, ShowNonMarkdown = true };
        var visible = TreeFilter.Apply(node, prefs);
        Assert.True(visible);
        Assert.True(node.IsVisible);
    }

    [Fact]
    public void Apply_ShowsJsonlFiles_WhenShowNonMarkdownFalse()
    {
        // .jsonl transcripts get the markdown viewer treatment, so they
        // stay visible even with the non-markdown filter on.
        var node = File("session.jsonl");
        var prefs = new FilePrefs { ShowHidden = false, ShowNonMarkdown = false };
        var visible = TreeFilter.Apply(node, prefs);
        Assert.True(visible);
        Assert.True(node.IsVisible);
    }

    [Fact]
    public void Apply_HiddenMarkdownFile_HiddenWhenShowHiddenFalse()
    {
        // Dotfile rule wins over the markdown rule.
        var node = File(".private.md");
        var prefs = new FilePrefs { ShowHidden = false, ShowNonMarkdown = false };
        var visible = TreeFilter.Apply(node, prefs);
        Assert.False(visible);
        Assert.False(node.IsVisible);
    }

    [Fact]
    public void Apply_EmptyFolder_IsStillVisible()
    {
        var node = Folder("emptyDir");
        var prefs = new FilePrefs { ShowHidden = false, ShowNonMarkdown = false };
        var visible = TreeFilter.Apply(node, prefs);
        Assert.True(visible);
        Assert.True(node.IsVisible);
    }

    [Fact]
    public void Apply_FolderWithOnlyFilteredChildren_StillVisible()
    {
        // The folder itself stays visible even when every child is filtered.
        var folder = Folder("sub", File("a.txt"), File("b.bin"));
        var prefs = new FilePrefs { ShowHidden = false, ShowNonMarkdown = false };
        var visible = TreeFilter.Apply(folder, prefs);
        Assert.True(visible);
        Assert.True(folder.IsVisible);
        foreach (var c in folder.Children) Assert.False(c.IsVisible);
    }

    [Fact]
    public void Apply_DotFolder_HiddenWhenShowHiddenFalse()
    {
        var folder = Folder(".git", File("HEAD"));
        var prefs = new FilePrefs { ShowHidden = false, ShowNonMarkdown = true };
        var visible = TreeFilter.Apply(folder, prefs);
        Assert.False(visible);
        Assert.False(folder.IsVisible);
    }

    [Fact]
    public void Apply_RecursesIntoSubfolders()
    {
        var inner = File("note.md");
        var sub = Folder("sub", inner);
        var root = Folder("root", sub);
        var prefs = new FilePrefs { ShowHidden = false, ShowNonMarkdown = false };
        TreeFilter.Apply(root, prefs);
        Assert.True(inner.IsVisible);
        Assert.True(sub.IsVisible);
        Assert.True(root.IsVisible);
    }
}
