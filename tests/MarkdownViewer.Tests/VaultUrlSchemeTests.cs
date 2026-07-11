using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class VaultUrlSchemeTests
{
    [Theory]
    [InlineData("note.md", "https://app.local/__vault/note.md")]
    [InlineData("sub/note.md", "https://app.local/__vault/sub/note.md")]
    [InlineData("my file.md", "https://app.local/__vault/my%20file.md")]
    [InlineData("a#b.md", "https://app.local/__vault/a%23b.md")]
    [InlineData("100%.md", "https://app.local/__vault/100%25.md")]
    [InlineData("café.md", "https://app.local/__vault/caf%C3%A9.md")]
    [InlineData("sub dir/pic #1.png", "https://app.local/__vault/sub%20dir/pic%20%231.png")]
    public void FileUrl_EscapesSegments_KeepsSlashSeparators(string rel, string expected)
    {
        Assert.Equal(expected, VaultUrlScheme.FileUrl(rel));
    }

    [Fact]
    public void DirBase_EmptyInput_ReturnsOrigin()
    {
        Assert.Equal(VaultUrlScheme.Origin, VaultUrlScheme.DirBase(""));
    }

    [Theory]
    [InlineData("sub", "https://app.local/__vault/sub/")]
    [InlineData("sub/dir", "https://app.local/__vault/sub/dir/")]
    [InlineData("my dir", "https://app.local/__vault/my%20dir/")]
    [InlineData("a#b", "https://app.local/__vault/a%23b/")]
    public void DirBase_NonEmptyInput_AppendsTrailingSlash_EscapesSegments(string relDir, string expected)
    {
        Assert.Equal(expected, VaultUrlScheme.DirBase(relDir));
    }

    [Theory]
    [InlineData("note.md")]
    [InlineData("sub/note.md")]
    [InlineData("my file.md")]
    [InlineData("a#b.md")]
    [InlineData("100%.md")]
    [InlineData("café.md")]
    [InlineData("sub dir/pic #1.png")]
    public void TryVaultRel_RoundTrips_FileUrl(string rel)
    {
        var url = VaultUrlScheme.FileUrl(rel);
        Assert.True(VaultUrlScheme.TryVaultRel(url, out var result));
        Assert.Equal(rel, result);
    }

    [Theory]
    [InlineData("https://app.local/render.html")]
    [InlineData("https://example.com/")]
    [InlineData("")]
    public void TryVaultRel_NonVaultUrls_ReturnsFalse(string url)
    {
        Assert.False(VaultUrlScheme.TryVaultRel(url, out var rel));
        Assert.Equal("", rel);
    }

    [Fact]
    public void TryVaultRel_StripsQueryString()
    {
        Assert.True(VaultUrlScheme.TryVaultRel("https://app.local/__vault/note.md?x=1", out var rel));
        Assert.Equal("note.md", rel);
    }

    [Fact]
    public void TryVaultRel_StripsFragment()
    {
        Assert.True(VaultUrlScheme.TryVaultRel("https://app.local/__vault/note.md#heading", out var rel));
        Assert.Equal("note.md", rel);
    }

    [Fact]
    public void TryVaultRel_StripsQueryThenFragment()
    {
        Assert.True(VaultUrlScheme.TryVaultRel("https://app.local/__vault/note.md?x=1#heading", out var rel));
        Assert.Equal("note.md", rel);
    }

    [Fact]
    public void TryVaultRel_StripsFragmentThenQuery()
    {
        Assert.True(VaultUrlScheme.TryVaultRel("https://app.local/__vault/note.md#heading?x=1", out var rel));
        Assert.Equal("note.md", rel);
    }

    [Theory]
    [InlineData("HTTPS://APP.LOCAL/__VAULT/note.md")]
    [InlineData("Https://App.Local/__vault/note.md")]
    public void TryVaultRel_CaseInsensitiveOrigin(string url)
    {
        Assert.True(VaultUrlScheme.TryVaultRel(url, out var rel));
        Assert.Equal("note.md", rel);
    }

    [Fact]
    public void SplitAnchor_NoHash_ReturnsWholeAsPath()
    {
        var (path, anchor) = VaultUrlScheme.SplitAnchor("note.md");
        Assert.Equal("note.md", path);
        Assert.Equal("", anchor);
    }

    [Fact]
    public void SplitAnchor_WithHash_SplitsPathAndAnchor()
    {
        var (path, anchor) = VaultUrlScheme.SplitAnchor("note.md#heading");
        Assert.Equal("note.md", path);
        Assert.Equal("heading", anchor);
    }

    [Fact]
    public void SplitAnchor_HashAtStart_EmptyPath()
    {
        var (path, anchor) = VaultUrlScheme.SplitAnchor("#heading");
        Assert.Equal("", path);
        Assert.Equal("heading", anchor);
    }

    [Fact]
    public void SplitAnchor_MultipleHashes_SplitsAtFirst()
    {
        var (path, anchor) = VaultUrlScheme.SplitAnchor("note.md#a#b");
        Assert.Equal("note.md", path);
        Assert.Equal("a#b", anchor);
    }

    [Fact]
    public void InjectBaseTag_InsertsAfterHead()
    {
        var html = "<html><head></head><body></body></html>";
        var result = VaultUrlScheme.InjectBaseTag(html, "https://app.local/__vault/sub/");
        Assert.Equal(
            "<html><head>\n<base href=\"https://app.local/__vault/sub/\"></head><body></body></html>",
            result);
    }

    [Fact]
    public void InjectBaseTag_InsertsAfterHeadWithAttribute_CaseInsensitive()
    {
        var html = "<HTML><HEAD lang=\"en\">content</HEAD></HTML>";
        var result = VaultUrlScheme.InjectBaseTag(html, "https://app.local/__vault/");
        Assert.Equal(
            "<HTML><HEAD lang=\"en\">\n<base href=\"https://app.local/__vault/\">content</HEAD></HTML>",
            result);
    }

    [Fact]
    public void InjectBaseTag_SynthesizesHeadWhenAbsent()
    {
        var html = "<div>no head here</div>";
        var result = VaultUrlScheme.InjectBaseTag(html, "https://app.local/__vault/");
        Assert.Equal(
            "<head><base href=\"https://app.local/__vault/\"></head><div>no head here</div>",
            result);
    }
}
