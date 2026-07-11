using System;

namespace MarkdownViewer.Services;

public enum LinkAction
{
    Allow,
    Cancel,
    OpenExternal,
    OpenInVault,
}

/// <summary>For OpenInVault routes, <paramref name="TabId"/> names the tab
/// whose URL space the link belongs to; consumers drop routes whose tab is
/// not the active one (a vault URL is only meaningful to the tab that minted
/// it).</summary>
public readonly record struct LinkRoute(LinkAction Action, string VaultRel, string TabId = "");

/// <summary>
/// Pure decision layer for WebView2 navigation events. Given the raw event
/// data, decides what MainWindow should do; it never touches the WebView,
/// the vault, or the filesystem itself.
/// </summary>
public static class LinkRouter
{
    /// <summary>
    /// Routes a navigation on the main (top-level) WebView document.
    /// </summary>
    public static LinkRoute RouteTopLevel(string? uri)
    {
        var u = uri ?? "";
        // A vault-file link that slipped past the JS click interceptor is an
        // app.local URL, so it would be waved through below and replace the
        // shell document. Catch it first and route it through the app instead.
        if (VaultUrlScheme.TryVaultRel(u, out var vaultTab, out var vaultRel))
            return new LinkRoute(LinkAction.OpenInVault, vaultRel, vaultTab);
        // Allow our own navigations.
        if (u.StartsWith("https://app.local/")) return new LinkRoute(LinkAction.Allow, "");
        if (u.StartsWith("http://") || u.StartsWith("https://"))
            return new LinkRoute(LinkAction.OpenExternal, "");
        if (u.StartsWith("about:")) return new LinkRoute(LinkAction.Allow, ""); // initial blank etc.
        return new LinkRoute(LinkAction.Allow, "");
    }

    /// <summary>
    /// Routes a navigation on a child &lt;iframe&gt; frame inside a raw HTML
    /// document.
    /// </summary>
    public static LinkRoute RouteFrame(string? uri, string? currentIframeUrl, bool isUserInitiated)
    {
        var u = uri ?? "";
        if (string.IsNullOrEmpty(u) || u.StartsWith("about:") || u.StartsWith("blob:"))
            return new LinkRoute(LinkAction.Allow, "");

        // Our own intentional navigation (NavigateRaw sets _currentIframeUrl
        // right before posting setDoc). Same URL or same URL + #anchor stays.
        if (!string.IsNullOrEmpty(currentIframeUrl))
        {
            if (u == currentIframeUrl) return new LinkRoute(LinkAction.Allow, "");
            if (u.StartsWith(currentIframeUrl + "#")) return new LinkRoute(LinkAction.Allow, ""); // anchor scroll
        }

        // Anything past the intentional-navigation checks that the user did NOT
        // initiate is an <iframe> auto-loading from rendered (untrusted) markdown
        // or transcript content. Block it outright and never hand it to the OS
        // browser - otherwise <iframe src="https://evil"> in a .md is a zero-click
        // browser launch. Only a genuine click (a link inside a raw HTML doc)
        // reaches the routing below.
        if (!isUserInitiated)
            return new LinkRoute(LinkAction.Cancel, "");

        // In-vault link click inside a raw doc: open the target via the app shell.
        if (VaultUrlScheme.TryVaultRel(u, out var tab, out var rel))
            return new LinkRoute(LinkAction.OpenInVault, rel, tab);

        // External link: send to OS browser.
        if (u.StartsWith("http://") || u.StartsWith("https://"))
            return new LinkRoute(LinkAction.OpenExternal, "");

        return new LinkRoute(LinkAction.Allow, "");
    }

    /// <summary>
    /// Resolves a clicked-link href from a rendered document into a
    /// vault-relative path and anchor. Two ways to land in-vault: an
    /// absolute app.local/__vault URL, or a path relative to the document's
    /// own /__vault/ base.
    /// </summary>
    public static bool TryResolveVaultHref(string? href, string? basePath,
        out string tabId, out string rel, out string anchor)
    {
        tabId = "";
        rel = "";
        anchor = "";
        if (string.IsNullOrEmpty(href)) return false;

        // Strip query/fragment for path resolution.
        var (pathPart, anchorPart) = VaultUrlScheme.SplitAnchor(href);

        // 1. Absolute same-origin vault URL (app.local/__vault/<tabId>/<rel>).
        if (VaultUrlScheme.TryVaultRel(pathPart, out var tab1, out var rel1))
        {
            tabId = tab1;
            rel = rel1;
            anchor = anchorPart;
            return true;
        }
        // 2. Resolve as relative to the current document's tab-scoped
        // /__vault/<tabId>/ base — the resolved URL inherits the base's tab id.
        try
        {
            var baseUri = new Uri(!string.IsNullOrEmpty(basePath) ? basePath : VaultUrlScheme.Origin);
            var u = new Uri(baseUri, pathPart);
            if (VaultUrlScheme.TryVaultRel(u.AbsoluteUri, out var tab2, out var rel2))
            {
                tabId = tab2;
                rel = rel2;
                anchor = anchorPart;
                return true;
            }
        }
        catch { }
        return false;
    }
}
