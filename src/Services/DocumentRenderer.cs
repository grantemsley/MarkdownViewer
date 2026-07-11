using System;
using System.Collections.Generic;
using System.IO;

namespace MarkdownViewer.Services;

/// <summary>
/// One rendered document, ready to post to the WebView: final HTML (relative
/// URLs already rewritten where applicable), the headings for the outline, and
/// the /__vault/ base the HTML resolves relative resources against.
/// </summary>
public sealed record RenderedDoc(string Html, IReadOnlyList<HeadingEntry> Headings, string BasePath);

/// <summary>
/// The read -&gt; render -&gt; URL-rewrite pipeline shared by the markdown and
/// transcript viewers (and the cold-start prerender). Pure with respect to the
/// UI: no WPF/WebView2 types, so it is unit-testable and safe to run on a
/// worker thread.
/// </summary>
public static class DocumentRenderer
{
    /// <summary>
    /// Vault-relative directory of <paramref name="filePath"/> in forward-slash
    /// form: "" when the file sits at the vault root or outside the vault.
    /// The prefix match is ordinal-ignore-case against the normalized root.
    /// </summary>
    public static string VaultRelDir(string? vaultRoot, string filePath)
    {
        if (string.IsNullOrEmpty(vaultRoot)) return "";
        string root;
        try { root = Path.GetFullPath(vaultRoot).TrimEnd(Path.DirectorySeparatorChar); }
        catch { return ""; }
        if (!filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "";
        return Path.GetDirectoryName(filePath.Substring(root.Length).TrimStart('\\', '/'))
            ?.Replace('\\', '/') ?? "";
    }

    /// <summary>
    /// Render a markdown file: read (encoding-detected, size-capped), Markdig
    /// render, then rewrite relative img/href URLs so they resolve same-origin
    /// under the file's directory in the /__vault/ URL space.
    /// </summary>
    public static RenderedDoc RenderMarkdownFile(string filePath, string? vaultRoot,
        string tabId, bool showLineNumbers, bool highlightCustomTags)
    {
        var source = ContentRouter.ReadTextFile(filePath);
        var result = MarkdownService.Render(source, showLineNumbers, highlightCustomTags);
        var basePath = VaultUrlScheme.DirBase(tabId, VaultRelDir(vaultRoot, filePath));
        var html = UrlRewriter.RewriteRelativeUrls(result.Html, basePath);
        return new RenderedDoc(html, result.Headings, basePath);
    }

    /// <summary>
    /// Render a .jsonl transcript: convert to markdown, render without line
    /// numbers (they would just add noise on generated markdown). Transcript
    /// output carries no relative resources, so no URL rewrite; the base is the
    /// vault origin itself.
    /// </summary>
    public static RenderedDoc RenderTranscriptFile(string filePath, string tabId,
        IDictionary<string, bool>? visibleCategories, bool highlightCustomTags)
    {
        var jsonl = ContentRouter.ReadTextFile(filePath);
        var markdown = TranscriptService.ToMarkdown(jsonl, visibleCategories);
        var result = MarkdownService.Render(markdown, showLineNumbers: false,
            highlightCustomTags: highlightCustomTags);
        return new RenderedDoc(result.Html, result.Headings, VaultUrlScheme.DirBase(tabId, ""));
    }
}
