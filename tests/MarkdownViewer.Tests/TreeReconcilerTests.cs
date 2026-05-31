using System.Collections.ObjectModel;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class TreeReconcilerTests
{
    private static VaultNode File(string name) =>
        new() { Name = name, Kind = VaultNodeKind.File };

    [Fact]
    public void Sync_AddsRemovesAndPreservesIdentity()
    {
        var keep = File("a.md");
        var drop = File("b.md");
        var current = new ObservableCollection<VaultNode> { keep, drop };

        var added = File("c.md");
        // Target reuses the surviving instance (keep), drops b.md, adds c.md.
        var target = new[] { keep, added };

        TreeReconciler.Sync(current, target);

        Assert.Equal(2, current.Count);
        Assert.Same(keep, current[0]);   // identity preserved
        Assert.Same(added, current[1]);
        Assert.DoesNotContain(drop, current);
    }

    [Fact]
    public void Sync_ReordersToMatchTarget()
    {
        var a = File("a.md");
        var b = File("b.md");
        var c = File("c.md");
        var current = new ObservableCollection<VaultNode> { c, a, b };
        var target = new[] { a, b, c };

        TreeReconciler.Sync(current, target);

        Assert.Equal(new[] { a, b, c }, current);
    }

    [Fact]
    public void Sync_PreservesExpansionOfSurvivingFolder()
    {
        var folder = new VaultNode { Name = "sub", Kind = VaultNodeKind.Folder, IsExpanded = true, ChildrenLoaded = true };
        folder.Children.Add(File("inner.md"));
        var current = new ObservableCollection<VaultNode> { folder };

        // A sibling is added; the folder survives untouched.
        var sibling = File("new.md");
        var target = new[] { folder, sibling };

        TreeReconciler.Sync(current, target);

        Assert.Same(folder, current[0]);
        Assert.True(folder.IsExpanded);            // expansion intact
        Assert.True(folder.ChildrenLoaded);        // loaded subtree intact
        Assert.Single(folder.Children);
    }

    [Fact]
    public void Sync_EmptyTarget_ClearsAll()
    {
        var current = new ObservableCollection<VaultNode> { File("a.md"), File("b.md") };
        TreeReconciler.Sync(current, System.Array.Empty<VaultNode>());
        Assert.Empty(current);
    }
}
