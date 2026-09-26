using System;
using System.IO;
using System.Linq;

namespace MarkdownViewer.Services;

/// <summary>
/// Resolves a clicked <c>[[target]]</c> wiki link to a file inside the vault,
/// the way Obsidian does: a target without an extension means a markdown note.
/// Tries the path relative to the current note's folder, then to the vault root,
/// then falls back to a search of the vault by file name (a target with folders,
/// like <c>projects/plan</c>, must match the tail of the found path). Among
/// several matches the one with the shortest vault-relative path wins. Only
/// files inside the vault are ever returned.
/// </summary>
public static class WikiLinkResolver
{
    public static string? Resolve(string? vaultRoot, string? currentDir, string? target)
    {
        if (string.IsNullOrEmpty(vaultRoot) || string.IsNullOrWhiteSpace(target)) return null;
        var t = target.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(t)) return null;
        // "report.pdf" is a file as named; "Note" (or "v1.2 notes") is a note.
        var candidates = Path.HasExtension(t)
            ? new[] { t, t + ".md" }
            : new[] { t + ".md", t };

        foreach (var c in candidates)
        {
            foreach (var baseDir in new[] { currentDir, vaultRoot })
            {
                if (string.IsNullOrEmpty(baseDir)) continue;
                string abs;
                try { abs = Path.GetFullPath(Path.Combine(baseDir, c)); }
                catch { continue; }
                var inVault = VaultPaths.AbsoluteWithinRoot(vaultRoot, abs);
                if (inVault is not null && File.Exists(inVault)) return inVault;
            }
        }

        var root = Path.GetFullPath(vaultRoot);
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        foreach (var c in candidates)
        {
            var tail = Path.DirectorySeparatorChar + c;
            string[] hits;
            try
            {
                hits = Directory.EnumerateFiles(root, Path.GetFileName(c), opts)
                    .Where(p => p.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch { continue; }
            var best = hits
                .OrderBy(p => p.Length)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (best is not null) return best;
        }
        return null;
    }
}
