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

    public VaultServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mvtest_vault_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
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
