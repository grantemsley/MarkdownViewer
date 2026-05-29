using System;
using MarkdownViewer.Models;
using Xunit;

namespace MarkdownViewer.Tests;

public class VaultNodeTests : IDisposable
{
    private readonly bool _origShowExtensions;

    public VaultNodeTests()
    {
        _origShowExtensions = UiPrefs.Instance.ShowExtensions;
    }

    public void Dispose()
    {
        UiPrefs.Instance.ShowExtensions = _origShowExtensions;
    }

    [Fact]
    public void DisplayName_MarkdownFile_DropsExtension_WhenShowExtensionsFalse()
    {
        UiPrefs.Instance.ShowExtensions = false;
        var node = new VaultNode { Name = "notes.md", FullPath = "x", Kind = VaultNodeKind.File };
        Assert.Equal("notes", node.DisplayName);
    }

    [Fact]
    public void DisplayName_MarkdownFile_KeepsExtension_WhenShowExtensionsTrue()
    {
        UiPrefs.Instance.ShowExtensions = true;
        var node = new VaultNode { Name = "notes.md", FullPath = "x", Kind = VaultNodeKind.File };
        Assert.Equal("notes.md", node.DisplayName);
    }

    [Fact]
    public void DisplayName_NonMarkdownFile_AlwaysKeepsExtension()
    {
        UiPrefs.Instance.ShowExtensions = false;
        var node = new VaultNode { Name = "script.ps1", FullPath = "x", Kind = VaultNodeKind.File };
        Assert.Equal("script.ps1", node.DisplayName);
    }

    [Fact]
    public void DisplayName_Folder_AlwaysKeepsName()
    {
        UiPrefs.Instance.ShowExtensions = false;
        var node = new VaultNode { Name = "my.folder", FullPath = "x", Kind = VaultNodeKind.Folder };
        Assert.Equal("my.folder", node.DisplayName);
    }

    [Fact]
    public void IsMarkdown_TrueForKnownExtensions()
    {
        foreach (var name in new[] { "a.md", "a.markdown", "a.mdown", "a.mkd" })
        {
            var node = new VaultNode { Name = name, FullPath = name, Kind = VaultNodeKind.File };
            Assert.True(node.IsMarkdown, $"Expected {name} to be markdown");
        }
    }

    [Fact]
    public void IsMarkdown_FalseForOtherExtensions()
    {
        var node = new VaultNode { Name = "a.txt", FullPath = "a.txt", Kind = VaultNodeKind.File };
        Assert.False(node.IsMarkdown);
    }

    [Fact]
    public void IsHidden_TrueForDotPrefix()
    {
        var node = new VaultNode { Name = ".git", FullPath = ".git", Kind = VaultNodeKind.Folder };
        Assert.True(node.IsHidden);
    }
}
