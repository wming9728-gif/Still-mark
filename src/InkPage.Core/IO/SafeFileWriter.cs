using System.Security.Cryptography;

namespace InkPage.Core.IO;

/// <summary>Identity of a file's on-disk content at a point in time; used to detect external modification.</summary>
public sealed record FileFingerprint(long Length, DateTime LastWriteUtc, string Sha256)
{
    public static FileFingerprint FromBytes(byte[] bytes, DateTime lastWriteUtc) =>
        new(bytes.LongLength, lastWriteUtc, Convert.ToHexString(SHA256.HashData(bytes)));

    /// <summary>Reads the file and fingerprints it; null if it does not exist.</summary>
    public static FileFingerprint? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var bytes = ReadAllBytesShared(path);
        return FromBytes(bytes, File.GetLastWriteTimeUtc(path));
    }

    /// <summary>Same content? Timestamps are ignored on purpose: touching a file is not a change.</summary>
    public bool SameContent(FileFingerprint? other) =>
        other != null && other.Length == Length && other.Sha256 == Sha256;

    /// <summary>Reads a file while tolerating other processes that hold it open for writing.</summary>
    public static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > TextFileCodec.MaxFileSize)
            throw CoreText.Mark(new IOException(CoreText.F("Write_TooLarge", fs.Length / 1024 / 1024, TextFileCodec.MaxFileSize / 1024 / 1024)));
        var buffer = new byte[fs.Length];
        int read = 0;
        while (read < buffer.Length)
        {
            int n = fs.Read(buffer, read, buffer.Length - read);
            if (n == 0) break;
            read += n;
        }
        return read == buffer.Length ? buffer : buffer[..read];
    }
}

/// <summary>
/// Writes files atomically: content goes to a temporary file in the same directory, is flushed to disk and
/// verified, and only then replaces the target. If anything fails the original file is left untouched.
/// </summary>
public static class SafeFileWriter
{
    /// <summary>Test hook: invoked after the temp file is written, before it replaces the target.</summary>
    public static Action<string>? BeforeReplaceHook { get; set; }

    public static void Write(string path, byte[] bytes)
    {
        path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(path) ?? throw CoreText.Mark(new IOException(CoreText.T("Write_InvalidPath")));
        if (!Directory.Exists(dir))
            throw CoreText.Mark(new DirectoryNotFoundException(CoreText.F("Write_DirNotFound", dir)));

        bool targetExists = File.Exists(path);
        if (targetExists && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            throw CoreText.Mark(new UnauthorizedAccessException(CoreText.T("Write_ReadOnly")));

        var temp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid().ToString("N")[..8]}.inkpage-tmp");
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            var check = File.ReadAllBytes(temp);
            if (!check.AsSpan().SequenceEqual(bytes))
                throw CoreText.Mark(new IOException(CoreText.T("Write_TempVerifyFailed")));

            BeforeReplaceHook?.Invoke(temp);

            if (targetExists)
            {
                try
                {
                    // ReplaceFile keeps the original's attributes, ACLs and creation time.
                    File.Replace(temp, path, null, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temp, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temp, path, overwrite: false);
            }
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }

        // Final verification of what actually landed on disk.
        var written = FileFingerprint.ReadAllBytesShared(path);
        if (!written.AsSpan().SequenceEqual(bytes))
            throw CoreText.Mark(new IOException(CoreText.T("Write_VerifyFailed")));
    }
}
