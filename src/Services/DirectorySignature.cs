using System;
using System.Collections.Generic;
using System.IO;

namespace MarkdownViewer.Services;

/// <summary>
/// Cheap "did this folder's entry list change?" fingerprint: one enumeration of a
/// single directory level, folded into a 64-bit value. The network polling
/// fallback in <see cref="VaultService"/> uses it to decide which loaded folders
/// are worth a real reconcile, so a poll costs one round trip per folder instead
/// of a full re-scan (which also stats every subfolder to decide its arrow).
///
/// Covers entry names and kind only, deliberately not sizes or timestamps: the
/// tree shows entries, and a content-only edit is picked up separately (the open
/// file is stat-checked on its own). Including write times would rebuild the
/// folder every time any file in it was saved, which is not what the local
/// watcher path does either.
/// </summary>
public static class DirectorySignature
{
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    /// <summary>
    /// Fingerprint of <paramref name="path"/>'s immediate children, or
    /// <see langword="null"/> when the folder can't be read (missing, denied, or
    /// on an unreachable share). Callers must treat null as "no information",
    /// never as "empty folder" — otherwise a dropped network link would look
    /// like every file being deleted at once.
    /// </summary>
    public static ulong? Compute(string path)
    {
        var entries = new List<(string Name, bool IsFolder)>();
        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var sub in dir.GetDirectories()) entries.Add((sub.Name, true));
            foreach (var f in dir.GetFiles()) entries.Add((f.Name, false));
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
        return OfEntries(entries);
    }

    /// <summary>
    /// Same fingerprint computed from an entry list already in hand, so a folder
    /// that was just scanned can be seeded without a second enumeration. Callers
    /// must pass real entries only (no tree placeholders).
    /// </summary>
    public static ulong OfEntries(IEnumerable<(string Name, bool IsFolder)> entries)
    {
        var keys = new List<string>();
        foreach (var (name, isFolder) in entries) keys.Add((isFolder ? "d:" : "f:") + name);

        // Sort so the fingerprint doesn't move just because the filesystem
        // handed the entries back in a different order.
        keys.Sort(StringComparer.OrdinalIgnoreCase);

        // FNV-1a, case-folded to match the OrdinalIgnoreCase comparisons the
        // tree uses everywhere else.
        var hash = FnvOffsetBasis;
        foreach (var key in keys)
        {
            foreach (var ch in key)
            {
                hash ^= char.ToLowerInvariant(ch);
                hash *= FnvPrime;
            }
            hash ^= '\n';   // separator, so "ab"+"c" and "a"+"bc" differ
            hash *= FnvPrime;
        }
        return hash;
    }
}
