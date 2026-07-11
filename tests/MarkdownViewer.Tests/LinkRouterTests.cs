using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class LinkRouterTests
{
    // ─── RouteTopLevel ────────────────────────────────────────────────

    [Fact]
    public void RouteTopLevel_VaultUrl_OpensInVault()
    {
        var route = LinkRouter.RouteTopLevel("https://app.local/__vault/note.md");
        Assert.Equal(LinkAction.OpenInVault, route.Action);
        Assert.Equal("note.md", route.VaultRel);
    }

    [Fact]
    public void RouteTopLevel_VaultUrlWithFragment_FragmentStripped_OpensInVault()
    {
        var route = LinkRouter.RouteTopLevel("https://app.local/__vault/note.md#heading");
        Assert.Equal(LinkAction.OpenInVault, route.Action);
        Assert.Equal("note.md", route.VaultRel);
    }

    [Fact]
    public void RouteTopLevel_OwnAppLocalNavigation_Allow()
    {
        var route = LinkRouter.RouteTopLevel("https://app.local/render.html");
        Assert.Equal(LinkAction.Allow, route.Action);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    public void RouteTopLevel_ExternalHttpOrHttps_OpenExternal(string uri)
    {
        var route = LinkRouter.RouteTopLevel(uri);
        Assert.Equal(LinkAction.OpenExternal, route.Action);
    }

    [Fact]
    public void RouteTopLevel_AboutBlank_Allow()
    {
        var route = LinkRouter.RouteTopLevel("about:blank");
        Assert.Equal(LinkAction.Allow, route.Action);
    }

    [Fact]
    public void RouteTopLevel_Mailto_Allow_Quirk()
    {
        // Neither handler special-cases mailto: it isn't a vault URL, doesn't
        // start with app.local, and doesn't start with http(s), so it falls
        // through to Allow (the WebView itself hands it to the OS). Preserved
        // as-is from the original MainWindow logic.
        var route = LinkRouter.RouteTopLevel("mailto:test@example.com");
        Assert.Equal(LinkAction.Allow, route.Action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData((string?)null)]
    public void RouteTopLevel_EmptyOrGarbageOrNull_Allow(string? uri)
    {
        var route = LinkRouter.RouteTopLevel(uri);
        Assert.Equal(LinkAction.Allow, route.Action);
    }

    // ─── RouteFrame ───────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("about:blank")]
    [InlineData("blob:https://app.local/1234")]
    public void RouteFrame_EmptyOrAboutOrBlob_Allow(string uri)
    {
        var route = LinkRouter.RouteFrame(uri, currentIframeUrl: null, isUserInitiated: false);
        Assert.Equal(LinkAction.Allow, route.Action);
    }

    [Fact]
    public void RouteFrame_CurrentIframeUrlNull_AboutBlank_Allow()
    {
        var route = LinkRouter.RouteFrame("about:blank", currentIframeUrl: null, isUserInitiated: false);
        Assert.Equal(LinkAction.Allow, route.Action);
    }

    [Fact]
    public void RouteFrame_ExactCurrentIframeUrlMatch_Allow()
    {
        var route = LinkRouter.RouteFrame(
            "https://app.local/__vault/doc.html",
            currentIframeUrl: "https://app.local/__vault/doc.html",
            isUserInitiated: false);
        Assert.Equal(LinkAction.Allow, route.Action);
    }

    [Fact]
    public void RouteFrame_CurrentIframeUrlAnchorNavigation_Allow()
    {
        // Same-document anchor scroll is allowed even though it's not
        // user-initiated - the intentional-navigation check runs first.
        var route = LinkRouter.RouteFrame(
            "https://app.local/__vault/doc.html#section",
            currentIframeUrl: "https://app.local/__vault/doc.html",
            isUserInitiated: false);
        Assert.Equal(LinkAction.Allow, route.Action);
    }

    [Fact]
    public void RouteFrame_NonUserInitiated_ExternalIframe_Cancel()
    {
        // The security-critical zero-click block: an <iframe src="https://evil">
        // auto-loading from rendered untrusted markdown must be Cancel, never
        // OpenExternal.
        var route = LinkRouter.RouteFrame(
            "https://evil.example.com",
            currentIframeUrl: null,
            isUserInitiated: false);
        Assert.Equal(LinkAction.Cancel, route.Action);
    }

    [Fact]
    public void RouteFrame_UserInitiated_ExternalIframe_OpenExternal()
    {
        var route = LinkRouter.RouteFrame(
            "https://example.com",
            currentIframeUrl: null,
            isUserInitiated: true);
        Assert.Equal(LinkAction.OpenExternal, route.Action);
    }

    [Fact]
    public void RouteFrame_UserInitiated_VaultLink_OpenInVault()
    {
        var route = LinkRouter.RouteFrame(
            "https://app.local/__vault/note.md",
            currentIframeUrl: null,
            isUserInitiated: true);
        Assert.Equal(LinkAction.OpenInVault, route.Action);
        Assert.Equal("note.md", route.VaultRel);
    }

    [Fact]
    public void RouteFrame_UserInitiated_Mailto_Allow_Quirk()
    {
        var route = LinkRouter.RouteFrame(
            "mailto:test@example.com",
            currentIframeUrl: null,
            isUserInitiated: true);
        Assert.Equal(LinkAction.Allow, route.Action);
    }

    [Fact]
    public void RouteFrame_UserInitiated_GarbageScheme_Allow()
    {
        var route = LinkRouter.RouteFrame(
            "ftp://example.com/file",
            currentIframeUrl: null,
            isUserInitiated: true);
        Assert.Equal(LinkAction.Allow, route.Action);
    }

    // ─── TryResolveVaultHref ──────────────────────────────────────────

    [Fact]
    public void TryResolveVaultHref_AbsoluteVaultUrl_Resolves()
    {
        var ok = LinkRouter.TryResolveVaultHref(
            "https://app.local/__vault/other.md", basePath: null, out var rel, out var anchor);
        Assert.True(ok);
        Assert.Equal("other.md", rel);
        Assert.Equal("", anchor);
    }

    [Fact]
    public void TryResolveVaultHref_RelativeAgainstVaultBase_Resolves()
    {
        var basePath = VaultUrlScheme.DirBase("sub");
        var ok = LinkRouter.TryResolveVaultHref("note.md", basePath, out var rel, out var anchor);
        Assert.True(ok);
        Assert.Equal("sub/note.md", rel);
        Assert.Equal("", anchor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryResolveVaultHref_RelativeAgainstEmptyBase_ResolvesAgainstOrigin(string? basePath)
    {
        var ok = LinkRouter.TryResolveVaultHref("note.md", basePath, out var rel, out var anchor);
        Assert.True(ok);
        Assert.Equal("note.md", rel);
        Assert.Equal("", anchor);
    }

    [Fact]
    public void TryResolveVaultHref_TraversalEscapesOrigin_ReturnsFalse()
    {
        var basePath = VaultUrlScheme.DirBase("sub");
        // Two levels up from /__vault/sub/ lands outside /__vault/ entirely.
        var ok = LinkRouter.TryResolveVaultHref("../../evil.md", basePath, out var rel, out var anchor);
        Assert.False(ok);
        Assert.Equal("", rel);
        Assert.Equal("", anchor);
    }

    [Fact]
    public void TryResolveVaultHref_AnchorRoundTrips()
    {
        var ok = LinkRouter.TryResolveVaultHref(
            "https://app.local/__vault/note.md#heading", basePath: null, out var rel, out var anchor);
        Assert.True(ok);
        Assert.Equal("note.md", rel);
        Assert.Equal("heading", anchor);
    }

    [Fact]
    public void TryResolveVaultHref_RelativeWithAnchor_ResolvesBothPathAndAnchor()
    {
        var basePath = VaultUrlScheme.DirBase("sub");
        var ok = LinkRouter.TryResolveVaultHref("note.md#heading", basePath, out var rel, out var anchor);
        Assert.True(ok);
        Assert.Equal("sub/note.md", rel);
        Assert.Equal("heading", anchor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryResolveVaultHref_NullOrEmptyHref_ReturnsFalse(string? href)
    {
        var ok = LinkRouter.TryResolveVaultHref(href, basePath: null, out var rel, out var anchor);
        Assert.False(ok);
        Assert.Equal("", rel);
        Assert.Equal("", anchor);
    }
}
