using System;
using System.Collections.Generic;
using System.IO;

namespace MarkdownViewer.Services;

public enum ViewerKind { Markdown, RawBrowser, Image, Text, Binary, JsonlTranscript, None }

public static class ContentRouter
{
    private static readonly HashSet<string> MarkdownExts = new(StringComparer.OrdinalIgnoreCase)
        { ".md", ".markdown", ".mdown", ".mkd" };

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico" };

    // Extensions we'll render with highlight.js. Anything else falls through to
    // plain text (no highlight).
    private static readonly Dictionary<string, string> HighlightLang = new(StringComparer.OrdinalIgnoreCase)
    {
        [".ps1"] = "powershell", [".psm1"] = "powershell", [".psd1"] = "powershell",
        [".sh"] = "bash", [".bash"] = "bash", [".zsh"] = "bash",
        [".py"] = "python", [".rb"] = "ruby",
        [".js"] = "javascript", [".mjs"] = "javascript", [".cjs"] = "javascript",
        [".jsx"] = "javascript", [".ts"] = "typescript", [".tsx"] = "typescript",
        [".json"] = "json", [".yaml"] = "yaml", [".yml"] = "yaml",
        [".toml"] = "ini", [".ini"] = "ini", [".cfg"] = "ini", [".conf"] = "ini",
        [".xml"] = "xml", [".xaml"] = "xml", [".html"] = "xml", [".htm"] = "xml",
        [".cs"] = "csharp", [".csproj"] = "xml",
        [".cpp"] = "cpp", [".c"] = "c", [".h"] = "cpp", [".hpp"] = "cpp",
        [".go"] = "go", [".rs"] = "rust",
        [".java"] = "java", [".kt"] = "kotlin", [".swift"] = "swift",
        [".sql"] = "sql",
        [".css"] = "css", [".scss"] = "scss", [".less"] = "less",
        [".bat"] = "dos", [".cmd"] = "dos",
        [".lua"] = "lua", [".pl"] = "perl",
        [".dockerfile"] = "dockerfile",
        [".gradle"] = "groovy",
    };

    private static readonly HashSet<string> PlainTextExts = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".log", ".csv", ".tsv", ".md5", ".sha1", ".sha256" };

    public static ViewerKind Route(string filePath, out string highlightLang)
    {
        highlightLang = "";
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return ViewerKind.None;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (MarkdownExts.Contains(ext)) return ViewerKind.Markdown;
        if (ImageExts.Contains(ext)) return ViewerKind.Image;
        if (ext == ".pdf") return ViewerKind.RawBrowser;
        if (ext == ".html" || ext == ".htm" || ext == ".xhtml") return ViewerKind.RawBrowser;
        if (ext == ".jsonl") return ViewerKind.JsonlTranscript;

        if (HighlightLang.TryGetValue(ext, out var lang))
        {
            highlightLang = lang;
            return ViewerKind.Text;
        }
        if (PlainTextExts.Contains(ext))
            return ViewerKind.Text;

        // Unknown extension: peek at the file. If it looks binary, treat as binary.
        if (LooksBinary(filePath))
            return ViewerKind.Binary;
        return ViewerKind.Text;
    }

    private static bool LooksBinary(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            Span<byte> buf = stackalloc byte[8192];
            int n = fs.Read(buf);
            for (int i = 0; i < n; i++)
                if (buf[i] == 0) return true;
            return false;
        }
        catch
        {
            return true;
        }
    }

    // Cap on how much of a file we pull into memory to render. A viewer has no
    // business loading a multi-GB .log/.jsonl: the read + decode + Markdig render
    // + JSON re-serialize all happen on the UI thread and would freeze (or OOM)
    // the window. Beyond this we read only the head and mark it truncated.
    private const long MaxTextBytes = 50L * 1024 * 1024; // 50 MB

    /// <summary>
    /// Read a text file using a sensible Windows encoding detection chain:
    /// honor BOM if present, else UTF-8 strict, fall back to Windows-1252.
    /// Files larger than <see cref="MaxTextBytes"/> are read only up to that cap
    /// and get a trailing truncation notice, so a pathologically large file can't
    /// freeze or OOM the UI thread.
    /// </summary>
    public static string ReadTextFile(string filePath)
    {
        bool truncated = false;
        byte[] bytes;
        var length = new FileInfo(filePath).Length;
        if (length > MaxTextBytes)
        {
            bytes = new byte[MaxTextBytes];
            using (var fs = File.OpenRead(filePath))
            {
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = fs.Read(bytes, read, bytes.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read < bytes.Length) System.Array.Resize(ref bytes, read);
            }
            truncated = true;
        }
        else
        {
            bytes = File.ReadAllBytes(filePath);
        }

        var text = DecodeBytes(bytes);
        if (truncated)
            text += $"\n\n... [truncated by MarkdownViewer at {MaxTextBytes / (1024 * 1024)} MB; file is "
                  + $"{length / (1024 * 1024)} MB] ...\n";
        return text;
    }

    private static string DecodeBytes(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        // UTF-32 LE BOM (FF FE 00 00) must be tested before UTF-16 LE (FF FE),
        // since the UTF-16 prefix would otherwise match a UTF-32 file first.
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            return System.Text.Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            var utf8 = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true);
            return utf8.GetString(bytes);
        }
        catch
        {
            // Code page 1252 covers Windows-Western. Registered via
            // System.Text.Encoding.RegisterProvider on startup.
            return System.Text.Encoding.GetEncoding(1252).GetString(bytes);
        }
    }
}
