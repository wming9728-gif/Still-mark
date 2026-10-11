using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using InkPage.Core.IO;
using InkPage.Editor;
using InkPage.Localization;
using InkPage.Services;
using InkPage.Views;

namespace InkPage;

public partial class MainWindow : Window
{
    private const double BaseEditorFont = 14.5;

    private readonly AppSettings _settings;
    private readonly string? _startupPath;
    private readonly string? _startupRestore;

    private DocumentSession _session = DocumentSession.CreateNew();
    private string _newLine = "\n";      // line break for new lines when the document has none yet
    private bool _forceDirty;            // restored draft etc.: dirty even though the undo stack is at its origin
    private bool _lastDirtyShown;
    private ViewMode _mode;
    private bool _suppressModeEvent;
    private double _zoom = 1.0;
    private SearchHighlighter _search = null!;
    private CodeBlockBackground _codeBg = null!;
    private readonly DispatcherTimer _statusTimer;

    public MainWindow(AppSettings settings, string? path = null, string? restoreDraftId = null)
    {
        _settings = settings;
        _startupPath = path;
        _startupRestore = restoreDraftId;
        InitializeComponent();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += (_, _) => { StatusMessage.Text = ""; _statusTimer.Stop(); };

        ConfigureEditor();
        InitFiles();
        InitPreviewTimers();
        RestoreWindowPlacement();
        ApplyEditorTheme();

        _zoom = Math.Clamp(settings.PreviewZoom, 0.5, 3.0);
        ApplyZoom(_zoom, fromPreview: false);
        SetOutlineVisible(true); // the outline is on at every start; hiding it only lasts for the session
        Editor.WordWrap = settings.WordWrap;
        Editor.ShowLineNumbers = settings.LineNumbers;
        Editor.Options.ShowSpaces = Editor.Options.ShowTabs = settings.ShowWhitespace;
        SetMode(settings.Mode, sync: false);

        ThemeManager.Changed += OnThemeChanged;
        Loc.Changed += OnLanguageChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        Activated += (_, _) => CheckExternalChange();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewDragOver += OnPreviewDragOver;
        PreviewDrop += OnPreviewDrop;
        MainArea.SizeChanged += (_, _) => UpdateEditorColumnPadding();
        SizeChanged += (_, _) => ApplyOutlineVisibility();

        LoadSessionIntoEditor(DocumentSession.CreateNew(), null, initial: true);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await InitPreviewAsync();
        if (_startupRestore != null)
        {
            var d = _drafts.List().FirstOrDefault(x => x.Id == _startupRestore);
            if (d != null) RestoreDraft(d);
        }
        else if (_startupPath != null)
        {
            OpenPath(_startupPath, confirm: false);
        }
        OfferDraftRecovery();
        Editor.Focus();
    }

    // ------------------------------------------------------------------ editor setup

    private void ConfigureEditor()
    {
        var o = Editor.Options;
        o.EnableHyperlinks = true;
        o.RequireControlModifierForHyperlinkClick = true;
        o.EnableEmailHyperlinks = false;
        o.HighlightCurrentLine = true;
        o.AllowScrollBelowDocument = true;
        o.EnableRectangularSelection = true;
        o.InheritWordWrapIndentation = true;
        o.CutCopyWholeLine = true;
        o.IndentationSize = 4;
        o.ConvertTabsToSpaces = true;
        o.ShowEndOfLine = false;
        o.EnableTextDragDrop = true;
        Editor.Padding = new Thickness(6, 0, 6, 0);
        Editor.TextArea.SelectionCornerRadius = 2;
        Editor.TextArea.TextView.Options.ColumnRulerPosition = 80;

        _codeBg = new CodeBlockBackground(Editor.TextArea.TextView);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_codeBg);
        _search = new SearchHighlighter(Editor.TextArea.TextView);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_search);

        Editor.TextChanged += (_, _) => OnEditorTextChanged();
        Editor.DocumentChanged += (_, _) => HookDocument();
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaretStatus();
        Editor.TextArea.SelectionChanged += (_, _) => UpdateCaretStatus();
        Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => OnEditorScrolled();
        Editor.TextArea.PreviewKeyDown += EditorTextArea_PreviewKeyDown;
        // AvalonEdit decides whether Paste is available by reading the clipboard on every command re-query, which
        // throws while another program is updating the clipboard. Paste simply stays available where text can be
        // inserted (with nothing on the clipboard it does nothing); executing it is still AvalonEdit's job.
        Editor.TextArea.CommandBindings.Insert(0, new CommandBinding(ApplicationCommands.Paste, null, (_, e) =>
        {
            e.CanExecute = Editor.TextArea.ReadOnlySectionProvider.CanInsert(Editor.TextArea.Caret.Offset);
            e.Handled = true;
        }));
        Editor.ContextMenu = BuildEditorContextMenu();
        Editor.PreviewMouseWheel += (s, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                ApplyZoom(_zoom + (e.Delta > 0 ? 0.1 : -0.1), fromPreview: false);
                e.Handled = true;
            }
        };
    }

    /// <summary>Right-click menu for the source editor. Standard commands are routed to AvalonEdit itself.</summary>
    private ContextMenu BuildEditorContextMenu()
    {
        var m = new ContextMenu();
        MenuItem Cmd(string header, ICommand cmd, string gesture) =>
            new() { Header = header, Command = cmd, CommandTarget = Editor.TextArea, InputGestureText = gesture };
        MenuItem Act(string header, string gesture, RoutedEventHandler click)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture };
            mi.Click += click;
            return mi;
        }
        m.Items.Add(Cmd(Loc.T("Menu_Undo"), ApplicationCommands.Undo, "Ctrl+Z"));
        m.Items.Add(Cmd(Loc.T("Menu_Redo"), ApplicationCommands.Redo, "Ctrl+Y"));
        m.Items.Add(new Separator());
        m.Items.Add(Cmd(Loc.T("Menu_Cut"), ApplicationCommands.Cut, "Ctrl+X"));
        m.Items.Add(Cmd(Loc.T("Menu_Copy"), ApplicationCommands.Copy, "Ctrl+C"));
        m.Items.Add(Cmd(Loc.T("Menu_Paste"), ApplicationCommands.Paste, "Ctrl+V"));
        m.Items.Add(Cmd(Loc.T("Menu_SelectAll"), ApplicationCommands.SelectAll, "Ctrl+A"));
        m.Items.Add(new Separator());
        m.Items.Add(Act(Loc.T("Fmt_Bold"), "Ctrl+B", FmtBold_Click));
        m.Items.Add(Act(Loc.T("Fmt_Italic"), "Ctrl+I", FmtItalic_Click));
        m.Items.Add(Act(Loc.T("Fmt_Code"), "Ctrl+`", FmtCode_Click));
        m.Items.Add(Act(Loc.T("Fmt_Link"), "Ctrl+K", FmtLink_Click));
        m.Items.Add(new Separator());
        m.Items.Add(Act(Loc.T("Menu_FindMore"), "Ctrl+F", (_, _) => ShowFind(false)));
        m.Items.Add(Act(Loc.T("Menu_ReplaceMore"), "Ctrl+H", (_, _) => ShowFind(true)));
        return m;
    }

    private void HookDocument()
    {
        Editor.Document.UndoStack.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UndoStack.IsOriginalFile)) UpdateDirtyState();
        };
    }

    private void EditorTextArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Editor.IsReadOnly &&
            (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
        {
            bool plain = Keyboard.Modifiers == ModifierKeys.Shift;
            if (MarkdownCommands.SmartEnter(Editor, _newLine, plain)) e.Handled = true;
        }
    }

    private void ApplyEditorTheme()
    {
        bool dark = ThemeManager.IsDark;
        Editor.SyntaxHighlighting = MarkdownHighlighting.Get(dark);
        var accent = ThemeManager.Color("Brush.Accent");
        Editor.TextArea.SelectionBrush = Frozen(Color.FromArgb(0x50, accent.R, accent.G, accent.B));
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.Caret.CaretBrush = Frozen(ThemeManager.Color("Brush.Fg"));
        Editor.LineNumbersForeground = Frozen(dark ? Color.FromRgb(0x4D, 0x53, 0x5B) : Color.FromRgb(0xB0, 0xB6, 0xBE));
        Editor.TextArea.TextView.CurrentLineBackground = Frozen(dark ? Color.FromArgb(0x60, 0x2A, 0x2F, 0x36) : Color.FromArgb(0x80, 0xF1, 0xF3, 0xF5));
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        Editor.TextArea.TextView.LinkTextForegroundBrush = Frozen(accent);
        Editor.TextArea.TextView.NonPrintableCharacterBrush = Frozen(dark ? Color.FromRgb(0x4D, 0x53, 0x5B) : Color.FromRgb(0xC0, 0xC5, 0xCC));
        _search.MatchBrush = Frozen(dark ? Color.FromArgb(0x70, 0x6B, 0x57, 0x14) : Color.FromArgb(0x90, 0xFD, 0xE6, 0x8A));
        _search.CurrentBrush = Frozen(dark ? Color.FromArgb(0xC0, 0xB7, 0x79, 0x1F) : Color.FromArgb(0xC0, 0xF5, 0x9E, 0x0B));
        _codeBg.Fill = Frozen(dark ? Color.FromRgb(0x22, 0x26, 0x2C) : Color.FromRgb(0xF6, 0xF8, 0xFA));
        Editor.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
        _search.Invalidate();
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ------------------------------------------------------------------ theme

    private void OnThemeChanged()
    {
        ApplyEditorTheme();
        ApplyPreviewTheme();
    }

    private void OnSystemPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (_settings.Theme == ThemeChoice.System && ThemeManager.ResolveDark(ThemeChoice.System) != ThemeManager.IsDark)
            Dispatcher.BeginInvoke(() => ThemeManager.Apply(ThemeChoice.System));
    }

    private void SetTheme(ThemeChoice choice)
    {
        _settings.Theme = choice;
        ThemeManager.Apply(choice);
        _settings.Save();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = BuildThemeMenu();
        menu.PlacementTarget = ThemeButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private ContextMenu BuildThemeMenu()
    {
        var menu = new ContextMenu();
        foreach (var (key, choice) in new[] { ("Theme_System", ThemeChoice.System), ("Theme_Light", ThemeChoice.Light), ("Theme_Dark", ThemeChoice.Dark) })
        {
            var item = new MenuItem { Header = Loc.T(key), IsChecked = _settings.Theme == choice };
            item.Click += (_, _) => SetTheme(choice);
            menu.Items.Add(item);
        }
        return menu;
    }

    // ------------------------------------------------------------------ language

    private void SetLanguage(LanguageChoice choice)
    {
        _settings.Language = choice;
        Loc.Apply(choice);
        _settings.Save();
    }

    /// <summary>Re-applies every text that is built in code; XAML texts follow through their bindings.</summary>
    private void OnLanguageChanged()
    {
        Editor.ContextMenu = BuildEditorContextMenu();
        UpdateCaretStatus();
        UpdateDirtyState();
        UpdateFormatStatus();
        UpdateWordCount();
        UpdateFindCountText();
        RelocalizeInfo();
        StatusMessage.Text = ""; // transient; would otherwise linger in the old language
        if (_previewFallbackText != null) PreviewFallbackText.Text = _previewFallbackText();
        PostPreviewStrings();
    }

    // ------------------------------------------------------------------ view modes

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressModeEvent || sender is not RadioButton rb) return;
        SetMode(Enum.Parse<ViewMode>((string)rb.Tag), sync: true);
    }

    private void SetMode(ViewMode mode, bool sync)
    {
        double editorLine = sync ? EditorTopLine() : 0;
        double previewLine = _previewTopLine;
        var previous = _mode;
        _mode = mode;
        _settings.Mode = mode;

        _suppressModeEvent = true;
        ModeRead.IsChecked = mode == ViewMode.Read;
        ModeSplit.IsChecked = mode == ViewMode.Split;
        ModeEdit.IsChecked = mode == ViewMode.Edit;
        _suppressModeEvent = false;

        double ratio = Math.Clamp(_settings.SplitRatio, 0.2, 0.8);
        switch (mode)
        {
            case ViewMode.Read:
                EditorColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
                PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
                EditorHost.Visibility = Visibility.Collapsed;
                Splitter.Visibility = Visibility.Collapsed;
                PreviewHost.Visibility = Visibility.Visible;
                break;
            case ViewMode.Split:
                EditorColumn.Width = new GridLength(ratio, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(8);
                PreviewColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
                EditorHost.Visibility = Visibility.Visible;
                Splitter.Visibility = Visibility.Visible;
                PreviewHost.Visibility = Visibility.Visible;
                break;
            case ViewMode.Edit:
                EditorColumn.Width = new GridLength(1, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(0);
                PreviewColumn.Width = new GridLength(0);
                EditorHost.Visibility = Visibility.Visible;
                Splitter.Visibility = Visibility.Collapsed;
                PreviewHost.Visibility = Visibility.Collapsed;
                break;
        }
        EditorHost.Margin = mode == ViewMode.Split ? new Thickness(16, 12, 0, 16) : new Thickness(16, 12, 16, 16);
        ApplyOutlineVisibility();
        UpdateEditorColumnPadding();
        PostToPreview(new { type = "layout", compact = mode == ViewMode.Split });
        if (FindBar.Visibility == Visibility.Visible) RefreshFind(keepPosition: true);

        if (!sync || previous == mode) return;
        // Keep the reading position across mode switches.
        if (mode == ViewMode.Read || (previous == ViewMode.Edit && mode == ViewMode.Split))
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => PreviewScrollToLine(editorLine, smooth: false));
        else if (previous == ViewMode.Read)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => EditorScrollToLine(previewLine));
        if (mode != ViewMode.Read) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Editor.TextArea.Focus());
    }

    private void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        double a = EditorColumn.ActualWidth, b = PreviewColumn.ActualWidth;
        if (a + b > 0) _settings.SplitRatio = a / (a + b);
    }

    /// <summary>In full-width edit mode the text column is centred and widens with the window, like the reading sheet.</summary>
    private void UpdateEditorColumnPadding()
    {
        double w = MainArea.ActualWidth;
        double column = Math.Clamp(w * 0.8, 1000, 1320);
        double side = _mode == ViewMode.Edit ? Math.Max(0, (w - 32 - column) / 2) : 0;
        Editor.Margin = new Thickness(side, 8, 2 + side, 8);
        FormatBar.Padding = new Thickness(8 + side, 5, 8 + side, 5);
    }

    private bool? _narrowState;
    private bool _outlineUserOverride;

    private bool IsNarrow => ActualWidth > 0 && ActualWidth < (_mode == ViewMode.Split ? 900 : 700);
    private bool OutlineShown => OutlinePanel.Visibility == Visibility.Visible;

    /// <summary>Opening a document brings the outline back (unless the window is too narrow for it).</summary>
    private void ShowOutlineForNewDocument()
    {
        _settings.OutlineVisible = true;
        ApplyOutlineVisibility();
    }

    private void SetOutlineVisible(bool visible)
    {
        _settings.OutlineVisible = visible;
        _outlineUserOverride = visible && IsNarrow; // an explicit "show" wins even on a narrow window
        ApplyOutlineVisibility();
    }

    /// <summary>On narrow windows the outline yields its space automatically (the saved preference is kept).</summary>
    private void ApplyOutlineVisibility()
    {
        bool narrow = IsNarrow;
        if (_narrowState != narrow)
        {
            _narrowState = narrow;
            if (!narrow) _outlineUserOverride = false;
        }
        bool show = _settings.OutlineVisible && (!narrow || _outlineUserOverride);
        // Split mode needs every pixel for two panes, so the outline is narrower there (long headings are trimmed, with a tooltip).
        OutlineColumn.Width = show ? new GridLength(_mode == ViewMode.Split ? 192 : 232) : new GridLength(0);
        OutlinePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ToggleOutline_Click(object sender, RoutedEventArgs e) => SetOutlineVisible(!OutlineShown);

    // ------------------------------------------------------------------ zoom

    private void ApplyZoom(double zoom, bool fromPreview)
    {
        _zoom = Math.Round(Math.Clamp(zoom, 0.5, 3.0), 2);
        _settings.PreviewZoom = _zoom;
        Editor.FontSize = BaseEditorFont * _zoom;
        StatusZoom.Text = $"{Math.Round(_zoom * 100)}%";
        if (!fromPreview && Preview.CoreWebView2 != null && Math.Abs(Preview.ZoomFactor - _zoom) > 0.001)
            Preview.ZoomFactor = _zoom;
    }

    // ------------------------------------------------------------------ status / title

    private void UpdateCaretStatus()
    {
        var c = Editor.TextArea.Caret;
        string sel = Editor.SelectionLength > 0 ? Loc.F("Status_Selected", Editor.SelectionLength) : "";
        StatusCaret.Text = Loc.F("Status_Caret", c.Line, c.Column) + sel;
    }

    public bool IsDirty
    {
        get
        {
            if (_forceDirty || _session.PendingFormatChange) return true;
            if (Editor.Document.UndoStack.IsOriginalFile) return false;
            var doc = Editor.Document;
            if (doc.TextLength != _session.SavedText.Length) return true;
            return _session.IsDirty(doc.Text);
        }
    }

    private void UpdateDirtyState()
    {
        bool dirty = IsDirty;
        if (dirty == _lastDirtyShown && DocTitle.Text == _session.DisplayName) return;
        _lastDirtyShown = dirty;
        DirtyDot.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        DocTitle.Text = _session.DisplayName;
        DocTitle.ToolTip = _session.FilePath ?? Loc.T("Tip_NewDocNotSaved");
        Title = (dirty ? "● " : "") + _session.DisplayName + " — InkPage";
    }

    private void UpdateFormatStatus()
    {
        StatusEncoding.Text = _session.Format.DisplayName;
        var style = LineEndings.Detect(Editor.Document.Text);
        StatusEol.Text = style == LineEndingStyle.None ? LineEndings.Label(_newLine == "\r\n" ? LineEndingStyle.CRLF : LineEndingStyle.LF) : LineEndings.Label(style);
        ReadOnlyBadge.Visibility = Editor.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStatus(string text)
    {
        StatusMessage.Text = text;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    // ------------------------------------------------------------------ info bar

    private enum InfoKind { None, General, External, Problem }
    private InfoKind _infoKind;
    private DispatcherTimer? _infoTimer;
    // Text producers rather than strings, so a visible message can follow a language switch.
    private Func<string> _infoText = () => "";
    private (Func<string> Label, Button Button)[] _infoButtons = [];

    private void ShowInfo(InfoKind kind, Func<string> text, bool error = false, TimeSpan? autoHide = null, params (Func<string> Label, Action Action)[] actions)
    {
        _infoKind = kind;
        _infoText = text;
        InfoText.Text = text();
        InfoBar.Background = (Brush)FindResource(error ? "Brush.Error" : "Brush.Info");
        InfoBar.BorderBrush = (Brush)FindResource(error ? "Brush.ErrorLine" : "Brush.InfoLine");
        InfoIcon.Text = error ? "" : kind == InfoKind.General ? "" : "";
        InfoButtons.Children.Clear();
        var buttons = new List<(Func<string>, Button)>();
        foreach (var (label, action) in actions)
        {
            var b = new Button { Content = label(), Style = (Style)FindResource("TextButton"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
            b.Click += (_, _) => action();
            InfoButtons.Children.Add(b);
            buttons.Add((label, b));
        }
        _infoButtons = buttons.ToArray();
        InfoBar.Visibility = Visibility.Visible;
        _infoTimer?.Stop();
        if (autoHide is { } t)
        {
            _infoTimer = new DispatcherTimer { Interval = t };
            _infoTimer.Tick += (_, _) => { _infoTimer?.Stop(); if (_infoKind == kind) HideInfo(); };
            _infoTimer.Start();
        }
    }

    private void RelocalizeInfo()
    {
        InfoText.Text = _infoText();
        foreach (var (label, button) in _infoButtons) button.Content = label();
    }

    private void HideInfo()
    {
        InfoBar.Visibility = Visibility.Collapsed;
        _infoKind = InfoKind.None;
    }

    private void InfoClose_Click(object sender, RoutedEventArgs e)
    {
        if (_infoKind == InfoKind.External) _dismissedDiskStamp = CurrentDiskStamp();
        HideInfo();
    }

    // ------------------------------------------------------------------ keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift), alt = mods.HasFlag(ModifierKeys.Alt);
        bool inEditor = Editor.TextArea.IsKeyboardFocusWithin && !Editor.IsReadOnly;
        bool handled = true;

        if (ctrl && !alt)
        {
            switch (key)
            {
                case Key.N when shift: NewWindow(); break;
                case Key.N: NewDocument(); break;
                case Key.O when shift: SetOutlineVisible(!OutlineShown); break;
                case Key.O: OpenWithDialog(); break;
                case Key.S when shift: SaveAs(); break;
                case Key.S: Save(); break;
                case Key.F: ShowFind(replace: false); break;
                case Key.H: ShowFind(replace: _mode != ViewMode.Read); break;
                case Key.P: PrintPreview(); break;
                case Key.D1 or Key.NumPad1: SetMode(ViewMode.Read, true); break;
                case Key.D2 or Key.NumPad2: SetMode(ViewMode.Split, true); break;
                case Key.D3 or Key.NumPad3: SetMode(ViewMode.Edit, true); break;
                case Key.OemPlus or Key.Add: ApplyZoom(_zoom + 0.1, false); break;
                case Key.OemMinus or Key.Subtract: ApplyZoom(_zoom - 0.1, false); break;
                case Key.D0 or Key.NumPad0: ApplyZoom(1.0, false); break;
                case Key.B when inEditor && !shift: FmtBold_Click(this, e); break;
                case Key.I when inEditor && !shift: FmtItalic_Click(this, e); break;
                case Key.K when inEditor && shift: FmtCodeBlock_Click(this, e); break;
                case Key.K when inEditor: FmtLink_Click(this, e); break;
                case Key.X when inEditor && shift: FmtStrike_Click(this, e); break;
                case Key.Oem3 when inEditor: FmtCode_Click(this, e); break;
                default: handled = false; break;
            }
        }
        else if (ctrl && alt && inEditor && key is Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.D6)
        {
            MarkdownCommands.SetHeading(Editor, key - Key.D0);
        }
        else if (key == Key.F3)
        {
            FindStep(shift ? -1 : 1);
        }
        else if (key == Key.Escape && FindBar.Visibility == Visibility.Visible)
        {
            CloseFind();
        }
        else handled = false;

        if (handled) e.Handled = true;
    }

    // ------------------------------------------------------------------ drag & drop

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(() => OpenPath(files[0], confirm: true));
        }
    }

    // ------------------------------------------------------------------ window placement

    private void RestoreWindowPlacement()
    {
        var s = _settings;
        Width = Math.Max(MinWidth, s.WindowWidth);
        Height = Math.Max(MinHeight, s.WindowHeight);
        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop) &&
            s.WindowLeft >= SystemParameters.VirtualScreenLeft - 50 && s.WindowTop >= SystemParameters.VirtualScreenTop - 10 &&
            s.WindowLeft + 200 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            s.WindowTop + 100 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowLeft;
            Top = s.WindowTop;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void StoreWindowPlacement()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.WindowLeft = r.Left;
        _settings.WindowTop = r.Top;
        _settings.WindowWidth = r.Width;
        _settings.WindowHeight = r.Height;
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
    }

    private void NewWindow()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            AskDialog.Error(this, Loc.T("Dlg_NewWindowFailed"), ex.Message);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.Changed -= OnThemeChanged;
        Loc.Changed -= OnLanguageChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
    }
}
