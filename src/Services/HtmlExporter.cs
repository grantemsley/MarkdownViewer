using System;
using System.Collections.Generic;
using System.IO;

namespace MarkdownViewer.Services;

/// <summary>
/// Builds the standalone HTML document used by "Export rendered HTML" and
/// "Open rendered in default browser". Pure string-building over the same
/// render pipeline as the in-app viewer; no UI types, unit-testable.
/// </summary>
public static class HtmlExporter
{
    /// <summary>
    /// Render <paramref name="filePath"/> (markdown or .jsonl transcript) into a
    /// self-contained HTML document, or null when the file kind isn't renderable
    /// or the read fails.
    /// </summary>
    public static string? BuildStandaloneHtml(string filePath,
        IDictionary<string, bool>? transcriptVisibleCategories, bool highlightCustomTags)
    {
        var kind = ContentRouter.Route(filePath, out _);
        string markdown;
        try
        {
            if (kind == ViewerKind.Markdown)
            {
                markdown = ContentRouter.ReadTextFile(filePath);
            }
            else if (kind == ViewerKind.JsonlTranscript)
            {
                var jsonl = ContentRouter.ReadTextFile(filePath);
                markdown = TranscriptService.ToMarkdown(jsonl, transcriptVisibleCategories);
            }
            else return null;
        }
        catch { return null; }

        var rendered = MarkdownService.Render(markdown, showLineNumbers: false,
            highlightCustomTags: highlightCustomTags);
        var inner = rendered.Html;
        var title = Path.GetFileName(filePath);

        var readerCss = TryReadAsset("reader.css");
        var hlCss = TryReadAsset("lib/highlight/styles/github.min.css");
        // The exported file is a fully-parsed document opened at a file:// origin
        // (unlike the in-app viewer, which injects content via innerHTML where
        // <script> never runs), so untrusted <script>/inline-handlers in the
        // rendered markdown WOULD execute. A CSP without 'unsafe-inline' in
        // script-src blocks them; our own init script carries this nonce, and the
        // highlight.js/mermaid CDN scripts are allowed by origin. 'unsafe-eval'
        // stays for mermaid.
        var nonce = Guid.NewGuid().ToString("N");
        // We deliberately link highlight.js / mermaid from a CDN rather than
        // inlining: highlight is ~125 KB and mermaid is 3.3 MB, which would
        // bloat every exported file. CDN-loaded copies are cached after the
        // first open and degrade gracefully when offline (code blocks just
        // stay unhighlighted, diagrams stay as their source text).
        return $@"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<meta http-equiv=""Content-Security-Policy"" content=""default-src 'none'; script-src https://cdnjs.cloudflare.com 'nonce-{nonce}' 'unsafe-eval'; style-src 'unsafe-inline'; img-src data: blob: https: http:; font-src data: https: http:; connect-src 'none'; object-src 'none'; base-uri 'none'"">
<title>{System.Net.WebUtility.HtmlEncode(title)}</title>
<style>
{readerCss}
{hlCss}
/* reader.css locks html/body to overflow:hidden because in-app a separate
   #scroll container does the scrolling. The standalone document scrolls the
   page itself, so restore normal document scrolling here. */
html, body {{ overflow: auto; height: auto; }}
body {{ margin: 0; background: var(--bg); color: var(--fg); font-family: var(--font); font-size: var(--base-size); }}
.page {{ max-width: 880px; margin: 0 auto; padding: 28px 24px 80px; }}
</style>
<script src=""https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/highlight.min.js""></script>
<script src=""https://cdnjs.cloudflare.com/ajax/libs/mermaid/10.9.1/mermaid.min.js""></script>
</head>
<body class=""theme-light kind-markdown"">
<div class=""page"" id=""page"">
{inner}
</div>
<script nonce=""{nonce}"">
  if (window.hljs) document.querySelectorAll('pre code').forEach(function(b) {{
    if (!b.closest('.mermaid')) try {{ window.hljs.highlightElement(b); }} catch (e) {{}}
  }});
  if (window.mermaid) try {{
    window.mermaid.initialize({{ startOnLoad: false, securityLevel: 'strict' }});
    window.mermaid.run({{ nodes: document.querySelectorAll('.mermaid') }});
  }} catch (e) {{}}
</script>
</body>
</html>";
    }

    private static string TryReadAsset(string relativePath)
        => WebAssetProvider.ReadText(relativePath) ?? "";
}
