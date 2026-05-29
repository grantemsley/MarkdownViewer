using System;
using System.IO;
using System.Text.Json;
using MarkdownViewer.Models;

namespace MarkdownViewer.Services;

public static class SettingsService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MarkdownViewer");

    private static readonly string Path_ = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static AppSettings Load() => LoadFrom(Path_);

    public static void Save(AppSettings settings) => SaveTo(Path_, settings);

    // Path-injecting overloads, used by tests to keep the user's real
    // settings file untouched. The default-path public methods above forward
    // here.
    public static AppSettings LoadFrom(string path)
    {
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<AppSettings>(json, Options);
            if (s == null || s.SchemaVersion != SettingsSchema.Current)
            {
                ResetCorruptFile(path);
                return new AppSettings();
            }
            return s;
        }
        catch
        {
            ResetCorruptFile(path);
            return new AppSettings();
        }
    }

    public static void SaveTo(string path, AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // Best-effort persistence; ignore IO errors.
        }
    }

    private static void ResetCorruptFile(string path)
    {
        try
        {
            var bak = path + ".bak-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Move(path, bak, overwrite: true);
        }
        catch
        {
            try { File.Delete(path); } catch { }
        }
    }

    public static string CrashLogPath => Path.Combine(Dir, "crash.log");
}
