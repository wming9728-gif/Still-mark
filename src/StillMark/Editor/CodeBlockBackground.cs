using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using StillMark.Core.Markdown;

namespace StillMark.Editor;

/// <summary>
/// Full-width band behind fenced/indented code blocks in the source editor. The ranges come from the same
/// Markdig parse that drives the preview, so the editor and the preview always agree on what is code.
/// </summary>
public sealed class CodeBlockBackground : IBackgroundRenderer
{
    private readonly TextView _view;
    private IReadOnlyList<LineRange> _ranges = [];

    public Brush Fill { get; set; } = Brushes.Transparent;

    public CodeBlockBackground(TextView view) { _view = view; }

    public KnownLayer Layer => KnownLayer.Background;

    public void SetRanges(IReadOnlyList<LineRange> ranges)
    {
        if (ranges.SequenceEqual(_ranges)) return;
        _ranges = ranges;
        _view.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (_ranges.Count == 0 || !textView.VisualLinesValid) return;
        double width = textView.ActualWidth + textView.HorizontalOffset;
        foreach (var vl in textView.VisualLines)
        {
            int line0 = vl.FirstDocumentLine.LineNumber - 1;
            if (!Contains(line0)) continue;
            double top = vl.VisualTop - textView.ScrollOffset.Y;
            dc.DrawRectangle(Fill, null, new Rect(0, top, width, vl.Height));
        }
    }

    private bool Contains(int line0)
    {
        foreach (var r in _ranges)
        {
            if (line0 < r.Start) return false; // ranges are in document order
            if (line0 <= r.End) return true;
        }
        return false;
    }
}
