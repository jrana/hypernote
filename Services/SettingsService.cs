using System.IO;
using System.Text.Json;

namespace HyperNote.Services;

public class AppSettings
{
    // ── Appearance ───────────────────────────────────────────────────────────
    public bool DarkTheme { get; set; }
    public bool ShowLineNumbers { get; set; } = true;
    public bool ShowMinimap     { get; set; } = true;
    public bool ShowWhitespace  { get; set; } = false;



    // ── Editor ───────────────────────────────────────────────────────────────
    public string EditorFontFamily { get; set; } = "Consolas";
    public double EditorFontSize   { get; set; } = 13;
    public bool   WordWrap          { get; set; } = false;
    public bool   HighlightCurrentLine { get; set; } = true;
    public bool   ConvertTabsToSpaces { get; set; } = false;
    public int    IndentWidth       { get; set; } = 4;
    public bool   EnableAutoBraceCompletion { get; set; } = true;

    // ── Files & Session ──────────────────────────────────────────────────────
    public bool RememberOpenFiles   { get; set; } = true;
    public int  MaxRecentFiles      { get; set; } = 10;
    public long LargeFileThresholdBytes { get; set; } = 32L * 1024 * 1024; // 32 MB
    public bool AutoSave            { get; set; } = false;
    public int  AutoSaveIntervalSeconds { get; set; } = 30;
    public string DefaultEncoding   { get; set; } = "UTF-8";
    public string DefaultLineEnding { get; set; } = "CRLF";

    // ── Terminal ─────────────────────────────────────────────────────────────
    public string TerminalShellPath { get; set; } = "powershell.exe";
    public string TerminalFontFamily { get; set; } = "Consolas";
    public double TerminalFontSize  { get; set; } = 12;

    // ── Recent / session ─────────────────────────────────────────────────────
    public List<string> RecentFiles    { get; set; } = new();
    public List<string> LastOpenFiles  { get; set; } = new();
}

/// <summary>Persists settings and the recent-file list to %APPDATA%\HyperNote\settings.json.</summary>
public sealed class SettingsService
{
    public static SettingsService Instance { get; } = new();

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public AppSettings Settings { get; private set; }

    private SettingsService()
    {
        Settings = Load();
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* corrupt settings -> start fresh */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }

    public void AddRecentFile(string path)
    {
        path = Path.GetFullPath(path);
        Settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Settings.RecentFiles.Insert(0, path);
        int max = Settings.MaxRecentFiles;
        if (Settings.RecentFiles.Count > max)
            Settings.RecentFiles.RemoveRange(max, Settings.RecentFiles.Count - max);
        Save();
    }
}
