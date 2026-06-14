using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownViewer.Services;

/// <summary>
/// Lightweight, notify-only update check. Queries the GitHub Releases API for
/// the repo's latest release and compares its tag to the running assembly
/// version. It never downloads or installs anything — the UI just surfaces a
/// "new version available" banner linking to the release page, so the app stays
/// a single portable exe. Every failure (offline, rate-limited, malformed JSON)
/// is swallowed and reported as "no update", so a check never disrupts startup.
/// </summary>
public static class UpdateService
{
    private const string Owner = "grantemsley";
    private const string Repo = "MarkdownViewer";
    private const string LatestReleaseApi =
        "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

    // Fallback target if the API response somehow lacks an html_url.
    private const string ReleasesPage =
        "https://github.com/" + Owner + "/" + Repo + "/releases/latest";

    // One shared client. GitHub rejects API requests that omit a User-Agent.
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MarkdownViewer-update-check");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Check no more than once per this interval (see <see cref="IsCheckDue"/>).</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    /// <summary>A newer release than the one running.</summary>
    public sealed record Result(string LatestVersion, string ReleaseUrl);

    /// <summary>
    /// Outcome of a check. <see cref="Completed"/> is true when GitHub was
    /// actually reached (so the daily throttle should be stamped — even if there
    /// was no newer release); false when the request couldn't complete (offline,
    /// timeout) so the caller should retry on the next launch. <see cref="Update"/>
    /// is set only when a strictly-newer release exists.
    /// </summary>
    public sealed record CheckOutcome(bool Completed, Result? Update);

    /// <summary>
    /// Query the latest release and compare it to <paramref name="current"/>.
    /// Never throws — a transport failure returns a not-completed outcome.
    /// </summary>
    public static async Task<CheckOutcome> CheckAsync(Version current, CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync(LatestReleaseApi, ct);
            // A non-success status still reached GitHub (e.g. 403 rate-limit, 404).
            // Count it as completed so we don't hammer the API before next interval.
            if (!resp.IsSuccessStatusCode) return new CheckOutcome(true, null);

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagEl)) return new CheckOutcome(true, null);
            var tag = tagEl.GetString();
            var latest = ParseVersion(tag);
            if (latest == null || !IsNewer(latest, current)) return new CheckOutcome(true, null);

            var url = root.TryGetProperty("html_url", out var urlEl)
                ? urlEl.GetString() ?? ReleasesPage
                : ReleasesPage;
            return new CheckOutcome(true, new Result(NormalizeTag(tag!), url));
        }
        catch
        {
            return new CheckOutcome(false, null); // offline / timeout → retry next launch
        }
    }

    /// <summary>
    /// True if a check is due: never checked (<paramref name="lastCheckUtc"/> at
    /// its default), or at least <paramref name="interval"/> has elapsed since.
    /// </summary>
    public static bool IsCheckDue(DateTime lastCheckUtc, DateTime nowUtc, TimeSpan interval)
        => nowUtc - lastCheckUtc >= interval;

    /// <summary>
    /// The running app's version, from assembly metadata (CI stamps it via
    /// -p:Version from the release tag). Falls back to 0.0.0.0.
    /// </summary>
    public static Version CurrentVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version
           ?? Assembly.GetExecutingAssembly().GetName().Version
           ?? new Version(0, 0, 0, 0);

    // ── pure helpers (unit-tested) ─────────────────────────────────────────

    /// <summary>Strip a leading 'v'/'V' and surrounding whitespace from a tag.</summary>
    public static string NormalizeTag(string tag) =>
        (tag ?? "").Trim().TrimStart('v', 'V').Trim();

    /// <summary>
    /// Parse a release tag (e.g. "v0.9.7.0", "0.9.7") into a Version, padding
    /// unspecified components to 0. Returns null for anything that isn't a plain
    /// dotted-numeric tag (e.g. "nightly", "1.2.3-beta").
    /// </summary>
    public static Version? ParseVersion(string? tag)
    {
        var s = NormalizeTag(tag ?? "");
        if (s.Length == 0) return null;
        return Version.TryParse(s, out var v) ? Pad(v) : null;
    }

    /// <summary>True iff <paramref name="latest"/> is strictly newer than <paramref name="current"/>.</summary>
    public static bool IsNewer(Version latest, Version current) => Pad(latest) > Pad(current);

    // Version.TryParse leaves unspecified components as -1, so "0.9.7" would
    // otherwise sort below "0.9.7.0" (same release). Pad them to 0 so a 3-part
    // and 4-part tag for the same version compare equal.
    private static Version Pad(Version v) => new(
        v.Major < 0 ? 0 : v.Major,
        v.Minor < 0 ? 0 : v.Minor,
        v.Build < 0 ? 0 : v.Build,
        v.Revision < 0 ? 0 : v.Revision);
}
