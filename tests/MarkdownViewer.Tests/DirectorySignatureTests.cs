using System;
using System.IO;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class DirectorySignatureTests : IDisposable
{
    private readonly string _dir;

    public DirectorySignatureTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mvtest_sig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Compute_UnchangedFolder_IsStable()
    {
        File.WriteAllText(Path.Combine(_dir, "a.md"), "x");
        Assert.Equal(DirectorySignature.Compute(_dir), DirectorySignature.Compute(_dir));
    }

    [Fact]
    public void Compute_NewFile_Changes()
    {
        var before = DirectorySignature.Compute(_dir);
        File.WriteAllText(Path.Combine(_dir, "new.md"), "x");
        Assert.NotEqual(before, DirectorySignature.Compute(_dir));
    }

    [Fact]
    public void Compute_NewSubfolder_Changes()
    {
        var before = DirectorySignature.Compute(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        Assert.NotEqual(before, DirectorySignature.Compute(_dir));
    }

    [Fact]
    public void Compute_DeletedFile_Changes()
    {
        var p = Path.Combine(_dir, "a.md");
        File.WriteAllText(p, "x");
        var before = DirectorySignature.Compute(_dir);
        File.Delete(p);
        Assert.NotEqual(before, DirectorySignature.Compute(_dir));
    }

    [Fact]
    public void Compute_RenamedFile_Changes()
    {
        var p = Path.Combine(_dir, "a.md");
        File.WriteAllText(p, "x");
        var before = DirectorySignature.Compute(_dir);
        File.Move(p, Path.Combine(_dir, "b.md"));
        Assert.NotEqual(before, DirectorySignature.Compute(_dir));
    }

    [Fact]
    public void Compute_FileAndFolderOfSameName_AreDistinct()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "notes"));
        var withFolder = DirectorySignature.Compute(_dir);

        var other = Path.Combine(Path.GetTempPath(), "mvtest_sig2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);
        try
        {
            File.WriteAllText(Path.Combine(other, "notes"), "");
            Assert.NotEqual(withFolder, DirectorySignature.Compute(other));
        }
        finally { try { Directory.Delete(other, recursive: true); } catch { } }
    }

    [Fact]
    public void Compute_ContentEditOnly_DoesNotChange()
    {
        // Deliberate: the fingerprint tracks the entry list, so saving a file
        // doesn't rebuild the folder. A content change to the *open* file is
        // detected separately by its own stat check.
        var p = Path.Combine(_dir, "a.md");
        File.WriteAllText(p, "x");
        var before = DirectorySignature.Compute(_dir);
        File.WriteAllText(p, "a much longer body than before");
        Assert.Equal(before, DirectorySignature.Compute(_dir));
    }

    [Fact]
    public void Compute_EmptyFolder_ReturnsValueNotNull()
    {
        // An empty folder must be distinguishable from an unreadable one:
        // null means "no information", and the poller must not read it as
        // "everything was deleted".
        Assert.NotNull(DirectorySignature.Compute(_dir));
    }

    [Fact]
    public void Compute_MissingFolder_ReturnsNull()
    {
        Assert.Null(DirectorySignature.Compute(Path.Combine(_dir, "ghost")));
    }

    [Fact]
    public void Compute_EmptyFolder_DiffersFromMissingFolder()
    {
        var empty = DirectorySignature.Compute(_dir);
        var missing = DirectorySignature.Compute(Path.Combine(_dir, "ghost"));
        Assert.NotEqual(empty, missing);
    }
}
