using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace StillMark.Editor;

/// <summary>Paints find matches behind the editor text.</summary>
public sealed class SearchHighlighter : IBackgroundRenderer
{
    private readonly TextView _view;
    public List<(int Offset, int Length)> Matches { get; } = new();
    public int Current { get; set; } = -1;

    public Brush MatchBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x90, 0xFD, 0xE6, 0x8A));
    public Brush CurrentBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0xC0, 0xF5, 0x9E, 0x0B));

    public SearchHighlighter(TextView view) { _view = view; }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (Matches.Count == 0 || !textView.VisualLinesValid) return;
        var lines = textView.VisualLines;
        if (lines.Count == 0) return;
        int viewStart = lines[0].FirstDocumentLine.Offset;
        int viewEnd = lines[^1].LastDocumentLine.EndOffset;

        // binary search first visible match
        int lo = 0, hi = Matches.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (Matches[mid].Offset + Matches[mid].Length < viewStart) lo = mid + 1; else hi = mid; }

        for (int i = lo; i < Matches.Count && Matches[i].Offset <= viewEnd; i++)
        {
            var seg = new TextSegment { StartOffset = Matches[i].Offset, Length = Matches[i].Length };
            var brush = i == Current ? CurrentBrush : MatchBrush;
            foreach (Rect r in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
                dc.DrawRoundedRectangle(brush, null, new Rect(r.Location, new Size(r.Width, r.Height)), 2, 2);
        }
    }

    public void Invalidate() => _view.InvalidateLayer(Layer);
}
