using System.Text;

namespace StillMark.Core.IO;

/// <summary>The byte-level encoding of a text file, as detected on load and reused on save.</summary>
public enum TextEncodingKind
{
    Utf8,
    Utf16LE,
    Utf16BE,
    Gb18030,
}

/// <summary>Line ending style found in a text.</summary>
public enum LineEndingStyle
{
    None,   // no line breaks at all
    LF,
    CRLF,
    CR,
    Mixed,
}

/// <summary>Everything needed to write a text back in exactly the byte format it was read in.</summary>
public sealed record TextFormat(TextEncodingKind Encoding, bool HasBom)
{
    public static readonly TextFormat Utf8NoBom = new(TextEncodingKind.Utf8, false);

    static TextFormat()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Strict encoder/decoder: throws instead of substituting replacement characters.</summary>
    public Encoding GetStrictEncoding() => Encoding switch
    {
        TextEncodingKind.Utf8 => new UTF8Encoding(false, true),
        TextEncodingKind.Utf16LE => new UnicodeEncoding(false, false, true),
        TextEncodingKind.Utf16BE => new UnicodeEncoding(true, false, true),
        TextEncodingKind.Gb18030 => System.Text.Encoding.GetEncoding(54936,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => throw new ArgumentOutOfRangeException(),
    };

    public byte[] GetPreamble() => !HasBom ? [] : Encoding switch
    {
        TextEncodingKind.Utf8 => [0xEF, 0xBB, 0xBF],
        TextEncodingKind.Utf16LE => [0xFF, 0xFE],
        TextEncodingKind.Utf16BE => [0xFE, 0xFF],
        _ => [],
    };

    public string DisplayName => Encoding switch
    {
        TextEncodingKind.Utf8 => HasBom ? "UTF-8 BOM" : "UTF-8",
        TextEncodingKind.Utf16LE => "UTF-16 LE",
        TextEncodingKind.Utf16BE => "UTF-16 BE",
        TextEncodingKind.Gb18030 => "GB18030",
        _ => Encoding.ToString(),
    };

    /// <summary>Encodes text to the exact bytes to be written (preamble included). Throws if a character cannot be represented.</summary>
    public byte[] Encode(string text)
    {
        var pre = GetPreamble();
        var body = GetStrictEncoding().GetBytes(text);
        if (pre.Length == 0) return body;
        var all = new byte[pre.Length + body.Length];
        pre.CopyTo(all, 0);
        body.CopyTo(all, pre.Length);
        return all;
    }
}

public static class LineEndings
{
    public static (int crlf, int lf, int cr) Count(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                else cr++;
            }
            else if (c == '\n') lf++;
        }
        return (crlf, lf, cr);
    }

    public static LineEndingStyle Detect(string text)
    {
        var (crlf, lf, cr) = Count(text);
        int kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        if (kinds == 0) return LineEndingStyle.None;
        if (kinds > 1) return LineEndingStyle.Mixed;
        return crlf > 0 ? LineEndingStyle.CRLF : lf > 0 ? LineEndingStyle.LF : LineEndingStyle.CR;
    }

    /// <summary>The newline that should be used for newly inserted lines: the most frequent one, LF if none.</summary>
    public static string Dominant(string text)
    {
        var (crlf, lf, cr) = Count(text);
        if (crlf == 0 && lf == 0 && cr == 0) return "\n";
        if (crlf >= lf && crlf >= cr) return "\r\n";
        if (lf >= cr) return "\n";
        return "\r";
    }

    /// <summary>Converts every line break to <paramref name="newLine"/>. Only used on explicit user request.</summary>
    public static string Normalize(string text, string newLine)
    {
        var sb = new StringBuilder(text.Length + 16);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                sb.Append(newLine);
            }
            else if (c == '\n') sb.Append(newLine);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public static string Label(LineEndingStyle s) => s switch
    {
        LineEndingStyle.LF => "LF",
        LineEndingStyle.CRLF => "CRLF",
        LineEndingStyle.CR => "CR",
        LineEndingStyle.Mixed => CoreText.T("Eol_Mixed"),
        _ => "LF",
    };
}
