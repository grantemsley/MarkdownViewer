using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

/// <summary>One content match inside a file: 1-based <paramref name="Line"/>,
/// the 1-based <paramref name="Ordinal"/> of this match among all term occurrences
/// in the file (document order — lets the UI jump to <i>this</i> occurrence, not
/// just the first), a display <paramref name="Preview"/> of that line, and the
/// matched span within the preview (<paramref name="MatchStart"/>/<paramref
/// name="MatchLength"/>) so the UI can emphasize it.</summary>
public sealed record SearchHit(int Line, int Ordinal, string Preview, int MatchStart, int MatchLength);

/// <summary>Everything found in one file: whether its <b>name</b> matched, its
/// content <paramref name="Hits"/> (possibly empty when only the name matched),
/// and a running scanned-file count for a live progress readout.</summary>
public sealed record SearchFileResult(
    string FullPath, string RelPath, bool NameMatched,
    IReadOnlyList<SearchHit> Hits, int FilesScannedSoFar);

/// <summary>Terminal tally for a completed (or cancelled/truncated) search.</summary>
public sealed record SearchSummary(
    int FilesScanned, int FilesMatched, int TotalHits, bool Truncated, bool Cancelled);

/// <summary>Resolved, per-search knobs. Built once from <c>SearchPrefs</c> so the
/// walk never recomputes the effective extension set per file.</summary>
public sealed record SearchOptions(
    long MaxFileBytes,
    IReadOnlySet<string> AllowedExtensions,
    IReadOnlySet<string> ExcludedDirNames,
    bool IncludeHidden,
    bool ScanAllText,
    int MaxDegreeOfParallelism,
    int MaxHitsPerFile,
    int MaxTotalHits)
{
    /// <summary>
    /// Resolve user <see cref="SearchPrefs"/> into a ready-to-run option set. The
    /// effective content-scan allowlist is (the user's IncludeExtensions, or
    /// ContentRouter's known-text set when that's empty) minus ExcludeExtensions;
    /// resolving it once here keeps the per-file walk from recomputing it. Extension
    /// entries are accepted with or without a leading dot.
    /// </summary>
    public static SearchOptions From(SearchPrefs p)
    {
        var baseSet = p.IncludeExtensions is { Count: > 0 }
            ? p.IncludeExtensions.Select(NormalizeExt).Where(e => e.Length > 1)
            : ContentRouter.KnownTextExtensions;
        var allowed = new HashSet<string>(baseSet, StringComparer.OrdinalIgnoreCase);
        foreach (var ex in p.ExcludeExtensions ?? Enumerable.Empty<string>())
        {
            var n = NormalizeExt(ex);
            if (n.Length > 1) allowed.Remove(n);
        }
        var exDirs = new HashSet<string>(
            (p.ExcludeFolders ?? Enumerable.Empty<string>()).Select(d => d.Trim()).Where(d => d.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        return new SearchOptions(
            p.MaxFileBytes, allowed, exDirs, p.IncludeHidden, p.ScanAllText,
            p.MaxDegreeOfParallelism, p.MaxHitsPerFile, p.MaxTotalHits);
    }

    // "md" / ".MD" / "*.md" -> ".md". Returns "." for an empty entry (filtered out).
    private static string NormalizeExt(string e)
    {
        e = (e ?? "").Trim().TrimStart('*');
        if (e.Length == 0) return ".";
        return e.StartsWith('.') ? e : "." + e;
    }
}

/// <summary>
/// On-demand full-text + filename search over a folder tree. UI-agnostic (no WPF /
/// WebView / VaultService) so it is unit-testable in full, like <see cref="TabManager"/>.
///
/// The design is tuned for a large tree reached over SMB, where latency (not CPU)
/// dominates: a lazy recursive walk feeds a bounded-parallel scan so many per-file
/// round-trips overlap; and content bytes are pulled only for files whose extension
/// is in the allowlist and whose size is under the cap — every other file is matched
/// by <b>name</b> only, which costs nothing beyond the directory enumeration.
/// </summary>
public static class FileSearchService
{
    private static readonly IReadOnlyList<SearchHit> NoHits = Array.Empty<SearchHit>();

    public static async Task<SearchSummary> SearchAsync(
        string root, string query, SearchOptions options,
        IProgress<SearchFileResult> onFile, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return new SearchSummary(0, 0, 0, false, ct.IsCancellationRequested);

        int scanned = 0, matched = 0, totalHits = 0, truncatedFlag = 0;

        // Own token so we can stop the walk once the global hit cap is reached,
        // without conflating that with the caller cancelling.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = linked.Token;

        var po = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.MaxDegreeOfParallelism),
            CancellationToken = token,
        };

        try
        {
            await Parallel.ForEachAsync(EnumerateFiles(root, options, token), po, (fi, tok) =>
            {
                var scannedSoFar = Interlocked.Increment(ref scanned);

                // Filename match is free — applies to every file, incl. binaries
                // and over-cap files we would never read.
                bool nameMatch = fi.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                IReadOnlyList<SearchHit> hits = NoHits;
                if (Volatile.Read(ref truncatedFlag) == 0 && ShouldScanContent(fi, options))
                    hits = ScanFile(fi.FullName, query, options.MaxFileBytes, options.MaxHitsPerFile, tok);

                if (nameMatch || hits.Count > 0)
                {
                    Interlocked.Increment(ref matched);
                    if (hits.Count > 0 &&
                        Interlocked.Add(ref totalHits, hits.Count) >= options.MaxTotalHits)
                    {
                        // Cap reached: mark truncated and stop the walk. This
                        // file's hits are still reported (below) — the flag just
                        // prevents starting more content scans.
                        Interlocked.Exchange(ref truncatedFlag, 1);
                    }
                    onFile.Report(new SearchFileResult(
                        fi.FullName, GetRelPath(root, fi.FullName), nameMatch, hits, scannedSoFar));
                }

                if (Volatile.Read(ref truncatedFlag) == 1) linked.Cancel();
                return ValueTask.CompletedTask;
            });
        }
        catch (OperationCanceledException) { /* caller-cancel or self-cancel at cap */ }

        return new SearchSummary(
            FilesScanned: Volatile.Read(ref scanned),
            FilesMatched: Volatile.Read(ref matched),
            TotalHits: Volatile.Read(ref totalHits),
            Truncated: Volatile.Read(ref truncatedFlag) == 1,
            // Only a caller cancellation counts as "cancelled"; a cap-stop is a
            // completed-but-truncated result, not a cancel.
            Cancelled: ct.IsCancellationRequested);
    }

    // ── walk ────────────────────────────────────────────────────────────────

    // Lazily yield every file under root, one directory at a time via an explicit
    // stack (not Directory.EnumerateFiles(AllDirectories), which aborts on the
    // first denied folder and can't prune subtrees). Per-directory errors are
    // swallowed so an unreadable folder skips rather than killing the search.
    private static IEnumerable<FileInfo> EnumerateFiles(string root, SearchOptions opt, CancellationToken ct)
    {
        // Junctions/symlinks are followed (the tree shows them, so search must
        // look inside them), which reintroduces the possibility of a cycle. Guard
        // by real path: every directory is entered at most once. Any cycle has to
        // pass through a reparse point, and each distinct target is admitted only
        // once, so the walk always terminates. This also stops a junction to an
        // already-walked folder from reporting its files twice.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        visited.Add(RealPath(new DirectoryInfo(root)));
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            DirectoryInfo[] subDirs;
            FileInfo[] files;
            try
            {
                var di = new DirectoryInfo(dir);
                subDirs = di.GetDirectories();
                files = di.GetFiles();
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }
            catch (System.Security.SecurityException) { continue; }

            foreach (var sub in subDirs)
            {
                if (opt.ExcludedDirNames.Contains(sub.Name)) continue;
                if (!opt.IncludeHidden && IsHidden(sub)) continue;
                if (!visited.Add(RealPath(sub))) continue;   // cycle, or already reached another way
                stack.Push(sub.FullName);
            }

            // FileInfo from GetFiles() carries Name/Length/Attributes populated by
            // the directory enumeration — reading them here is no extra syscall.
            foreach (var f in files)
            {
                if (!opt.IncludeHidden && IsHidden(f)) continue;
                yield return f;
            }
        }
    }

    private static bool IsHidden(FileSystemInfo fsi)
    {
        if (fsi.Name.StartsWith('.')) return true;
        try { return (fsi.Attributes & FileAttributes.Hidden) != 0; }
        catch { return false; }
    }

    // Identity key for the cycle guard: a reparse point resolves to its final
    // target, anything else is its own path. A directory reached *through* a
    // junction keys off the virtual path rather than the target's real one, so
    // this dedups link targets, not every alias. That is enough to terminate:
    // a cycle must cross a reparse point, and each target is admitted once.
    // A broken or too-deeply-chained link falls back to the literal path — it
    // simply won't dedup, and the directory read that follows fails harmlessly.
    private static string RealPath(DirectoryInfo dir)
    {
        try
        {
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = dir.ResolveLinkTarget(returnFinalTarget: true);
                if (target != null) return Trim(target.FullName);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Trim(dir.FullName);

        static string Trim(string p) =>
            p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    // ── scan ────────────────────────────────────────────────────────────────

    private static bool ShouldScanContent(FileInfo fi, SearchOptions opt)
    {
        if (fi.Length > opt.MaxFileBytes) return false;   // free size gate, no read
        if (opt.AllowedExtensions.Contains(fi.Extension)) return true;
        // Unknown extension: only under ScanAllText, and only after a cheap binary
        // peek so we don't line-scan a mislabeled blob.
        return opt.ScanAllText && !ContentRouter.LooksBinary(fi.FullName);
    }

    private static IReadOnlyList<SearchHit> ScanFile(
        string path, string query, long maxBytes, int maxHits, CancellationToken ct)
    {
        string text;
        try { text = ContentRouter.DecodeCappedFile(path, maxBytes, out _); }
        catch (IOException) { return NoHits; }
        catch (UnauthorizedAccessException) { return NoHits; }

        List<SearchHit>? hits = null;
        int line = 0;
        int occ = 0;   // running count of term occurrences across the whole file
        using var reader = new StringReader(text);
        string? lineText;
        while ((lineText = reader.ReadLine()) != null)
        {
            line++;
            int idx = lineText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // This hit points at the first occurrence on this line, whose
                // ordinal is (occurrences in all previous lines) + 1.
                (hits ??= new List<SearchHit>()).Add(MakeHit(line, occ + 1, lineText, idx, query.Length));
                if (hits.Count >= maxHits) break;
            }
            // Count every occurrence on this line (not just the first) so ordinals
            // line up with a whole-document find-in-page over the same term.
            occ += CountOccurrences(lineText, query);
            // Bound cancellation latency on a huge single-line-free file.
            if ((line & 0x3FF) == 0) ct.ThrowIfCancellationRequested();
        }
        return (IReadOnlyList<SearchHit>?)hits ?? NoHits;
    }

    private static int CountOccurrences(string s, string q)
    {
        int count = 0, i = 0;
        while ((i = s.IndexOf(q, i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            i += q.Length;
        }
        return count;
    }

    // Build a display-safe preview: drop leading indentation, and if the line is
    // very long, window it around the match (with a leading ellipsis) so a minified
    // line can't blow up the results list. MatchStart is kept valid against preview.
    private const int PreviewMax = 400;
    private static SearchHit MakeHit(int line, int ordinal, string lineText, int idx, int queryLen)
    {
        int lead = lineText.Length - lineText.TrimStart().Length;
        string body = lineText.Substring(lead);
        int rel = idx - lead;

        string preview;
        int matchStart;
        if (body.Length <= PreviewMax)
        {
            preview = body; matchStart = rel;
        }
        else if (rel < PreviewMax - queryLen)
        {
            preview = body.Substring(0, PreviewMax); matchStart = rel;
        }
        else
        {
            int winStart = Math.Max(0, rel - 40);
            preview = "…" + body.Substring(winStart, Math.Min(PreviewMax, body.Length - winStart));
            matchStart = rel - winStart + 1; // +1 for the ellipsis
        }
        int matchLen = Math.Max(0, Math.Min(queryLen, preview.Length - matchStart));
        return new SearchHit(line, ordinal, preview, matchStart, matchLen);
    }

    private static string GetRelPath(string root, string full)
    {
        try { return Path.GetRelativePath(root, full); }
        catch { return full; }
    }
}
