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

    [Theory]
    [InlineData(@"C:\vault\sub\a.md", @"C:\vault\sub\a.md")]
    [InlineData(@"c:\VAULT\a.md", @"C:\vault\a.md")]
    [InlineData("C:/vault/sub/a.md", @"C:\vault\sub\a.md")]
    public void Absolute_paths_inside_root_resolve(string abs, string expected)
    {
        Assert.Equal(expected, VaultPaths.AbsoluteWithinRoot(Root, abs), ignoreCase: true);
    }

    [Theory]
    [InlineData(@"C:\vault2\a.md")]
    [InlineData(@"C:\other\a.md")]
    [InlineData(@"D:\vault\a.md")]
    [InlineData(@"\\server\share\a.md")]
    [InlineData("sub/a.md")]                                     // relative: not a transcript path
    [InlineData(@"C:\vault\..\escape.md")]
    public void Absolute_paths_outside_root_are_refused(string abs)
    {
        Assert.Null(VaultPaths.AbsoluteWithinRoot(Root, abs));
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
