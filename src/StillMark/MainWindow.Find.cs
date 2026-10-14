using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StillMark.Localization;
using StillMark.Services;

namespace StillMark;

/// <summary>Find / replace. Reading mode searches the rendered page; edit and split modes search the source.</summary>
public partial class MainWindow
{
    private DispatcherTimer? _findTimer;
    private DispatcherTimer? _editorFindRefresh;

    private bool FindTargetsEditor => _mode != ViewMode.Read;

    private void Find_Click(object sender, RoutedEventArgs e) => ShowFind(replace: false);

    private void ShowFind(bool replace)
    {
        FindBar.Visibility = Visibility.Visible;
        ReplaceToggle.IsEnabled = FindTargetsEditor;
        ReplaceToggle.IsChecked = replace && FindTargetsEditor;
        if (FindTargetsEditor && Editor.SelectionLength is > 0 and < 200 && !Editor.SelectedText.Contains('\n'))
            FindBox.Text = Editor.SelectedText;
        FindBox.Focus();
        FindBox.SelectAll();
        RefreshFind(keepPosition: true);
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        ClearEditorFind();
        PostToPreview(new { type = "findClear" });
        if (FindTargetsEditor) Editor.TextArea.Focus();
    }

    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFind();
    private void FindNext_Click(object sender, RoutedEventArgs e) => FindStep(1);
    private void FindPrev_Click(object sender, RoutedEventArgs e) => FindStep(-1);
    private void CaseToggle_Changed(object sender, RoutedEventArgs e) => RefreshFind(keepPosition: true);

    private void ReplaceToggle_Changed(object sender, RoutedEventArgs e) =>
        ReplaceRow.Visibility = ReplaceToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _findTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) =>
        {
            _findTimer!.Stop();
            RefreshFind(keepPosition: false);
        }, Dispatcher);
        _findTimer.Stop();
        _findTimer.Start();
    }

    private void FindBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindStep(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
        }
    }

    private void ReplaceBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ReplaceAll_Click(sender, e);
            else ReplaceOne_Click(sender, e);
            e.Handled = true;
        }
    }

    private int _findCount, _findIndex;

    private void SetFindCount(int count, int index)
    {
        _findCount = count;
        _findIndex = index;
        UpdateFindCountText();
    }

    private void UpdateFindCountText()
    {
        FindCount.Text = string.IsNullOrEmpty(FindBox.Text) ? "" : _findCount == 0 ? Loc.T("Find_NoResults") : $"{(_findIndex >= 0 ? _findIndex + 1 : 0)} / {_findCount}";
    }

    private void RefreshFind(bool keepPosition)
    {
        if (FindBar.Visibility != Visibility.Visible) return;
        ReplaceToggle.IsEnabled = FindTargetsEditor;
        if (!FindTargetsEditor) ReplaceToggle.IsChecked = false;
        if (FindTargetsEditor)
        {
            PostToPreview(new { type = "findClear" });
            ComputeEditorMatches();
            if (!keepPosition) SelectMatchFrom(Editor.SelectionStart, 1, includeCurrent: true);
            else UpdateEditorFindCount();
        }
        else
        {
            ClearEditorFind();
            PostToPreview(new { type = "find", query = FindBox.Text, caseSensitive = CaseToggle.IsChecked == true, direction = 0 });
        }
    }

    private void FindStep(int direction)
    {
        if (FindBar.Visibility != Visibility.Visible) { ShowFind(false); return; }
        if (FindTargetsEditor)
        {
            if (_search.Matches.Count == 0) ComputeEditorMatches();
            int from = direction > 0 ? Editor.SelectionStart + Math.Max(1, Editor.SelectionLength) : Editor.SelectionStart;
            SelectMatchFrom(from, direction, includeCurrent: false);
        }
        else
        {
            PostToPreview(new { type = "find", query = FindBox.Text, caseSensitive = CaseToggle.IsChecked == true, direction });
        }
    }

    // ------------------------------------------------------------------ editor search

    private StringComparison FindComparison => CaseToggle.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private void ComputeEditorMatches()
    {
        _search.Matches.Clear();
        _search.Current = -1;
        string q = FindBox.Text;
        if (q.Length > 0)
        {
            string text = Editor.Document.Text;
            var cmp = FindComparison;
            for (int i = text.IndexOf(q, cmp); i >= 0 && _search.Matches.Count < 20000; i = text.IndexOf(q, i + q.Length, cmp))
                _search.Matches.Add((i, q.Length));
        }
        _search.Invalidate();
        UpdateEditorFindCount();
    }

    private void ScheduleEditorFindRefresh()
    {
        _editorFindRefresh ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            _editorFindRefresh!.Stop();
            ComputeEditorMatches();
        }, Dispatcher);
        _editorFindRefresh.Stop();
        _editorFindRefresh.Start();
    }

    private void SelectMatchFrom(int offset, int direction, bool includeCurrent)
    {
        var m = _search.Matches;
        if (m.Count == 0) { UpdateEditorFindCount(); return; }
        int idx;
        if (direction > 0)
        {
            idx = m.FindIndex(x => includeCurrent ? x.Offset >= offset : x.Offset >= offset);
            if (idx < 0) idx = 0;
        }
        else
        {
            idx = m.FindLastIndex(x => x.Offset < offset);
            if (idx < 0) idx = m.Count - 1;
        }
        _search.Current = idx;
        var (o, l) = m[idx];
        Editor.Select(o, l);
        var loc = Editor.Document.GetLocation(o);
        Editor.ScrollTo(loc.Line, loc.Column);
        _search.Invalidate();
        UpdateEditorFindCount();
    }

    private void UpdateEditorFindCount()
    {
        if (_search.Current < 0 && _search.Matches.Count > 0)
        {
            int sel = Editor.SelectionStart;
            int i = _search.Matches.FindIndex(x => x.Offset == sel && x.Length == Editor.SelectionLength);
            if (i >= 0) _search.Current = i;
        }
        SetFindCount(_search.Matches.Count, _search.Current);
    }

    private void ClearEditorFind()
    {
        if (_search == null) return;
        _search.Matches.Clear();
        _search.Current = -1;
        _search.Invalidate();
    }

    private void ReplaceOne_Click(object sender, RoutedEventArgs e)
    {
        if (!FindTargetsEditor || Editor.IsReadOnly || FindBox.Text.Length == 0) return;
        string q = FindBox.Text;
        if (Editor.SelectionLength == q.Length && string.Equals(Editor.SelectedText, q, FindComparison))
        {
            int at = Editor.SelectionStart;
            Editor.Document.Replace(at, q.Length, ReplaceBox.Text);
            Editor.CaretOffset = at + ReplaceBox.Text.Length;
            ComputeEditorMatches();
            SelectMatchFrom(Editor.CaretOffset, 1, includeCurrent: true);
        }
        else FindStep(1);
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        if (!FindTargetsEditor || Editor.IsReadOnly || FindBox.Text.Length == 0) return;
        ComputeEditorMatches();
        var matches = _search.Matches.ToList();
        if (matches.Count == 0) return;
        string rep = ReplaceBox.Text;
        var doc = Editor.Document;
        doc.BeginUpdate(); // one undo step
        try
        {
            for (int i = matches.Count - 1; i >= 0; i--) doc.Replace(matches[i].Offset, matches[i].Length, rep);
        }
        finally { doc.EndUpdate(); }
        ComputeEditorMatches();
        ShowStatus(Loc.F("Status_Replaced", matches.Count));
    }
}
