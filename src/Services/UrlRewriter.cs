using System.Text.RegularExpressions;

namespace MarkdownViewer.Services;

public static class UrlRewriter
{
    private static readonly Regex AttrRegex =
        new("(?<attr>\\s(?:src|href)=)(?<q>[\"'])(?<url>[^\"']*)\\k<q>", RegexOptions.Compiled);

    /// <summary>
    /// Markdig emits src="image.png" for relative images. We need them resolvable
    /// from app.local origin. Rewrite src= and href= attributes whose values
    /// don't have a scheme or leading slash. Skips data: and #anchors.
    /// </summary>
    public static string RewriteRelativeUrls(string html, string basePath)
    {
        return AttrRegex.Replace(html, m =>
        {
            var url = m.Groups["url"].Value;
            if (url.Length == 0) return m.Value;
            if (url.StartsWith("#") || url.StartsWith("data:") || url.Contains("://"))
                return m.Value;
            if (url.StartsWith("/")) return m.Value;
            if (url.StartsWith("mailto:") || url.StartsWith("tel:")) return m.Value;
            return $"{m.Groups["attr"].Value}\"{basePath}{url}\"";
        });
    }
}
