using System;
using System.Linq;

namespace MarkdownViewer.Services;

/// <summary>
/// The vault URL space: how vault files are addressed inside the WebView.
/// Vault files are served same-origin under
/// <c>https://app.local/__vault/&lt;tabId&gt;/&lt;rel&gt;</c> by MainWindow's
/// WebResourceRequested handler — being same-origin to the app.local document
/// is what makes &lt;img&gt;/iframe subresources load (a cross-origin
/// vault.local URL won't). The tab id scopes every URL to the tab (runtime)
/// that minted it, so a late subresource request from a hidden/background
/// document resolves against the vault that owns it by construction, and the
/// same relative path in two tabs can never collide. This class owns the pure
/// string logic for building and parsing those URLs; it never touches disk.
/// Disk resolution stays behind <see cref="VaultPaths.ResolveWithinRoot"/>,
/// the single path-traversal gate.
/// </summary>
public static class VaultUrlScheme
{
    /// <summary>Absolute URL prefix for vault-served files (tab id follows).</summary>
    public const string Origin = "https://app.local/__vault/";

    /// <summary>The path prefix the resource handler matches, relative to the
    /// app.local root (no leading slash).</summary>
    public const string RequestPrefix = "__vault/";

    /// <summary>
    /// Same-origin URL for a vault file owned by <paramref name="tabId"/>,
    /// given its vault-relative path in forward-slash form. Each segment is
    /// escaped so spaces etc. don't break the URL.
    /// </summary>
    public static string FileUrl(string tabId, string relForwardSlash) =>
        Origin + tabId + "/" +
        string.Join("/", relForwardSlash.Split('/').Select(Uri.EscapeDataString));

    /// <summary>
    /// Base URL for resolving relative resources/links in a rendered document:
    /// the file's directory (forward-slash, relative to the vault root) under
    /// the tab's /__vault/&lt;tabId&gt;/ space, or that space's root when the
    /// file sits at the top level. Segments are escaped (like
    /// <see cref="FileUrl"/>) so a directory with spaces or '#' doesn't break
    /// relative-URL resolution against this base.
    /// </summary>
    public static string DirBase(string tabId, string relDirForwardSlash) =>
        string.IsNullOrEmpty(relDirForwardSlash)
            ? Origin + tabId + "/"
            : Origin + tabId + "/" +
              string.Join("/", relDirForwardSlash.Split('/').Select(Uri.EscapeDataString)) + "/";

    /// <summary>
    /// Pull the owning tab id and vault-relative path out of an absolute
    /// app.local/__vault/&lt;tabId&gt;/&lt;rel&gt; URL, dropping any ?query /
    /// #fragment. A vault URL with no path after the tab id yields an empty
    /// rel (still true, so navigation handlers cancel it and do nothing,
    /// rather than letting a malformed vault URL replace the shell document).
    /// The tab id is not unescaped — generated ids are plain ASCII, and
    /// unescaping could smuggle a '/' into the segment.
    /// </summary>
    public static bool TryVaultRel(string url, out string tabId, out string rel)
    {
        tabId = "";
        rel = "";
        if (!url.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return false;
        var after = url.Substring(Origin.Length);
        var cut = after.IndexOfAny(new[] { '#', '?' });
        if (cut >= 0) after = after.Substring(0, cut);
        var slash = after.IndexOf('/');
        if (slash < 0)
        {
            tabId = after;
            return true;
        }
        tabId = after.Substring(0, slash);
        rel = Uri.UnescapeDataString(after.Substring(slash + 1));
        return true;
    }

    /// <summary>Split an href into its path part and (unprefixed) anchor.</summary>
    public static (string path, string anchor) SplitAnchor(string href)
    {
        var i = href.IndexOf('#');
        if (i < 0) return (href, "");
        return (href.Substring(0, i), href.Substring(i + 1));
    }

    /// <summary>
    /// Insert a &lt;base href&gt; tag into an HTML document (right after
    /// &lt;head&gt;, or in a synthesized head when there is none) so the
    /// document's relative URLs resolve against the vault URL space.
    /// </summary>
    public static string InjectBaseTag(string html, string baseHref)
    {
        var baseTag = $"<base href=\"{baseHref}\">";
        var headMatch = System.Text.RegularExpressions.Regex.Match(
            html, @"<head[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (headMatch.Success)
        {
            var insertAt = headMatch.Index + headMatch.Length;
            return html.Substring(0, insertAt) + "\n" + baseTag + html.Substring(insertAt);
        }
        // No <head>: stick a minimal one at the start.
        return $"<head>{baseTag}</head>" + html;
    }
}
