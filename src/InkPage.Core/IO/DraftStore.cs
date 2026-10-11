using System.Text;
using System.Text.Json;

namespace InkPage.Core.IO;

public sealed class DraftInfo
{
    public string Id { get; set; } = "";
    public string? OriginalPath { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public int Length { get; set; }
    public string Preview { get; set; } = "";

    /// <summary>SHA-256 of the original file when the editing session started (to warn about later disk changes).</summary>
    public string? BaseSha256 { get; set; }
}

/// <summary>
/// Crash/failed-save protection: periodically stores unsaved text in a private folder.
/// Drafts are never written over user files; they are only offered back on the next start.
/// </summary>
public sealed class DraftStore
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _dir;

    public DraftStore(string directory)
    {
        _dir = directory;
    }

    public string Directory => _dir;

    /// <summary>Held open by a running window so other instances do not offer its live draft for recovery.</summary>
    public FileStream AcquireLock(string id)
    {
        System.IO.Directory.CreateDirectory(_dir);
        return new FileStream(Path.Combine(_dir, id + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None, 1, FileOptions.DeleteOnClose);
    }

    public bool IsLocked(string id)
    {
        var path = Path.Combine(_dir, id + ".lock");
        if (!File.Exists(path)) return false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public void Save(string id, string? originalPath, string text, string? baseSha256 = null)
    {
        System.IO.Directory.CreateDirectory(_dir);
        var info = new DraftInfo
        {
            Id = id,
            OriginalPath = originalPath,
            SavedAtUtc = DateTime.UtcNow,
            Length = text.Length,
            Preview = MakePreview(text),
            BaseSha256 = baseSha256,
        };
        // Text first, then the metadata that makes it visible: a half-written draft is never listed.
        SafeFileWriter.Write(Path.Combine(_dir, id + ".md"), Utf8.GetBytes(text));
        SafeFileWriter.Write(Path.Combine(_dir, id + ".json"), JsonSerializer.SerializeToUtf8Bytes(info));
    }

    public void Delete(string id)
    {
        foreach (var ext in new[] { ".json", ".md", ".lock" })
        {
            try { File.Delete(Path.Combine(_dir, id + ext)); } catch { /* best effort */ }
        }
    }

    public IReadOnlyList<DraftInfo> List()
    {
        var list = new List<DraftInfo>();
        if (!System.IO.Directory.Exists(_dir)) return list;
        foreach (var json in System.IO.Directory.GetFiles(_dir, "*.json"))
        {
            try
            {
                var info = JsonSerializer.Deserialize<DraftInfo>(File.ReadAllBytes(json));
                if (info != null && File.Exists(Path.Combine(_dir, info.Id + ".md")))
                    list.Add(info);
            }
            catch { /* ignore corrupt metadata */ }
        }
        return list.OrderByDescending(d => d.SavedAtUtc).ToList();
    }

    public string ReadText(string id) => Utf8.GetString(File.ReadAllBytes(Path.Combine(_dir, id + ".md")));

    private static string MakePreview(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('#', '>', '-', '*', ' ');
            if (line.Length > 0) return line.Length > 60 ? line[..60] + "…" : line;
        }
        return CoreText.T("Draft_Blank");
    }
}
