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
    public void Open_BuildsRecursiveTree()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllText(Path.Combine(_dir, "a.md"), "");
        File.WriteAllText(Path.Combine(_dir, "sub", "b.md"), "");

        using var vault = new VaultService();
        vault.Open(_dir);

        Assert.NotNull(vault.RootNode);
        var rootChildren = vault.RootNode!.Children;
        Assert.Contains(rootChildren, c => c.Name == "a.md" && c.Kind == VaultNodeKind.File);
        var sub = rootChildren.SingleOrDefault(c => c.Name == "sub" && c.Kind == VaultNodeKind.Folder);
        Assert.NotNull(sub);
        Assert.Contains(sub!.Children, c => c.Name == "b.md");
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

        Assert.Contains(vault.RootNode!.Children,
            c => c.Name == "empty" && c.Kind == VaultNodeKind.Folder);
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
