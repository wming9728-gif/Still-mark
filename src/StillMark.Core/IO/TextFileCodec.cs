using System.Text;

namespace StillMark.Core.IO;

/// <summary>Result of decoding a file's bytes.</summary>
public sealed class DecodedText
{
    public required string Text { get; init; }
    public required TextFormat Format { get; init; }

    /// <summary>True when re-encoding <see cref="Text"/> with <see cref="Format"/> reproduces the original bytes exactly.</summary>
    public required bool RoundTrips { get; init; }

    /// <summary>Human-readable reason when the file cannot be saved back safely.</summary>
    public string? Problem { get; init; }

    public bool LooksBinary { get; init; }
}

public static class TextFileCodec
{
    /// <summary>Files above this size are refused: the editor and preview are not designed for them.</summary>
    public const long MaxFileSize = 64L * 1024 * 1024;

    public static DecodedText Decode(byte[] bytes)
    {
        // 1. BOMs — UTF-32 BOMs are rejected explicitly (FF FE 00 00 would otherwise look like UTF-16 LE).
        if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00) || StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
            return Unsupported(bytes, CoreText.T("Codec_Utf32"));
        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
            return TryStrict(bytes, 3, new TextFormat(TextEncodingKind.Utf8, true))
                   ?? Unsupported(bytes, CoreText.T("Codec_BadUtf8Bom"));
        if (StartsWith(bytes, 0xFF, 0xFE))
            return TryStrict(bytes, 2, new TextFormat(TextEncodingKind.Utf16LE, true))
                   ?? Unsupported(bytes, CoreText.T("Codec_BadUtf16"));
        if (StartsWith(bytes, 0xFE, 0xFF))
            return TryStrict(bytes, 2, new TextFormat(TextEncodingKind.Utf16BE, true))
                   ?? Unsupported(bytes, CoreText.T("Codec_BadUtf16"));

        bool binary = LooksBinary(bytes);

        // 2. No BOM: strict UTF-8 first (also covers pure ASCII).
        var utf8 = TryStrict(bytes, 0, TextFormat.Utf8NoBom);
        if (utf8 != null && !binary) return utf8;

        // 3. Legacy Chinese encoding. Only accepted when decoding is strict and round-trips byte for byte.
        if (!binary)
        {
            var gb = TryStrict(bytes, 0, new TextFormat(TextEncodingKind.Gb18030, false));
            if (gb != null) return gb;
        }

        return Unsupported(bytes, binary ? CoreText.T("Codec_Binary") : CoreText.T("Codec_UnknownEncoding"));
    }

    private static DecodedText? TryStrict(byte[] bytes, int offset, TextFormat format)
    {
        string text;
        try
        {
            text = format.GetStrictEncoding().GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }

        bool roundTrips;
        try
        {
            roundTrips = format.Encode(text).AsSpan().SequenceEqual(bytes);
        }
        catch (EncoderFallbackException)
        {
            roundTrips = false;
        }

        if (!roundTrips && format.Encoding != TextEncodingKind.Utf8)
            return null; // legacy decodes that do not round-trip are not trusted at all

        return new DecodedText
        {
            Text = text,
            Format = format,
            RoundTrips = roundTrips,
            Problem = roundTrips ? null : CoreText.T("Codec_NoRoundTrip"),
        };
    }

    private static DecodedText Unsupported(byte[] bytes, string problem)
    {
        // Show something readable, but never allow saving over the original.
        var text = new UTF8Encoding(false, false).GetString(bytes);
        return new DecodedText
        {
            Text = text,
            Format = TextFormat.Utf8NoBom,
            RoundTrips = false,
            Problem = problem,
            LooksBinary = LooksBinary(bytes),
        };
    }

    private static bool StartsWith(byte[] b, params byte[] prefix)
    {
        if (b.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
            if (b[i] != prefix[i]) return false;
        return true;
    }

    /// <summary>NUL bytes in the first 8 KB of a BOM-less file are a strong binary signal.</summary>
    private static bool LooksBinary(byte[] bytes)
    {
        int n = Math.Min(bytes.Length, 8192);
        for (int i = 0; i < n; i++)
            if (bytes[i] == 0) return true;
        return false;
    }
}
