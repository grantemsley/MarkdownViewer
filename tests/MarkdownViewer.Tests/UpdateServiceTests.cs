using System;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v0.9.7.0", "0.9.7.0")]
    [InlineData("0.9.7", "0.9.7")]
    [InlineData("  V1.2.3  ", "1.2.3")]
    [InlineData("", "")]
    public void NormalizeTag_StripsPrefixAndWhitespace(string tag, string expected)
        => Assert.Equal(expected, UpdateService.NormalizeTag(tag));

    [Theory]
    [InlineData("v0.9.7.0", 0, 9, 7, 0)]
    [InlineData("0.9.7", 0, 9, 7, 0)]   // 3-part pads to .0
    [InlineData("V2.0", 2, 0, 0, 0)]
    public void ParseVersion_ParsesDottedTags(string tag, int a, int b, int c, int d)
        => Assert.Equal(new Version(a, b, c, d), UpdateService.ParseVersion(tag));

    [Theory]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("nightly")]
    [InlineData("1.2.3-beta")]
    public void ParseVersion_RejectsNonNumericTags(string tag)
        => Assert.Null(UpdateService.ParseVersion(tag));

    [Fact]
    public void IsNewer_DetectsAnUpgrade()
    {
        Assert.True(UpdateService.IsNewer(new Version(0, 9, 8, 0), new Version(0, 9, 7, 0)));
        Assert.True(UpdateService.IsNewer(new Version(1, 0, 0, 0), new Version(0, 9, 7, 0)));
    }

    [Fact]
    public void IsNewer_FalseForSameOrOlder()
    {
        Assert.False(UpdateService.IsNewer(new Version(0, 9, 7, 0), new Version(0, 9, 7, 0)));
        Assert.False(UpdateService.IsNewer(new Version(0, 9, 6, 0), new Version(0, 9, 7, 0)));
    }

    [Fact]
    public void IsNewer_TreatsMissingComponentsAsZero()
    {
        // "0.9.7" and "0.9.7.0" name the same release — not an upgrade.
        Assert.False(UpdateService.IsNewer(new Version(0, 9, 7), new Version(0, 9, 7, 0)));
        Assert.False(UpdateService.IsNewer(new Version(0, 9, 7, 0), new Version(0, 9, 7)));
    }

    private static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    [Fact]
    public void IsCheckDue_TrueWhenNeverChecked()
        => Assert.True(UpdateService.IsCheckDue(default, Now, Day));

    [Fact]
    public void IsCheckDue_FalseWithinInterval()
        => Assert.False(UpdateService.IsCheckDue(Now.AddHours(-5), Now, Day));

    [Theory]
    [InlineData(-24)] // boundary is inclusive: exactly a day later is due
    [InlineData(-25)]
    public void IsCheckDue_TrueAtOrAfterInterval(int hoursAgo)
        => Assert.True(UpdateService.IsCheckDue(Now.AddHours(hoursAgo), Now, Day));
}
