namespace InkPage.Core.IO;

public enum SaveOutcome
{
    /// <summary>Bytes were written and verified.</summary>
    Saved,
    /// <summary>Nothing to write: the text and the disk content are already identical. The file was not touched.</summary>
    Unchanged,
    /// <summary>The file on disk changed (or vanished) since it was loaded/saved; caller must confirm overwriting.</summary>
    Conflict,
}

public enum DiskState
{
    Unchanged,
    Modified,
    Deleted,
}

/// <summary>
/// The file-side state of one open document: where it lives, how its bytes are encoded and what is on disk.
/// Holds no UI state. All writes go through <see cref="SafeFileWriter"/>.
/// </summary>
public sealed class DocumentSession
{
    public string? FilePath { get; private set; }
    public TextFormat Format { get; private set; } = TextFormat.Utf8NoBom;

    /// <summary>The text as last loaded from or saved to disk.</summary>
    public string SavedText { get; private set; } = "";

    /// <summary>Fingerprint of the disk content we know about (null for new, never-saved documents).</summary>
    public FileFingerprint? KnownDisk { get; private set; }

    /// <summary>False when the bytes could not be decoded in a way that re-encodes identically; saving in place is then refused.</summary>
    public bool CanSaveInPlace { get; private set; } = true;
    public string? LoadProblem { get; private set; }

    public bool IsUntitled => FilePath == null;
    public string DisplayName => FilePath == null ? CoreText.T("Doc_Untitled") : Path.GetFileName(FilePath);

    public static DocumentSession CreateNew() => new();

    public static DocumentSession Load(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
            throw CoreText.Mark(new FileNotFoundException(CoreText.F("Doc_FileNotFound", path), path));

        var bytes = FileFingerprint.ReadAllBytesShared(path);
        var decoded = TextFileCodec.Decode(bytes);
        return new DocumentSession
        {
            FilePath = path,
            Format = decoded.Format,
            SavedText = decoded.Text,
            KnownDisk = FileFingerprint.FromBytes(bytes, File.GetLastWriteTimeUtc(path)),
            CanSaveInPlace = decoded.RoundTrips,
            LoadProblem = decoded.Problem,
        };
    }

    /// <summary>The encoding was changed by the user; the next save must write even if the text is unchanged.</summary>
    public bool PendingFormatChange { get; private set; }

    public bool IsDirty(string currentText) =>
        PendingFormatChange || !string.Equals(currentText, SavedText, StringComparison.Ordinal);

    public DiskState CheckDisk(bool thorough = false)
    {
        if (FilePath == null) return DiskState.Unchanged;
        try
        {
            // Cheap pre-check: same size and timestamp as our last read/write means unchanged.
            var fi = new FileInfo(FilePath);
            if (!thorough && fi.Exists && KnownDisk != null && fi.Length == KnownDisk.Length && fi.LastWriteTimeUtc == KnownDisk.LastWriteUtc)
                return DiskState.Unchanged;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        FileFingerprint? now;
        try
        {
            now = FileFingerprint.Read(FilePath);
        }
        catch (IOException)
        {
            return DiskState.Unchanged; // temporarily locked by the writer: check again later
        }
        catch (UnauthorizedAccessException)
        {
            return DiskState.Unchanged;
        }
        if (now == null) return KnownDisk == null ? DiskState.Unchanged : DiskState.Deleted;
        if (KnownDisk == null) return DiskState.Modified;
        return KnownDisk.SameContent(now) ? DiskState.Unchanged : DiskState.Modified;
    }

    /// <summary>Saves to the current path. Never overwrites an externally modified file unless <paramref name="overwriteExternalChanges"/>.</summary>
    public SaveOutcome Save(string text, bool overwriteExternalChanges = false)
    {
        if (FilePath == null) throw CoreText.Mark(new InvalidOperationException(CoreText.T("Doc_NotSavedYet")));
        if (!CanSaveInPlace)
            throw CoreText.Mark(new InvalidOperationException(CoreText.F("Doc_CantOverwrite", LoadProblem)));

        var disk = CheckDisk(thorough: true);
        if (disk != DiskState.Unchanged && !overwriteExternalChanges)
            return SaveOutcome.Conflict;

        if (disk == DiskState.Unchanged && !IsDirty(text) && KnownDisk != null && !PendingFormatChange)
            return SaveOutcome.Unchanged; // byte-identical already; do not touch the file at all

        WriteTo(FilePath, text, Format);
        return SaveOutcome.Saved;
    }

    /// <summary>Saves to a new path (the session then points at it). Format defaults to the current one.</summary>
    public void SaveAs(string path, string text, TextFormat? format = null)
    {
        path = Path.GetFullPath(path);
        var fmt = format ?? (CanSaveInPlace ? Format : TextFormat.Utf8NoBom);
        WriteTo(path, text, fmt);
        FilePath = path;
        Format = fmt;
        CanSaveInPlace = true;
        LoadProblem = null;
    }

    /// <summary>Changes the encoding used for the next save (explicit user action).</summary>
    public void ChangeFormat(TextFormat format)
    {
        if (format == Format) return;
        Format = format;
        CanSaveInPlace = true;
        LoadProblem = null;
        PendingFormatChange = true;
    }

    private void WriteTo(string path, string text, TextFormat fmt)
    {
        byte[] bytes;
        try
        {
            bytes = fmt.Encode(text);
        }
        catch (System.Text.EncoderFallbackException)
        {
            throw CoreText.Mark(new InvalidOperationException(CoreText.F("Doc_Unencodable", fmt.DisplayName)));
        }
        SafeFileWriter.Write(path, bytes);
        SavedText = text;
        PendingFormatChange = false;
        KnownDisk = FileFingerprint.FromBytes(bytes, File.GetLastWriteTimeUtc(path));
    }

    /// <summary>Re-reads the current file from disk, discarding the in-memory notion of saved text.</summary>
    public static DocumentSession Reload(DocumentSession s) =>
        s.FilePath == null ? throw new InvalidOperationException() : Load(s.FilePath);
}
