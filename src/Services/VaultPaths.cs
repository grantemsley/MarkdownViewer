using System;
using System.IO;

namespace MarkdownViewer.Services;

/// <summary>
/// Resolves a vault-relative request path (the <c>&lt;rel&gt;</c> in
/// <c>https://app.local/__vault/&lt;rel&gt;</c>) to an absolute on-disk path,
/// refusing anything that escapes the vault root: <c>../</c> traversal, rooted
/// (absolute / drive / UNC) paths, and sibling-prefix tricks. Returns
/// <see langword="null"/> when the input is empty, malformed, or would resolve
/// outside the root. This is the single security gate for serving vault files
/// same-origin, so it lives in a pure static helper that is unit-tested directly.
/// </summary>
public static class VaultPaths
{
    public static string? ResolveWithinRoot(string? root, string? rel)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(rel))
            return null;

        // A rooted rel (e.g. "C:\…", "\\server\share", "/etc/…") would let
        // Path.Combine discard the root entirely — reject before combining.
        if (Path.IsPathRooted(rel))
            return null;

        string fullRoot, combined;
        try
        {
            // Normalize the root and strip any trailing separator so the prefix
            // comparison below is exact (C:\vault vs C:\vault2).
            fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            combined = Path.GetFullPath(Path.Combine(fullRoot, rel));
        }
        catch
        {
            return null; // invalid characters, too long, etc.
        }

        if (combined.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
            return combined;
        if (combined.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return combined;
        return null; // escaped the root
    }
}
