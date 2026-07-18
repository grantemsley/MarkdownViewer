using System;
using System.IO;
using System.Threading.Tasks;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class VelopackUpdaterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveFeed_DefaultsToRepo_WhenOverrideUnset(string? value)
        => Assert.Equal("https://github.com/grantemsley/MarkdownViewer",
            VelopackUpdater.ResolveFeed(value));

    [Fact]
    public void ResolveFeed_UsesOverride_WhenSet()
        => Assert.Equal(@"C:\some\feed",
            VelopackUpdater.ResolveFeed(@"  C:\some\feed  "));

    [Fact]
    public void IsInstalled_FalseForNonInstalledProcess()
        // The test runner is not a Velopack install (no Update.exe layout),
        // which is exactly the portable/dev case the banner relies on.
        => Assert.False(VelopackUpdater.IsInstalled);

    [Fact]
    public async Task UpdateAndRestartAsync_ReturnsFalse_InsteadOfThrowing()
    {
        // Point the feed at an empty local dir so the check cannot reach the
        // network, then confirm the guarantee callers depend on: any failure
        // (here: not installed + empty feed) surfaces as false, never a throw,
        // so the banner can fall back to opening the release page.
        var feedDir = Path.Combine(Path.GetTempPath(),
            "mdv-velopack-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(feedDir);
        var prior = Environment.GetEnvironmentVariable(VelopackUpdater.FeedOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(VelopackUpdater.FeedOverrideVariable, feedDir);
            Assert.False(await VelopackUpdater.UpdateAndRestartAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable(VelopackUpdater.FeedOverrideVariable, prior);
            try { Directory.Delete(feedDir, recursive: true); } catch { }
        }
    }
}
