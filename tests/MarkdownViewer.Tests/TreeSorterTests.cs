using System;
using System.Linq;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class TreeSorterTests
{
    private static VaultNode File(string name, DateTime? created = null, DateTime? modified = null) =>
        new()
        {
            Name = name,
            FullPath = name,
            Kind = VaultNodeKind.File,
            CreatedUtc = created ?? DateTime.MinValue,
            ModifiedUtc = modified ?? DateTime.MinValue,
        };

    [Fact]
    public void Sort_ByName_Ascending_IsCaseInsensitive()
    {
        var nodes = new[] { File("banana.md"), File("Apple.md"), File("cherry.md") };
        var sorted = TreeSorter.Sort(nodes, "name", descending: false);
        Assert.Equal(new[] { "Apple.md", "banana.md", "cherry.md" }, sorted.Select(n => n.Name));
    }

    [Fact]
    public void Sort_ByName_Descending_Reverses()
    {
        var nodes = new[] { File("a.md"), File("b.md"), File("c.md") };
        var sorted = TreeSorter.Sort(nodes, "name", descending: true);
        Assert.Equal(new[] { "c.md", "b.md", "a.md" }, sorted.Select(n => n.Name));
    }

    [Fact]
    public void Sort_ByCreated_Ascending()
    {
        var nodes = new[]
        {
            File("new.md", created: new DateTime(2026, 5, 3)),
            File("old.md", created: new DateTime(2020, 1, 1)),
            File("mid.md", created: new DateTime(2024, 6, 6)),
        };
        var sorted = TreeSorter.Sort(nodes, "created", descending: false);
        Assert.Equal(new[] { "old.md", "mid.md", "new.md" }, sorted.Select(n => n.Name));
    }

    [Fact]
    public void Sort_ByModified_Descending_NewestFirst()
    {
        var nodes = new[]
        {
            File("a.md", modified: new DateTime(2020, 1, 1)),
            File("b.md", modified: new DateTime(2026, 1, 1)),
            File("c.md", modified: new DateTime(2023, 1, 1)),
        };
        var sorted = TreeSorter.Sort(nodes, "modified", descending: true);
        Assert.Equal(new[] { "b.md", "c.md", "a.md" }, sorted.Select(n => n.Name));
    }

    [Fact]
    public void Sort_ByExtension_GroupsByExtension_ThenName()
    {
        var nodes = new[] { File("z.md"), File("a.txt"), File("a.md"), File("b.txt") };
        var sorted = TreeSorter.Sort(nodes, "extension", descending: false);
        // .md before .txt; within each extension, name ascending.
        Assert.Equal(new[] { "a.md", "z.md", "a.txt", "b.txt" }, sorted.Select(n => n.Name));
    }

    [Fact]
    public void Sort_NameTiebreak_WhenPrimaryKeyTies()
    {
        // Equal timestamps -> deterministic name-ascending order regardless of
        // input order, even when sorting descending on the primary key.
        var t = new DateTime(2025, 1, 1);
        var nodes = new[] { File("c.md", created: t), File("a.md", created: t), File("b.md", created: t) };
        var sorted = TreeSorter.Sort(nodes, "created", descending: true);
        Assert.Equal(new[] { "a.md", "b.md", "c.md" }, sorted.Select(n => n.Name));
    }

    [Fact]
    public void Sort_UnknownKey_FallsBackToName()
    {
        var nodes = new[] { File("b.md"), File("a.md") };
        var sorted = TreeSorter.Sort(nodes, "bogus", descending: false);
        Assert.Equal(new[] { "a.md", "b.md" }, sorted.Select(n => n.Name));
    }
}
