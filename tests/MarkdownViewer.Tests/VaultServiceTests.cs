using System;
using System.IO;
using System.Linq;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class VaultServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly List<string> _extraDirs = new();

    public VaultServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mvtest_vault_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        // Directory.Delete(recursive) unlinks a junction rather than deleting
        // through it, so the out-of-vault targets below survive to be cleaned up
        // on their own.
        try { Directory.Delete(_dir, recursive: true); } catch { }
        foreach (var d in _extraDirs)
            try { Directory.Delete(d, recursive: true); } catch { }
    }

    // A directory outside the vault root, for junction targets.
    private string NewOutsideDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "mvtest_outside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        _extraDirs.Add(d);
        return d;
    }

    [Fact]
    public void ResolveInput_FolderPath_ReturnsFolder()
    {
        var (folder, file) = VaultService.ResolveInput(_dir);
        Assert.Equal(_dir.TrimEnd(Path.DirectorySeparatorChar),
            folder.TrimEnd(Path.DirectorySeparatorChar));
        Assert.Null(file);
    }

    [Fact]
    public void ResolveInput_FilePath_ReturnsParentAndFile()
    {
        var p = Path.Combine(_dir, "note.md");
        File.WriteAllText(p, "# x");
        var (folder, file) = VaultService.ResolveInput(p);
        Assert.Equal(_dir.TrimEnd(Path.DirectorySeparatorChar),
            folder.TrimEnd(Path.DirectorySeparatorChar));
        Assert.Equal(p, file);
    }

    [Fact]
    public void ResolveInput_NonexistentPath_ReturnsEmpty()
    {
        var (folder, file) = VaultService.ResolveInput(Path.Combine(_dir, "nope"));
        Assert.Equal("", folder);
        Assert.Null(file);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveInput_NullOrWhitespace_ReturnsEmpty(string? arg)
    {
        var (folder, file) = VaultService.ResolveInput(arg);
        Assert.Equal("", folder);
        Assert.Null(file);
    }

    [Fact]
    public void Open_ScansRootLevelOnly_SubfolderNotLoaded()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllText(Path.Combine(_dir, "a.md"), "");
        File.WriteAllText(Path.Combine(_dir, "sub", "b.md"), "");

        using var vault = new VaultService();
        vault.Open(_dir);

        Assert.NotNull(vault.RootNode);
        Assert.True(vault.RootNode!.ChildrenLoaded);
        var rootChildren = vault.RootNode.Children;
        Assert.Contains(rootChildren, c => c.Name == "a.md" && c.Kind == VaultNodeKind.File);
        var sub = rootChildren.SingleOrDefault(c => c.Name == "sub" && c.Kind == VaultNodeKind.Folder);
        Assert.NotNull(sub);
        // Lazy: sub isn't scanned yet — it shows an arrow via a single placeholder,
        // but its real children (b.md) aren't loaded.
        Assert.False(sub!.ChildrenLoaded);
        Assert.True(sub.HasChildren);
        Assert.Single(sub.Children);
        Assert.True(sub.Children[0].IsPlaceholder);
    }

    // ── junctions / reparse points ───────────────────────────────────────────

    [Fact]
    public void Open_JunctionedFolder_AppearsInTree()
    {
        // The reported bug: a junctioned folder (e.g. .claude\docs -> elsewhere)
        // was skipped entirely and never showed up as a node.
        var outside = NewOutsideDir();
        File.WriteAllText(Path.Combine(outside, "linked.md"), "# x");
        TestJunction.Create(Path.Combine(_dir, "docs"), outside);

        using var vault = new VaultService();
        vault.Open(_dir);

        var docs = vault.RootNode!.Children.SingleOrDefault(
            c => c.Name == "docs" && c.Kind == VaultNodeKind.Folder);
        Assert.NotNull(docs);
        Assert.True(docs!.HasChildren);
    }

    [Fact]
    public void LoadChildren_JunctionedFolder_MaterializesTargetContents()
    {
        var outside = NewOutsideDir();
        File.WriteAllText(Path.Combine(outside, "linked.md"), "# x");
        Directory.CreateDirectory(Path.Combine(outside, "nested"));
        TestJunction.Create(Path.Combine(_dir, "docs"), outside);

        using var vault = new VaultService();
        vault.Open(_dir);
        var docs = vault.RootNode!.Children.Single(c => c.Name == "docs");

        vault.LoadChildren(docs);

        Assert.True(docs.ChildrenLoaded);
        Assert.Contains(docs.Children, c => c.Name == "linked.md" && c.Kind == VaultNodeKind.File);
        Assert.Contains(docs.Children, c => c.Name == "nested" && c.Kind == VaultNodeKind.Folder);
    }

    [Fact]
    public void Open_JunctionToAncestor_DoesNotRecurse()
    {
        // A junction pointing at its own ancestor is the case the old skip existed
        // to prevent. Loading is one level per expand, so it cannot recurse: the
        // loop node simply appears, and expanding it shows the root's entries again.
        File.WriteAllText(Path.Combine(_dir, "a.md"), "");
        TestJunction.Create(Path.Combine(_dir, "loop"), _dir);

        using var vault = new VaultService();
        vault.Open(_dir);
        var loop = vault.RootNode!.Children.Single(c => c.Name == "loop");

        vault.LoadChildren(loop);

        Assert.True(loop.ChildrenLoaded);
        Assert.Contains(loop.Children, c => c.Name == "a.md");
        Assert.Contains(loop.Children, c => c.Name == "loop");
    }

    [Fact]
    public void LoadChildren_MaterializesOneLevel()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllText(Path.Combine(_dir, "sub", "b.md"), "");

        using var vault = new VaultService();
        vault.Open(_dir);
        var sub = vault.RootNode!.Children.Single(c => c.Name == "sub");

        vault.LoadChildren(sub);

        Assert.True(sub.ChildrenLoaded);
        Assert.Single(sub.Children);
        Assert.False(sub.Children[0].IsPlaceholder);
        Assert.Equal("b.md", sub.Children[0].Name);
    }

    [Fact]
    public void Open_FolderWithOnlyFiles_HasArrow()
    {
        // A folder whose only entries are files still needs an expand arrow.
        Directory.CreateDirectory(Path.Combine(_dir, "docs"));
        File.WriteAllText(Path.Combine(_dir, "docs", "x.md"), "");

        using var vault = new VaultService();
        vault.Open(_dir);
        var docs = vault.RootNode!.Children.Single(c => c.Name == "docs");

        Assert.True(docs.HasChildren);
        Assert.Single(docs.Children);
        Assert.True(docs.Children[0].IsPlaceholder);
    }

    [Fact]
    public void RevealPath_LoadsAndExpandsAncestors()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "a", "b"));
        var deep = Path.Combine(_dir, "a", "b", "c.md");
        File.WriteAllText(deep, "");

        using var vault = new VaultService();
        vault.Open(_dir);

        var node = vault.RevealPath(deep, expandAncestors: true);

        Assert.NotNull(node);
        Assert.Equal("c.md", node!.Name);
        var a = vault.RootNode!.Children.Single(c => c.Name == "a");
        var b = a.Children.Single(c => c.Name == "b");
        Assert.True(a.IsExpanded);
        Assert.True(b.IsExpanded);
        Assert.True(a.ChildrenLoaded);
        Assert.True(b.ChildrenLoaded);
    }

    [Fact]
    public void RevealPath_NoExpand_LoadsButDoesNotExpandAncestors()
    {
        // Reveal without expanding: ancestors must still be loaded so the target
        // is reachable, but their IsExpanded stays false so a manual collapse
        // isn't overridden (e.g. on an F5 reload of the already-open file).
        Directory.CreateDirectory(Path.Combine(_dir, "a", "b"));
        var deep = Path.Combine(_dir, "a", "b", "c.md");
        File.WriteAllText(deep, "");

        using var vault = new VaultService();
        vault.Open(_dir);

        var node = vault.RevealPath(deep, expandAncestors: false);

        Assert.NotNull(node);
        Assert.Equal("c.md", node!.Name);
        var a = vault.RootNode!.Children.Single(c => c.Name == "a");
        var b = a.Children.Single(c => c.Name == "b");
        Assert.True(a.ChildrenLoaded);   // loaded so the walk can reach c.md
        Assert.True(b.ChildrenLoaded);
        Assert.False(a.IsExpanded);      // but not force-expanded
        Assert.False(b.IsExpanded);
    }

    [Fact]
    public void RevealPath_OutsideVault_ReturnsNull()
    {
        using var vault = new VaultService();
        vault.Open(_dir);
        Assert.Null(vault.RevealPath(@"C:\somewhere\else\x.md", expandAncestors: true));
    }

    [Fact]
    public void Open_EmptyFolder_StillProducesNode()
    {
        var empty = Path.Combine(_dir, "emptySub");
        Directory.CreateDirectory(empty);

        using var vault = new VaultService();
        vault.Open(empty);

        Assert.NotNull(vault.RootNode);
        Assert.Empty(vault.RootNode!.Children);
    }

    [Fact]
    public void Open_FolderWithEmptySubfolder_KeepsSubfolderInTree()
    {
        // Regression: empty subfolders used to vanish because they had no
        // visible children.
        Directory.CreateDirectory(Path.Combine(_dir, "empty"));

        using var vault = new VaultService();
        vault.Open(_dir);

        var empty = vault.RootNode!.Children.SingleOrDefault(
            c => c.Name == "empty" && c.Kind == VaultNodeKind.Folder);
        Assert.NotNull(empty);
        // An empty folder gets no arrow: no children, no placeholder.
        Assert.False(empty!.HasChildren);
        Assert.Empty(empty.Children);
    }

    [Fact]
    public void Open_NonexistentFolder_DoesNotThrow()
    {
        using var vault = new VaultService();
        vault.Open(Path.Combine(_dir, "ghost"));
        Assert.Null(vault.RootNode);
        Assert.False(vault.IsOpen);
    }
}
