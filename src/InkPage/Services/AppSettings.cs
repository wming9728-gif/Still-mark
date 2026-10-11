using System.IO;
using System.Text.Json;
using InkPage.Core.IO;
using InkPage.Localization;

namespace InkPage.Services;

public enum ViewMode { Read, Split, Edit }

public sealed class AppSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public LanguageChoice Language { get; set; } = LanguageChoice.System;
    public ViewMode Mode { get; set; } = ViewMode.Split;
    public bool OutlineVisible { get; set; } = true;
    public bool WordWrap { get; set; } = true;
    public bool LineNumbers { get; set; } = true;
    public bool ShowWhitespace { get; set; }
    public bool CodeLigatures { get; set; }
    public double EditorFontSize { get; set; } = 14.5;
    public double PreviewZoom { get; set; } = 1.0;
    public double SplitRatio { get; set; } = 0.5;
    public List<string> RecentFiles { get; set; } = new();
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 820;
    public bool WindowMaximized { get; set; }

    private static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(FilePath), Json) ?? new AppSettings();
        }
        catch
        {
            // a corrupt settings file must never prevent the app from starting
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            // Re-read recent files written by other windows so they are not lost.
            Directory.CreateDirectory(AppPaths.DataDir);
            SafeFileWriter.Write(FilePath, JsonSerializer.SerializeToUtf8Bytes(this, Json));
        }
        catch
        {
            // settings are a convenience; ignore failures
        }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 12) RecentFiles.RemoveRange(12, RecentFiles.Count - 12);
    }
}

/// <summary>
/// Portable layout: settings, drafts and the WebView2 profile live in "data" next to the executable when that
/// folder is writable; otherwise under %LOCALAPPDATA%\InkPage.
/// </summary>
public static class AppPaths
{
    public static string AppDir { get; } = AppContext.BaseDirectory;
    public static string WebDir => Path.Combine(AppDir, "web");

    private static string? _dataDir;
    public static string DataDir => _dataDir ??= ResolveDataDir();

    public static string DraftsDir => Path.Combine(DataDir, "recovery");
    public static string WebViewDir => Path.Combine(DataDir, "webview2");

    private static string ResolveDataDir()
    {
        var portable = Path.Combine(AppDir, "data");
        try
        {
            Directory.CreateDirectory(portable);
            var probe = Path.Combine(portable, ".write-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return portable;
        }
        catch
        {
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InkPage");
            Directory.CreateDirectory(local);
            return local;
        }
    }
}
