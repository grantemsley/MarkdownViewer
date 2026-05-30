using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MarkdownViewer.Services;

/// <summary>
/// Serves the app's bundled web assets (render.html, reader.css, bridge.js,
/// highlight.js, mermaid, the github-markdown CSS) from resources embedded in
/// the executable — so a published build is a single self-contained exe with no
/// <c>WebAssets\</c> folder beside it.
///
/// The asset files are embedded via <c>&lt;EmbeddedResource&gt;</c> with an
/// explicit <c>LogicalName</c> of <c>WebAssets/&lt;relative path&gt;</c> (see
/// the .csproj). MSBuild emits that name verbatim, using the OS path separator
/// for the recursive-dir portion, so a manifest name can look like
/// <c>WebAssets/lib\mermaid\mermaid.min.js</c>. We normalize separators to '/'
/// and index every resource under the prefix by its case-insensitive relative
/// path. The WebView2 <c>WebResourceRequested</c> handler and the HTML exporter
/// both resolve assets through here instead of reading from disk.
/// </summary>
public static class WebAssetProvider
{
    private static readonly Assembly Asm = typeof(WebAssetProvider).Assembly;
    private const string Prefix = "WebAssets/";

    // relative path (forward slash, case-insensitive) -> manifest resource name
    private static readonly Dictionary<string, string> Map = BuildMap();

    private static Dictionary<string, string> BuildMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Asm.GetManifestResourceNames())
        {
            var norm = name.Replace('\\', '/');
            var idx = norm.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var rel = norm.Substring(idx + Prefix.Length);
            if (rel.Length > 0) map[rel] = name;
        }
        return map;
    }

    /// <summary>
    /// Open an embedded asset by relative path (e.g. "lib/mermaid/mermaid.min.js"),
    /// or null if there's no such asset. The caller owns the returned stream;
    /// when handed to WebView2's CreateWebResourceResponse, WebView2 disposes it.
    /// </summary>
    public static Stream? Open(string relativePath)
    {
        var rel = Normalize(relativePath);
        return Map.TryGetValue(rel, out var name) ? Asm.GetManifestResourceStream(name) : null;
    }

    /// <summary>Read an embedded text asset as UTF-8, or null if absent.</summary>
    public static string? ReadText(string relativePath)
    {
        using var s = Open(relativePath);
        if (s is null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Normalize a request path to the map's key form: forward slashes, no leading slash.</summary>
    public static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    /// <summary>MIME type for a relative path, by extension. Text types carry charset=utf-8.</summary>
    public static string ContentType(string relativePath)
    {
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css"            => "text/css; charset=utf-8",
            ".js" or ".mjs"   => "text/javascript; charset=utf-8",
            ".json" or ".map" => "application/json; charset=utf-8",
            ".pdf"            => "application/pdf",
            ".svg"            => "image/svg+xml",
            ".png"            => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif"            => "image/gif",
            ".webp"           => "image/webp",
            ".bmp"            => "image/bmp",
            ".ico"            => "image/x-icon",
            ".woff"           => "font/woff",
            ".woff2"          => "font/woff2",
            ".ttf"            => "font/ttf",
            _                 => "application/octet-stream",
        };
    }
}
