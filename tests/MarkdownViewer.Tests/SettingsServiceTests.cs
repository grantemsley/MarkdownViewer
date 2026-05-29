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
    public void Load_UnknownBodyStyleValue_StaysAsIs()
    {
        // We don't validate the string at load. Document the behavior so a
        // future tightening (e.g. clamp to known values) is a deliberate
        // change rather than an accident.
        File.WriteAllText(_path, $"{{\"schemaVersion\":{SettingsSchema.Current},\"reading\":{{\"bodyStyle\":\"chartreuse\"}}}}");
        var s = SettingsService.LoadFrom(_path);
        Assert.Equal("chartreuse", s.Reading.BodyStyle);
    }
}
