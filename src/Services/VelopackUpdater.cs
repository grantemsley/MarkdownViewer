using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace MarkdownViewer.Services;

/// <summary>
/// Thin glue over Velopack's update API: the *apply* half of the update
/// story. Detection (is there a newer release?) stays with
/// <see cref="UpdateService"/>, which drives the banner for installed and
/// portable copies alike. This class only matters when the user clicks
/// Download on an installed (Setup.exe) copy: check Velopack's feed,
/// download, apply, restart. Every failure returns false so the caller can
/// fall back to opening the release page - the user always has a way
/// forward, and the portable exe (IsInstalled == false) never enters this
/// path at all.
/// </summary>
public static class VelopackUpdater
{
    private const string RepoUrl = "https://github.com/grantemsley/MarkdownViewer";

    /// <summary>
    /// Environment variable that redirects the update feed to a local
    /// directory (a vpk output folder) for testing the install/update cycle
    /// without touching GitHub. Redirecting the feed is not a security
    /// boundary: anyone who can set this can already replace the exe.
    /// </summary>
    public const string FeedOverrideVariable = "MARKDOWNVIEWER_UPDATE_FEED";

    /// <summary>The update feed to use: the override when set, else the repo.</summary>
    public static string ResolveFeed(string? overrideValue)
        => string.IsNullOrWhiteSpace(overrideValue) ? RepoUrl : overrideValue.Trim();

    private static UpdateManager CreateManager()
    {
        var feed = ResolveFeed(Environment.GetEnvironmentVariable(FeedOverrideVariable));
        // A GitHub URL needs GithubSource (it reads the Releases API to find
        // velopack assets; null token = anonymous, fine for a public repo).
        // Anything else (local dir for testing) can go straight to
        // UpdateManager, which resolves non-HTTP paths to SimpleFileSource.
        return feed.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
            ? new UpdateManager(new GithubSource(feed, accessToken: null, prerelease: false))
            : new UpdateManager(feed);
    }

    /// <summary>
    /// True when this process is a Velopack-installed copy (Setup.exe layout
    /// with Update.exe alongside). False for the portable exe, dev builds,
    /// and unit tests - and on any error, so callers can treat this as
    /// "safe to hand the click to Velopack".
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            try { return CreateManager().IsInstalled; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Check the feed, download the newest release, apply it and restart the
    /// app. On success the process exits inside this call and never returns.
    /// Returns false when there is nothing to apply (e.g. the release exists
    /// but its Velopack assets are missing) or on any failure - callers
    /// should then fall back to opening the release page.
    /// <paramref name="progress"/> reports download percent (0-100) on a
    /// worker thread; marshal to the UI thread before touching controls.
    /// </summary>
    public static async Task<bool> UpdateAndRestartAsync(Action<int>? progress = null)
    {
        try
        {
            var manager = CreateManager();
            var info = await manager.CheckForUpdatesAsync();
            if (info == null) return false;
            await manager.DownloadUpdatesAsync(info, progress);
            // Exits the process immediately (no WPF shutdown path; the OS
            // releases the single-instance mutex and pipe with the process),
            // applies, relaunches.
            manager.ApplyUpdatesAndRestart(info.TargetFullRelease);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
