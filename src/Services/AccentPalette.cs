using System;
using Microsoft.Win32;

namespace MarkdownViewer.Services;

/// <summary>
/// Picks the shade of the Windows accent colour to use for text (links, the
/// "reloaded" flash) in the rendered content. The raw accent is too dark to read
/// on a dark background, so Windows itself uses lighter/darker palette shades
/// for accent text; this follows the same choice reader.css's defaults encode:
/// SystemAccentColorDark1 in light mode, SystemAccentColorLight2 in dark mode.
///
/// The palette is the 32-byte <c>AccentPalette</c> value Windows keeps under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent</c>: eight
/// RGBA entries, Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3, spare.
/// </summary>
public static class AccentPalette
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const int Light2 = 1, Dark1 = 4;

    /// <summary>
    /// "#RRGGBB" for the text shade in the given mode, or <paramref name="fallback"/>
    /// (the raw accent) when the palette is missing or malformed.
    /// </summary>
    public static string TextShade(byte[]? palette, bool dark, string fallback)
    {
        if (palette is null || palette.Length < 32) return fallback;
        var i = (dark ? Light2 : Dark1) * 4;
        return $"#{palette[i]:X2}{palette[i + 1]:X2}{palette[i + 2]:X2}";
    }

    /// <summary>The current user's accent palette from the registry, or null.</summary>
    public static byte[]? ReadPalette()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("AccentPalette") as byte[];
        }
        catch { return null; }
    }
}
