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

    // Serializes concurrent saves (the debounced window-state save and the
    // close-time flush) so two writers can't interleave on the same file.
    private static readonly object _saveLock = new();

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
            // Coerce values into valid ranges/sets before anything consumes
            // them — a hand-edited (but still parseable) file shouldn't be able
            // to push e.g. a 99999px font straight into the renderer.
            s.Normalize();
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
            var json = JsonSerializer.Serialize(settings, Options);
            // Write to a sibling temp file then atomically replace the target,
            // so a crash or power loss mid-write can't leave a half-written
            // settings.json — which would fail to parse next launch and reset
            // every setting to defaults. File.Move with overwrite is an atomic
            // replace on the same NTFS volume (tmp sits beside the target).
            lock (_saveLock)
            {
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
            }
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
