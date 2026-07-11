using System;
using System.IO;
using MarkdownViewer.Models;
using MarkdownViewer.Services;
using Xunit;

namespace MarkdownViewer.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mvtest_settings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ─── Round-trip ─────────────────────────────────────────────────────────

    [Fact]
    public void Roundtrip_TranscriptVisibleCategories_Persist()
    {
        var s = new AppSettings();
        s.Transcripts.VisibleCategories["conversation"] = false;
        s.Transcripts.VisibleCategories["queue"] = true;

        SettingsService.SaveTo(_path, s);
        var loaded = SettingsService.LoadFrom(_path);

        Assert.False(loaded.Transcripts.VisibleCategories["conversation"]);
        Assert.True(loaded.Transcripts.VisibleCategories["queue"]);
    }

    [Fact]
    public void Roundtrip_BodyStyleGithub_Persists()
    {
        var s = new AppSettings();
        s.Reading.BodyStyle = "github";

        SettingsService.SaveTo(_path, s);
        var loaded = SettingsService.LoadFrom(_path);

        Assert.Equal("github", loaded.Reading.BodyStyle);
    }

    [Fact]
    public void Roundtrip_AllReadingPrefs_Stick()
    {
        var s = new AppSettings();
        s.Reading.Typeface = "serif";
        s.Reading.FontSize = 18;
        s.Reading.MarginPct = 72;
        s.Reading.ShowLineNumbers = true;
        s.Reading.BodyStyle = "github";

        SettingsService.SaveTo(_path, s);
        var loaded = SettingsService.LoadFrom(_path);

        Assert.Equal("serif", loaded.Reading.Typeface);
        Assert.Equal(18, loaded.Reading.FontSize);
        Assert.Equal(72, loaded.Reading.MarginPct);
        Assert.True(loaded.Reading.ShowLineNumbers);
        Assert.Equal("github", loaded.Reading.BodyStyle);
    }

    [Fact]
    public void Roundtrip_SortPrefs_Persist()
    {
        var s = new AppSettings();
        s.Sorting.FolderKey = "modified";
        s.Sorting.FolderDir = "desc";
        s.Sorting.FileKey = "extension";
        s.Sorting.FileDir = "asc";

        SettingsService.SaveTo(_path, s);
        var loaded = SettingsService.LoadFrom(_path);

        Assert.Equal("modified", loaded.Sorting.FolderKey);
        Assert.Equal("desc", loaded.Sorting.FolderDir);
        Assert.Equal("extension", loaded.Sorting.FileKey);
        Assert.Equal("asc", loaded.Sorting.FileDir);
    }

    [Fact]
    public void Load_MissingSortBlock_GetsNameAscendingDefaults()
    {
        // A settings file predating the sorting feature must load cleanly with
        // sorting backfilled to defaults (additive pref, no schema bump).
        File.WriteAllText(_path, $"{{\"schemaVersion\":{SettingsSchema.Current},\"theme\":\"dark\"}}");
        var s = SettingsService.LoadFrom(_path);

        Assert.NotNull(s.Sorting);
        Assert.Equal("name", s.Sorting.FolderKey);
        Assert.Equal("asc", s.Sorting.FolderDir);
        Assert.Equal("name", s.Sorting.FileKey);
        Assert.Equal("asc", s.Sorting.FileDir);
    }

    [Fact]
    public void Load_InvalidSortValues_CoercedToDefault()
    {
        File.WriteAllText(_path, $$"""
        {
          "schemaVersion": {{SettingsSchema.Current}},
          "sorting": { "folderKey": "rainbow", "folderDir": "sideways", "fileKey": "modified", "fileDir": "desc" }
        }
        """);
        var s = SettingsService.LoadFrom(_path);

        Assert.Equal("name", s.Sorting.FolderKey);  // invalid key -> default
        Assert.Equal("asc", s.Sorting.FolderDir);   // invalid dir -> default
        Assert.Equal("modified", s.Sorting.FileKey); // valid -> kept
        Assert.Equal("desc", s.Sorting.FileDir);     // valid -> kept
    }

    // ─── Resilience ─────────────────────────────────────────────────────────

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = SettingsService.LoadFrom(_path);
        Assert.Equal(SettingsSchema.Current, s.SchemaVersion);
        Assert.Equal("system", s.Theme);
        Assert.Equal("win11", s.Reading.BodyStyle);
    }

    [Fact]
    public void Load_GarbageJson_BacksUpAndReturnsDefaults()
    {
        File.WriteAllText(_path, "{ this is not valid json");
        var s = SettingsService.LoadFrom(_path);

        Assert.Equal(SettingsSchema.Current, s.SchemaVersion);
        // The corrupt file should have been moved aside.
        var bakFiles = Directory.GetFiles(_dir, "*.bak-*");
        Assert.NotEmpty(bakFiles);
    }

    [Fact]
    public void Load_WrongSchemaVersion_BacksUpAndReturnsDefaults()
    {
        File.WriteAllText(_path, "{\"schemaVersion\":99}");
        var s = SettingsService.LoadFrom(_path);

        Assert.Equal(SettingsSchema.Current, s.SchemaVersion);
        var bakFiles = Directory.GetFiles(_dir, "*.bak-*");
        Assert.NotEmpty(bakFiles);
    }

    [Fact]
    public void Load_MissingNewProperty_GetsDefault()
    {
        // Schema-2 settings file without the new transcripts block — should
        // load cleanly with defaults filled in. Validates the additive-pref
        // claim made in the JSONL persistence work.
        File.WriteAllText(_path, """
        {
          "schemaVersion": 2,
          "theme": "dark",
          "reading": { "typeface": "mono", "fontSize": 14, "marginPct": 85, "showLineNumbers": false }
        }
        """);
        var s = SettingsService.LoadFrom(_path);

        Assert.Equal("dark", s.Theme);
        Assert.Equal("mono", s.Reading.Typeface);
        // New properties default to their model values.
        Assert.Equal("win11", s.Reading.BodyStyle);
        Assert.NotNull(s.Transcripts);
        Assert.True(s.Transcripts.VisibleCategories["conversation"]);
    }

    [Fact]
    public void Load_UnknownBodyStyleValue_CoercedToDefault()
    {
        // Normalize() runs on load: an unknown bodyStyle is whitelisted back to
        // the default so a hand-edited file can't push nonsense into the
        // renderer. (This deliberately replaces the older "stays as-is".)
        File.WriteAllText(_path, $"{{\"schemaVersion\":{SettingsSchema.Current},\"reading\":{{\"bodyStyle\":\"chartreuse\"}}}}");
        var s = SettingsService.LoadFrom(_path);
        Assert.Equal("win11", s.Reading.BodyStyle);
    }

    [Fact]
    public void Load_OutOfRangeReadingPrefs_ClampedAndWhitelisted()
    {
        File.WriteAllText(_path, $$"""
        {
          "schemaVersion": {{SettingsSchema.Current}},
          "theme": "chartreuse",
          "reading": { "typeface": "comic", "fontSize": 99999, "marginPct": -500, "bodyStyle": "win11" }
        }
        """);
        var s = SettingsService.LoadFrom(_path);

        Assert.Equal("system", s.Theme);             // invalid theme -> default
        Assert.Equal("system", s.Reading.Typeface);  // invalid typeface -> default
        Assert.Equal(22, s.Reading.FontSize);        // clamped to max (11..22)
        Assert.Equal(50, s.Reading.MarginPct);       // clamped to min (50..100)
    }

    [Fact]
    public void LoadFrom_NullCollections_CoalesceToDefaults_NoThrow()
    {
        // Valid schema version (so the corrupt-file reset doesn't fire) but null
        // collections. Previously these NRE'd downstream (RestoreTabsFromSession,
        // recents) and surfaced as a bogus "WebView2 init failed" dialog.
        File.WriteAllText(_path, $$"""
        {
          "schemaVersion": {{SettingsSchema.Current}},
          "tabs": { "enabled": true, "sessions": null },
          "vaults": { "current": "", "pinned": null, "recents": null, "lastFile": null },
          "transcripts": { "visibleCategories": null }
        }
        """);
        var s = SettingsService.LoadFrom(_path);

        Assert.NotNull(s.Tabs.Sessions);
        Assert.NotNull(s.Vaults.Pinned);
        Assert.NotNull(s.Vaults.Recents);
        Assert.NotNull(s.Vaults.LastFile);
        Assert.NotNull(s.Transcripts.VisibleCategories);
    }
}
