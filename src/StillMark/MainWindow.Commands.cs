using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StillMark.Core.IO;
using StillMark.Editor;
using StillMark.Localization;
using StillMark.Services;
using StillMark.Views;

namespace StillMark;

/// <summary>Formatting toolbar and the main menu.</summary>
public partial class MainWindow
{
    private bool CanEdit()
    {
        if (_mode == ViewMode.Read) SetMode(ViewMode.Split, sync: true);
        if (Editor.IsReadOnly) { ShowStatus(Loc.T("Status_ReadOnlyDoc")); return false; }
        Editor.TextArea.Focus();
        return true;
    }

    private void FmtBold_Click(object sender, RoutedEventArgs e) { if (CanEdit()) MarkdownCommands.ToggleWrap(Editor, "**", "**", Loc.T("Ph_Bold")); }
    private void FmtItalic_Click(object sender, RoutedEventArgs e) { if (CanEdit()) MarkdownCommands.ToggleWrap(Editor, "*", "*", Loc.T("Ph_Italic")); }
    private void FmtStrike_Click(object sender, RoutedEventArgs e) { if (CanEdit()) MarkdownCommands.ToggleWrap(Editor, "~~", "~~", Loc.T("Ph_Strike")); }
    private void FmtCode_Click(object sender, RoutedEventArgs e) { if (CanEdit()) MarkdownCommands.ToggleWrap(Editor, "`", "`", "code"); }
    private void FmtHeading_Click(object sender, RoutedEventArgs e)
    {
        var m = BuildHeadingMenu();
        m.PlacementTarget = HeadingButton;
        m.Placement = PlacementMode.Bottom;
        m.IsOpen = true;
    }

    private ContextMenu BuildHeadingMenu()
    {
        var m = new ContextMenu();
        for (int level = 1; level <= 6; level++)
        {
            int l = level;
            var item = new MenuItem { Header = new TextBlock { Text = Loc.F("Fmt_HeadingN", l), FontSize = 17 - l, FontWeight = l <= 3 ? FontWeights.SemiBold : FontWeights.Normal }, InputGestureText = $"Ctrl+Alt+{l}" };
            item.Click += (_, _) => { if (CanEdit()) MarkdownCommands.SetHeading(Editor, l); };
            m.Items.Add(item);
        }
        return m;
    }

    private void FmtBullet_Click(object sender, RoutedEventArgs e) { if (CanEdit()) MarkdownCommands.ToggleLinePrefix(Editor, "- "); }
    private void FmtNumber_Click(object sender, RoutedEventArgs e) { if (CanEdit()) MarkdownCommands.ToggleLinePrefix(Editor, "1. ", numbered: true); }
    private void FmtTask_Click(object sender, RoutedEventArgs e) { if (CanEdit()) MarkdownCommands.ToggleLinePrefix(Editor, "- [ ] "); }
    private void FmtQuote_Click(object sender, RoutedEventArgs e) { if (CanEdit()) MarkdownCommands.ToggleLinePrefix(Editor, "> "); }
    private void FmtCodeBlock_Click(object sender, RoutedEventArgs e) { if (CanEdit()) MarkdownCommands.InsertFencedCode(Editor, _newLine); }
    private void FmtLink_Click(object sender, RoutedEventArgs e) { if (CanEdit()) MarkdownCommands.InsertLink(Editor, image: false); }
    private void FmtRule_Click(object sender, RoutedEventArgs e) { if (CanEdit()) MarkdownCommands.InsertBlock(Editor, ["---"], _newLine, 0, 3); }

    private void FmtTable_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        string head = "| " + string.Join(" | ", Enumerable.Range(1, 3).Select(i => Loc.F("Ph_TableColumn", i))) + " |";
        MarkdownCommands.InsertBlock(Editor, [head, "| --- | --- | --- |", "|  |  |  |", "|  |  |  |"], _newLine, 2, 2);
    }

    private void FmtImage_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Fmt_Image"),
            Filter = $"{Loc.T("Filter_Images")} (*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg;*.bmp|{Loc.T("Filter_All")} (*.*)|*.*",
        };
        if (_session.FilePath != null) dlg.InitialDirectory = Path.GetDirectoryName(_session.FilePath);
        if (dlg.ShowDialog(this) != true) { MarkdownCommands.InsertLink(Editor, image: true); return; }

        string path = dlg.FileName;
        if (_session.FilePath != null)
        {
            // Prefer a relative path so the document stays portable.
            string rel = Path.GetRelativePath(Path.GetDirectoryName(_session.FilePath)!, path);
            if (!Path.IsPathRooted(rel)) path = rel;
        }
        path = path.Replace('\\', '/');
        if (path.Contains(' ') || path.Contains('(') || path.Contains(')')) path = "<" + path + ">";
        Editor.TextArea.Focus();
        MarkdownCommands.InsertLink(Editor, image: true, url: path);
    }

    // ------------------------------------------------------------------ main menu

    /// <summary>Tag on menu items whose header is a file name (left out of the untranslated-text check).</summary>
    internal const string FileItemTag = "file";

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var m = BuildMainMenu();
        m.PlacementTarget = MenuButton;
        m.Placement = PlacementMode.Bottom;
        m.IsOpen = true;
    }

    private ContextMenu BuildMainMenu()
    {
        var m = new ContextMenu();
        MenuItem Item(string header, Action action, string? gesture = null, bool enabled = true, bool? check = null)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
            if (check != null) mi.IsChecked = check.Value;
            mi.Click += (_, _) => action();
            return mi;
        }
        MenuItem Sub(string header, params object[] items)
        {
            var mi = new MenuItem { Header = header };
            foreach (var i in items) mi.Items.Add(i);
            return mi;
        }

        bool hasFile = _session.FilePath != null;
        bool previewOk = _previewReady;

        // File
        m.Items.Add(Item(Loc.T("Cmd_New"), NewDocument, "Ctrl+N"));
        m.Items.Add(Item(Loc.T("Menu_NewWindow"), NewWindow, "Ctrl+Shift+N"));
        m.Items.Add(Item(Loc.T("Menu_Open"), OpenWithDialog, "Ctrl+O"));
        var recent = new MenuItem { Header = Loc.T("Menu_Recent"), IsEnabled = _settings.RecentFiles.Count > 0 };
        foreach (var p in _settings.RecentFiles.ToList())
        {
            string path = p;
            var mi = Item(Path.GetFileName(path), () => OpenPath(path, confirm: true));
            mi.ToolTip = path;
            mi.Tag = FileItemTag;
            recent.Items.Add(mi);
        }
        if (_settings.RecentFiles.Count > 0)
        {
            recent.Items.Add(new Separator());
            recent.Items.Add(Item(Loc.T("Menu_ClearRecent"), () => { _settings.RecentFiles.Clear(); _settings.Save(); }));
        }
        m.Items.Add(recent);
        m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.T("Cmd_Save"), () => Save(), "Ctrl+S"));
        m.Items.Add(Item(Loc.T("Menu_SaveAs"), () => SaveAs(), "Ctrl+Shift+S"));
        m.Items.Add(Item(Loc.T("Menu_Reload"), ReloadWithConfirm, enabled: hasFile));
        m.Items.Add(Item(Loc.T("Menu_Reveal"), () => RevealInExplorer(_session.FilePath!), enabled: hasFile));
        m.Items.Add(new Separator());

        // Edit
        m.Items.Add(Sub(Loc.T("Menu_Edit"),
            Item(Loc.T("Menu_Undo"), () => Editor.Undo(), "Ctrl+Z", Editor.CanUndo),
            Item(Loc.T("Menu_Redo"), () => Editor.Redo(), "Ctrl+Y", Editor.CanRedo),
            new Separator(),
            Item(Loc.T("Menu_Cut"), () => Editor.Cut(), "Ctrl+X", !Editor.IsReadOnly),
            Item(Loc.T("Menu_Copy"), () => Editor.Copy(), "Ctrl+C"),
            Item(Loc.T("Menu_Paste"), () => Editor.Paste(), "Ctrl+V", !Editor.IsReadOnly),
            Item(Loc.T("Menu_SelectAll"), () => { Editor.TextArea.Focus(); Editor.SelectAll(); }, "Ctrl+A"),
            new Separator(),
            Item(Loc.T("Cmd_Find"), () => ShowFind(false), "Ctrl+F"),
            Item(Loc.T("Find_Replace"), () => { if (_mode == ViewMode.Read) SetMode(ViewMode.Split, true); ShowFind(true); }, "Ctrl+H")));

        // View
        m.Items.Add(Sub(Loc.T("Menu_View"),
            Item(Loc.T("Mode_Read"), () => SetMode(ViewMode.Read, true), "Ctrl+1", check: _mode == ViewMode.Read),
            Item(Loc.T("Menu_ViewSplit"), () => SetMode(ViewMode.Split, true), "Ctrl+2", check: _mode == ViewMode.Split),
            Item(Loc.T("Mode_Edit"), () => SetMode(ViewMode.Edit, true), "Ctrl+3", check: _mode == ViewMode.Edit),
            new Separator(),
            Item(Loc.T("Cmd_Outline"), () => SetOutlineVisible(!OutlineShown), "Ctrl+Shift+O", check: OutlineShown),
            Item(Loc.T("Menu_WordWrap"), () => { Editor.WordWrap = _settings.WordWrap = !Editor.WordWrap; }, check: Editor.WordWrap),
            Item(Loc.T("Menu_LineNumbers"), () => { Editor.ShowLineNumbers = _settings.LineNumbers = !Editor.ShowLineNumbers; }, check: Editor.ShowLineNumbers),
            Item(Loc.T("Menu_Whitespace"), () =>
            {
                _settings.ShowWhitespace = !_settings.ShowWhitespace;
                Editor.Options.ShowSpaces = Editor.Options.ShowTabs = _settings.ShowWhitespace;
            }, check: _settings.ShowWhitespace),
            Item(Loc.T("Menu_CodeLigatures"), () => SetCodeLigatures(!_settings.CodeLigatures), check: _settings.CodeLigatures),
            new Separator(),
            Item(Loc.T("Menu_ZoomIn"), () => ApplyZoom(_zoom + 0.1, false), "Ctrl+="),
            Item(Loc.T("Menu_ZoomOut"), () => ApplyZoom(_zoom - 0.1, false), "Ctrl+-"),
            Item(Loc.T("Menu_ZoomReset"), () => ApplyZoom(1.0, false), "Ctrl+0"),
            new Separator(),
            Item(Loc.T("Menu_ThemeSystem"), () => SetTheme(ThemeChoice.System), check: _settings.Theme == ThemeChoice.System),
            Item(Loc.T("Menu_ThemeLight"), () => SetTheme(ThemeChoice.Light), check: _settings.Theme == ThemeChoice.Light),
            Item(Loc.T("Menu_ThemeDark"), () => SetTheme(ThemeChoice.Dark), check: _settings.Theme == ThemeChoice.Dark)));

        // Language (each language is listed under its own name, so it can be found from any UI language)
        m.Items.Add(Sub(Loc.T("Menu_Language"),
            Item(Loc.T("Lang_System"), () => SetLanguage(LanguageChoice.System), check: _settings.Language == LanguageChoice.System),
            new Separator(),
            Item(Loc.T("Lang_English"), () => SetLanguage(LanguageChoice.English), check: _settings.Language == LanguageChoice.English),
            Item(Loc.T("Lang_ZhHans"), () => SetLanguage(LanguageChoice.ChineseSimplified), check: _settings.Language == LanguageChoice.ChineseSimplified)));

        // Format of the file on disk
        var eol = LineEndings.Detect(Editor.Document.Text);
        m.Items.Add(Sub(Loc.T("Menu_FileFormat"),
            Item(Loc.F("Menu_CurrentEncoding", _session.Format.DisplayName), () => { }, enabled: false),
            Item(Loc.T("Menu_SaveUtf8"), () => ChangeEncoding(TextFormat.Utf8NoBom), check: _session.Format == TextFormat.Utf8NoBom),
            Item(Loc.T("Menu_SaveUtf8Bom"), () => ChangeEncoding(new TextFormat(TextEncodingKind.Utf8, true)),
                check: _session.Format == new TextFormat(TextEncodingKind.Utf8, true)),
            new Separator(),
            Item(Loc.F("Menu_CurrentEol", LineEndings.Label(eol)), () => { }, enabled: false),
            Item(Loc.T("Menu_ToLf"), () => ConvertLineEndings("\n"), enabled: !Editor.IsReadOnly),
            Item(Loc.T("Menu_ToCrlf"), () => ConvertLineEndings("\r\n"), enabled: !Editor.IsReadOnly)));
        m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.T("Menu_ExportHtml"), ExportHtml, enabled: previewOk));
        m.Items.Add(Item(Loc.T("Menu_Print"), PrintPreview, "Ctrl+P", previewOk));
        m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.T("Menu_About"), ShowAbout));
        m.Items.Add(Item(Loc.T("Menu_Exit"), Close, "Alt+F4"));
        return m;
    }

    private void ShowAbout()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        AskDialog.Show(this, Loc.F("About_Title", v?.ToString(3)), Loc.T("About_Body"),
            [Loc.T("Btn_OK")], 0, 0, AskIcon.Info,
            Loc.F("About_Details", Environment.Version, AppPaths.DataDir));
    }
}
