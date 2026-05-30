using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class VaultPathsTests
{
    private const string Root = @"C:\vault";

    [Theory]
    [InlineData("note.md", @"C:\vault\note.md")]
    [InlineData("sub/pic.png", @"C:\vault\sub\pic.png")]
    [InlineData("sub\\pic.png", @"C:\vault\sub\pic.png")]
    [InlineData("a/../b.png", @"C:\vault\b.png")]               // collapses, stays inside
    [InlineData("name with spaces.png", @"C:\vault\name with spaces.png")]
    [InlineData("ünïcode.png", @"C:\vault\ünïcode.png")]
    public void Resolves_paths_within_root(string rel, string expected)
    {
        Assert.Equal(expected, VaultPaths.ResolveWithinRoot(Root, rel));
    }

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("..\\escape.png")]
    [InlineData("sub/../../escape.png")]
    [InlineData("..")]
    [InlineData("/etc/passwd")]                                  // rooted on Windows
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]       // absolute
    [InlineData(@"\\server\share\file")]                         // UNC
    public void Rejects_paths_escaping_root(string rel)
    {
        Assert.Null(VaultPaths.ResolveWithinRoot(Root, rel));
    }

    [Fact]
    public void Sibling_prefix_is_not_treated_as_within_root()
    {
        // C:\vault2 shares the textual prefix "C:\vault" but is not under it.
        Assert.Null(VaultPaths.ResolveWithinRoot(Root, "../vault2/secret.png"));
    }

    [Theory]
    [InlineData(null, "x")]
    [InlineData("", "x")]
    [InlineData(Root, null)]
    [InlineData(Root, "")]
    public void Rejects_empty_or_null_inputs(string? root, string? rel)
    {
        Assert.Null(VaultPaths.ResolveWithinRoot(root, rel));
    }

    [Fact]
    public void Trailing_separator_on_root_is_handled()
    {
        Assert.Equal(@"C:\vault\note.md", VaultPaths.ResolveWithinRoot(@"C:\vault\", "note.md"));
    }
}
