using System;
using System.Diagnostics;
using System.IO;

namespace MarkdownViewer.Tests;

/// <summary>
/// Creates NTFS directory junctions for tests. Junctions are used rather than
/// symlinks deliberately: <c>Directory.CreateSymbolicLink</c> needs elevation or
/// Developer Mode, while <c>mklink /J</c> needs neither, so these tests run on a
/// stock Windows box and on CI. A junction is a reparse point either way, which is
/// what the code under test actually keys on.
/// </summary>
internal static class TestJunction
{
    public static void Create(string linkPath, string targetPath)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("could not start cmd.exe to create a junction");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);
        if (p.ExitCode != 0 || !Directory.Exists(linkPath))
            throw new InvalidOperationException(
                $"mklink /J \"{linkPath}\" \"{targetPath}\" failed (exit {p.ExitCode}): {stdout}{stderr}");
    }
}
