using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class AccentPaletteTests
{
    // Windows' palette for the default blue accent, as stored in AccentPalette:
    // Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3, spare (RGBA each).
    private static readonly byte[] DefaultBlue =
    {
        0x99, 0xEB, 0xFF, 0x00,  0x4C, 0xC2, 0xFF, 0x00,  0x00, 0x91, 0xF8, 0x00,
        0x00, 0x78, 0xD4, 0x00,  0x00, 0x67, 0xC0, 0x00,  0x00, 0x3E, 0x92, 0x00,
        0x00, 0x1A, 0x68, 0x00,  0xF7, 0x63, 0x0C, 0x00,
    };

    [Fact]
    public void Light_mode_uses_Dark1()
        => Assert.Equal("#0067C0", AccentPalette.TextShade(DefaultBlue, dark: false, "#0078D4"));

    [Fact]
    public void Dark_mode_uses_Light2()
        => Assert.Equal("#4CC2FF", AccentPalette.TextShade(DefaultBlue, dark: true, "#0078D4"));

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[] { 1, 2, 3 })]
    public void Missing_or_short_palette_falls_back_to_the_raw_accent(byte[]? palette)
        => Assert.Equal("#0078D4", AccentPalette.TextShade(palette, dark: true, "#0078D4"));
}
