using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace StillMark.Editor;

/// <summary>
/// Markdown editing helpers. Every operation is a single undo step and touches only the lines/selection it acts on;
/// new line breaks always reuse the line ending already used by the document.
/// </summary>
public static class MarkdownCommands
{
    private static readonly Regex ListLine = new(@"^(?<indent>[ \t]*)(?<marker>[-*+]|(?<num>\d{1,9})(?<delim>[.)]))(?<space>[ \t]+)(?<task>\[[ xX]\][ \t]+)?", RegexOptions.Compiled);
    private static readonly Regex QuoteLine = new(@"^(?<prefix>[ \t]*(>[ \t]?)+)", RegexOptions.Compiled);
    private static readonly Regex HeadingPrefix = new(@"^[ ]{0,3}#{1,6}[ \t]+", RegexOptions.Compiled);

    /// <summary>Line break to use at a given line: that line's own, else the previous one's, else the document default.</summary>
    public static string NewLineAt(TextDocument doc, int lineNumber, string fallback)
    {
        var line = doc.GetLineByNumber(lineNumber);
        if (line.DelimiterLength > 0) return doc.GetText(line.EndOffset, line.DelimiterLength);
        if (line.PreviousLine is { DelimiterLength: > 0 } prev) return doc.GetText(prev.EndOffset, prev.DelimiterLength);
        return fallback;
    }

    /// <summary>Enter key: continues lists and quotes, keeps indentation. Returns false to let the editor handle it.</summary>
    public static bool SmartEnter(TextEditor ed, string fallbackNewLine, bool plain)
    {
        var doc = ed.Document;
        var area = ed.TextArea;
        if (area.Selection.IsMultiline && !area.Selection.IsEmpty && area.Selection.Segments.Count() > 1) return false; // rectangular

        int selStart = ed.SelectionStart, selLen = ed.SelectionLength;
        var line = doc.GetLineByOffset(selStart);
        string nl = NewLineAt(doc, line.LineNumber, fallbackNewLine);
        string before = doc.GetText(line.Offset, selStart - line.Offset);
        string insert;

        if (!plain && ListLine.Match(before) is { Success: true } m)
        {
            string lineText = doc.GetText(line);
            bool caretAtEnd = selStart + selLen >= line.EndOffset;
            if (caretAtEnd && lineText.Trim() == m.Value.Trim())
            {
                // Empty list item: end the list instead of adding another empty marker.
                doc.Replace(line.Offset, line.Length, m.Groups["indent"].Value);
                ed.CaretOffset = line.Offset + m.Groups["indent"].Length;
                return true;
            }
            string marker = m.Groups["marker"].Value;
            if (m.Groups["num"].Success && long.TryParse(m.Groups["num"].Value, out var n))
                marker = (n + 1).ToString() + m.Groups["delim"].Value;
            string task = m.Groups["task"].Success ? "[ ] " : "";
            insert = nl + m.Groups["indent"].Value + marker + m.Groups["space"].Value + task;
        }
        else if (!plain && QuoteLine.Match(before) is { Success: true } q)
        {
            string lineText = doc.GetText(line);
            if (lineText.Trim().Trim('>').Trim().Length == 0 && selStart + selLen >= line.EndOffset)
            {
                doc.Replace(line.Offset, line.Length, "");
                ed.CaretOffset = line.Offset;
                return true;
            }
            insert = nl + q.Groups["prefix"].Value;
        }
        else
        {
            // keep indentation (spaces/tabs exactly as typed)
            int i = 0;
            while (i < before.Length && (before[i] == ' ' || before[i] == '\t')) i++;
            insert = nl + before[..i];
        }

        doc.Replace(selStart, selLen, insert);
        ed.CaretOffset = selStart + insert.Length;
        ed.TextArea.Caret.BringCaretToView();
        return true;
    }

    /// <summary>Wraps (or unwraps) the selection with inline markers such as ** or `.</summary>
    public static void ToggleWrap(TextEditor ed, string prefix, string suffix, string placeholder = "")
    {
        var doc = ed.Document;
        int start = ed.SelectionStart, len = ed.SelectionLength;
        string sel = doc.GetText(start, len);

        // Already wrapped inside the selection?
        if (sel.Length >= prefix.Length + suffix.Length && sel.StartsWith(prefix, StringComparison.Ordinal) && sel.EndsWith(suffix, StringComparison.Ordinal))
        {
            var inner = sel.Substring(prefix.Length, sel.Length - prefix.Length - suffix.Length);
            doc.Replace(start, len, inner);
            ed.Select(start, inner.Length);
            return;
        }
        // Wrapped just outside the selection?
        if (start >= prefix.Length && start + len + suffix.Length <= doc.TextLength &&
            doc.GetText(start - prefix.Length, prefix.Length) == prefix &&
            doc.GetText(start + len, suffix.Length) == suffix)
        {
            doc.BeginUpdate();
            doc.Remove(start + len, suffix.Length);
            doc.Remove(start - prefix.Length, prefix.Length);
            doc.EndUpdate();
            ed.Select(start - prefix.Length, len);
            return;
        }

        string body = len == 0 ? placeholder : sel;
        doc.Replace(start, len, prefix + body + suffix);
        ed.Select(start + prefix.Length, body.Length);
    }

    /// <summary>Sets the heading level of the lines in the selection (same level again removes it).</summary>
    public static void SetHeading(TextEditor ed, int level)
    {
        var doc = ed.Document;
        var first = doc.GetLineByOffset(ed.SelectionStart);
        var last = doc.GetLineByOffset(ed.SelectionStart + ed.SelectionLength);
        string want = new string('#', level) + " ";
        doc.BeginUpdate();
        try
        {
            for (var line = last; line != null && line.LineNumber >= first.LineNumber; line = line.PreviousLine)
            {
                string text = doc.GetText(line);
                var m = HeadingPrefix.Match(text);
                if (m.Success)
                {
                    bool same = m.Value.TrimEnd() == want.TrimEnd();
                    doc.Replace(line.Offset, m.Length, same ? "" : want);
                }
                else if (text.Trim().Length > 0 || first == last)
                {
                    doc.Insert(line.Offset, want);
                }
            }
        }
        finally { doc.EndUpdate(); }
    }

    /// <summary>Toggles a line prefix ("- ", "> ", "1. ", "- [ ] ") on every selected line.</summary>
    public static void ToggleLinePrefix(TextEditor ed, string prefix, bool numbered = false)
    {
        var doc = ed.Document;
        var first = doc.GetLineByOffset(ed.SelectionStart);
        var last = doc.GetLineByOffset(ed.SelectionStart + ed.SelectionLength);
        if (last != first && ed.SelectionStart + ed.SelectionLength == last.Offset) last = last.PreviousLine;

        var lines = new List<DocumentLine>();
        for (var l = first; l != null && l.LineNumber <= last.LineNumber; l = l.NextLine) lines.Add(l);
        var nonEmpty = lines.Where(l => doc.GetText(l).Trim().Length > 0).ToList();
        if (nonEmpty.Count == 0) nonEmpty = lines;

        Regex has = numbered ? new Regex(@"^([ \t]*)\d{1,9}[.)][ \t]+") : new Regex("^([ \t]*)" + Regex.Escape(prefix));
        bool allHave = nonEmpty.All(l => has.IsMatch(doc.GetText(l)));

        doc.BeginUpdate();
        try
        {
            int n = nonEmpty.Count;
            for (int i = nonEmpty.Count - 1; i >= 0; i--)
            {
                var l = nonEmpty[i];
                string text = doc.GetText(l);
                if (allHave)
                {
                    var m = has.Match(text);
                    doc.Replace(l.Offset + m.Groups[1].Length, m.Length - m.Groups[1].Length, "");
                }
                else
                {
                    int indent = text.Length - text.TrimStart(' ', '\t').Length;
                    doc.Insert(l.Offset + indent, numbered ? $"{i + 1}. " : prefix);
                }
            }
        }
        finally { doc.EndUpdate(); }
    }

    /// <summary>Inserts a block (table, rule, fenced code) on its own lines at the caret.</summary>
    public static void InsertBlock(TextEditor ed, string[] blockLines, string fallbackNewLine, int caretLine = 0, int caretColumn = 0)
    {
        var doc = ed.Document;
        var line = doc.GetLineByOffset(ed.SelectionStart);
        string nl = NewLineAt(doc, line.LineNumber, fallbackNewLine);
        string sel = doc.GetText(ed.SelectionStart, ed.SelectionLength);
        string currentLine = doc.GetText(line);

        string body = string.Join(nl, blockLines);
        bool lineEmpty = currentLine.Trim().Length == 0;
        int insertAt;
        string prefix;
        if (lineEmpty)
        {
            insertAt = line.Offset;
            doc.Replace(line.Offset, line.Length, "");
            prefix = line.PreviousLine != null && doc.GetText(line.PreviousLine).Trim().Length > 0 ? nl : "";
        }
        else
        {
            insertAt = line.EndOffset;
            prefix = nl + nl;
        }
        string text = prefix + body + (line.NextLine == null || !lineEmpty ? nl : "");
        doc.Insert(insertAt, text);

        int caret = insertAt + prefix.Length;
        for (int i = 0; i < caretLine && i < blockLines.Length; i++) caret += blockLines[i].Length + nl.Length;
        ed.CaretOffset = Math.Min(doc.TextLength, caret + caretColumn);
        _ = sel;
    }

    public static void InsertFencedCode(TextEditor ed, string fallbackNewLine)
    {
        var doc = ed.Document;
        if (ed.SelectionLength > 0)
        {
            var first = doc.GetLineByOffset(ed.SelectionStart);
            var last = doc.GetLineByOffset(ed.SelectionStart + ed.SelectionLength);
            string nl = NewLineAt(doc, first.LineNumber, fallbackNewLine);
            doc.BeginUpdate();
            doc.Insert(last.EndOffset, nl + "```");
            doc.Insert(first.Offset, "```" + nl);
            doc.EndUpdate();
            ed.CaretOffset = first.Offset + 3;
            return;
        }
        InsertBlock(ed, ["```", "", "```"], fallbackNewLine, 0, 3);
    }

    public static void InsertLink(TextEditor ed, bool image, string? url = null)
    {
        var doc = ed.Document;
        int start = ed.SelectionStart, len = ed.SelectionLength;
        string text = len > 0 ? doc.GetText(start, len) : Localization.Loc.T(image ? "Ph_ImageAlt" : "Ph_LinkText");
        string u = url ?? "https://";
        string s = (image ? "!" : "") + "[" + text + "](" + u + ")";
        doc.Replace(start, len, s);
        if (url == null)
        {
            int urlStart = start + (image ? 1 : 0) + text.Length + 3;
            ed.Select(urlStart, u.Length);
        }
        else ed.CaretOffset = start + s.Length;
    }
}
