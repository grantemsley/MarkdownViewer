using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class VaultUrlSchemeTests
{
    [Theory]
    [InlineData("note.md", "https://app.local/__vault/t1/note.md")]
    [InlineData("sub/note.md", "https://app.local/__vault/t1/sub/note.md")]
    [InlineData("my file.md", "https://app.local/__vault/t1/my%20file.md")]
    [InlineData("a#b.md", "https://app.local/__vault/t1/a%23b.md")]
    [InlineData("100%.md", "https://app.local/__vault/t1/100%25.md")]
    [InlineData("café.md", "https://app.local/__vault/t1/caf%C3%A9.md")]
    [InlineData("sub dir/pic #1.png", "https://app.local/__vault/t1/sub%20dir/pic%20%231.png")]
    public void FileUrl_EscapesSegments_KeepsSlashSeparators(string rel, string expected)
    {
        Assert.Equal(expected, VaultUrlScheme.FileUrl("t1", rel));
    }

    [Fact]
    public void DirBase_EmptyRelDir_ReturnsTabRoot()
    {
        Assert.Equal(VaultUrlScheme.Origin + "t1/", VaultUrlScheme.DirBase("t1", ""));
    }

    [Theory]
    [InlineData("sub", "https://app.local/__vault/t1/sub/")]
    [InlineData("sub/dir", "https://app.local/__vault/t1/sub/dir/")]
    [InlineData("my dir", "https://app.local/__vault/t1/my%20dir/")]
    [InlineData("a#b", "https://app.local/__vault/t1/a%23b/")]
    public void DirBase_NonEmptyInput_AppendsTrailingSlash_EscapesSegments(string relDir, string expected)
    {
        Assert.Equal(expected, VaultUrlScheme.DirBase("t1", relDir));
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
        var url = VaultUrlScheme.FileUrl("t1", rel);
        Assert.True(VaultUrlScheme.TryVaultRel(url, out var tabId, out var result));
        Assert.Equal("t1", tabId);
        Assert.Equal(rel, result);
    }

    [Theory]
    [InlineData("https://app.local/render.html")]
    [InlineData("https://example.com/")]
    [InlineData("")]
    public void TryVaultRel_NonVaultUrls_ReturnsFalse(string url)
    {
        Assert.False(VaultUrlScheme.TryVaultRel(url, out var tabId, out var rel));
        Assert.Equal("", tabId);
        Assert.Equal("", rel);
    }

    [Fact]
    public void TryVaultRel_StripsQueryString()
    {
        Assert.True(VaultUrlScheme.TryVaultRel("https://app.local/__vault/t1/note.md?x=1", out var tabId, out var rel));
        Assert.Equal("t1", tabId);
        Assert.Equal("note.md", rel);
    }

    [Fact]
    public void TryVaultRel_StripsFragment()
    {
        Assert.True(VaultUrlScheme.TryVaultRel("https://app.local/__vault/t1/note.md#heading", out var tabId, out var rel));
        Assert.Equal("t1", tabId);
        Assert.Equal("note.md", rel);
    }

    [Fact]
    public void TryVaultRel_StripsQueryThenFragment()
    {
        Assert.True(VaultUrlScheme.TryVaultRel("https://app.local/__vault/t1/note.md?x=1#heading", out var tabId, out var rel));
        Assert.Equal("t1", tabId);
        Assert.Equal("note.md", rel);
    }

    [Fact]
    public void TryVaultRel_StripsFragmentThenQuery()
    {
        Assert.True(VaultUrlScheme.TryVaultRel("https://app.local/__vault/t1/note.md#heading?x=1", out var tabId, out var rel));
        Assert.Equal("t1", tabId);
        Assert.Equal("note.md", rel);
    }

    [Theory]
    [InlineData("HTTPS://APP.LOCAL/__VAULT/t1/note.md")]
    [InlineData("Https://App.Local/__vault/t1/note.md")]
    public void TryVaultRel_CaseInsensitiveOrigin(string url)
    {
        Assert.True(VaultUrlScheme.TryVaultRel(url, out var tabId, out var rel));
        Assert.Equal("t1", tabId);
        Assert.Equal("note.md", rel);
    }

    [Fact]
    public void TabScope_SameRelInTwoTabs_DistinctUrlsAndRoundTrip()
    {
        // Same vault-relative path minted from two different tabs must not
        // collide - each URL carries its own tab id, and each round-trips
        // back to its own (tabId, rel) pair.
        var url1 = VaultUrlScheme.FileUrl("t1", "note.md");
        var url2 = VaultUrlScheme.FileUrl("t2", "note.md");

        Assert.Equal("https://app.local/__vault/t1/note.md", url1);
        Assert.Equal("https://app.local/__vault/t2/note.md", url2);
        Assert.NotEqual(url1, url2);

        Assert.True(VaultUrlScheme.TryVaultRel(url1, out var tabId1, out var rel1));
        Assert.Equal("t1", tabId1);
        Assert.Equal("note.md", rel1);

        Assert.True(VaultUrlScheme.TryVaultRel(url2, out var tabId2, out var rel2));
        Assert.Equal("t2", tabId2);
        Assert.Equal("note.md", rel2);
    }

    [Fact]
    public void TryVaultRel_NoSlashAfterTabId_ReturnsTrueWithEmptyRel()
    {
        // No '/' after the tab id segment: the whole remainder is the tab id
        // and rel is empty. Still returns true so navigation handlers cancel
        // this instead of letting it replace the shell document.
        Assert.True(VaultUrlScheme.TryVaultRel("https://app.local/__vault/t1", out var tabId, out var rel));
        Assert.Equal("t1", tabId);
        Assert.Equal("", rel);
    }

    [Fact]
    public void TryVaultRel_DoesNotUnescapeTabIdSegment()
    {
        // The tab id is never run through UnescapeDataString - generated ids
        // are plain ASCII, and unescaping could smuggle a '/' into it.
        Assert.True(VaultUrlScheme.TryVaultRel("https://app.local/__vault/t%2f1/x.md", out var tabId, out var rel));
        Assert.Equal("t%2f1", tabId);
        Assert.Equal("x.md", rel);
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
        var result = VaultUrlScheme.InjectBaseTag(html, "https://app.local/__vault/t1/sub/");
        Assert.Equal(
            "<html><head>\n<base href=\"https://app.local/__vault/t1/sub/\"></head><body></body></html>",
            result);
    }

    [Fact]
    public void InjectBaseTag_InsertsAfterHeadWithAttribute_CaseInsensitive()
    {
        var html = "<HTML><HEAD lang=\"en\">content</HEAD></HTML>";
        var result = VaultUrlScheme.InjectBaseTag(html, "https://app.local/__vault/t1/");
        Assert.Equal(
            "<HTML><HEAD lang=\"en\">\n<base href=\"https://app.local/__vault/t1/\">content</HEAD></HTML>",
            result);
    }

    [Fact]
    public void InjectBaseTag_SynthesizesHeadWhenAbsent()
    {
        var html = "<div>no head here</div>";
        var result = VaultUrlScheme.InjectBaseTag(html, "https://app.local/__vault/t1/");
        Assert.Equal(
            "<head><base href=\"https://app.local/__vault/t1/\"></head><div>no head here</div>",
            result);
    }
}
