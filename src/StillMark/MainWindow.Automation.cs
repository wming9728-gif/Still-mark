using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StillMark.Core.IO;
using StillMark.Localization;
using StillMark.Services;
using StillMark.Views;
using Microsoft.Web.WebView2.Core;

namespace StillMark;

/// <summary>Handlers for the opt-in test automation channel (see <see cref="AutomationServer"/>).</summary>
public partial class MainWindow
{
    public async Task<JsonNode?> HandleAutomation(JsonObject req)
    {
        string cmd = (string?)req["cmd"] ?? "";
        string? arg = req["arg"]?.ToString();
        switch (cmd)
        {
            case "state":
                return new JsonObject
                {
                    ["path"] = _session.FilePath,
                    ["title"] = Title,
                    ["dirty"] = IsDirty,
                    ["mode"] = _mode.ToString(),
                    ["theme"] = ThemeManager.IsDark ? "dark" : "light",
                    ["themeSetting"] = _settings.Theme.ToString(),
                    ["themeButton"] = $"{(int)((string)ThemeButton.Content)[0]:X4}|{AutomationProperties.GetName(ThemeButton)}|{ThemeButton.ToolTip}",
                    ["language"] = Loc.Code,
                    ["languageSetting"] = _settings.Language.ToString(),
                    ["systemCulture"] = Loc.SystemCultureName,
                    ["codeLigatures"] = _settings.CodeLigatures,
                    ["length"] = Editor.Document.TextLength,
                    ["lines"] = Editor.Document.LineCount,
                    ["caret"] = Editor.CaretOffset,
                    ["readOnly"] = Editor.IsReadOnly,
                    ["encoding"] = StatusEncoding.Text,
                    ["eol"] = StatusEol.Text,
                    ["status"] = StatusMessage.Text,
                    ["count"] = StatusCount.Text,
                    ["info"] = InfoBar.Visibility == Visibility.Visible ? InfoText.Text : null,
                    ["findCount"] = FindCount.Text,
                    ["outline"] = new JsonArray((OutlineList.ItemsSource as IEnumerable<OutlineItem> ?? []).Select(o => (JsonNode)JsonValue.Create($"{o.Level}|{o.Line}|{o.Text}")!).ToArray()),
                    ["previewReady"] = _previewReady,
                    ["zoom"] = _zoom,
                    ["outlineShown"] = OutlineShown,
                    ["outlineWidth"] = OutlineColumn.ActualWidth,
                    ["editorFontSize"] = Editor.FontSize,
                    ["windowWidth"] = ActualWidth,
                    ["windowHeight"] = ActualHeight,
                    ["dialogs"] = DialogsJson(),
                };
            case "text":
                return Editor.Document.Text;
            case "type":
                Editor.TextArea.Focus();
                foreach (var part in SplitForTyping(arg ?? ""))
                {
                    if (part == "\n") PressKey(Key.Enter);
                    else Editor.TextArea.PerformTextInput(part);
                }
                return true;
            case "key":
                PressKey(Enum.Parse<Key>(arg!));
                return true;
            case "caret":
                Editor.TextArea.Focus();
                Editor.CaretOffset = Math.Clamp(int.Parse(arg!), 0, Editor.Document.TextLength);
                return Editor.CaretOffset;
            case "caretLineEnd":
            {
                var line = Editor.Document.GetLineByNumber(int.Parse(arg!));
                Editor.TextArea.Focus();
                Editor.CaretOffset = line.EndOffset;
                return Editor.CaretOffset;
            }
            case "select":
            {
                var p = arg!.Split(',');
                Editor.TextArea.Focus();
                Editor.Select(int.Parse(p[0]), int.Parse(p[1]));
                return true;
            }
            case "selectText":
            {
                int i = Editor.Document.Text.IndexOf(arg!, StringComparison.Ordinal);
                if (i < 0) return false;
                Editor.TextArea.Focus();
                Editor.Select(i, arg!.Length);
                return i;
            }
            case "invoke":
                // Fire and forget: the action may open a modal dialog. Poll "state"/"dialogs" afterwards.
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                _ = Dispatcher.BeginInvoke(() => RunNamedAction((string)req["name"]!, arg));
                return "queued";
            case "dialogs":
                return DialogsJson();
            case "previewRightClick":
            {
                // A real right-click through the browser's input pipeline (DevTools protocol), at page coordinates.
                var p = arg!.Split(',');
                double x = double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), y = double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                _previewMenu = null;
                foreach (var (type, buttons) in new[] { ("mousePressed", 2), ("mouseReleased", 0) })
                    await Preview.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                        JsonSerializer.Serialize(new { type, x, y, button = "right", buttons, clickCount = 1 }));
                for (int i = 0; i < 30 && _previewMenu == null; i++) await Task.Delay(100);
                return _previewMenu == null ? null
                    : new JsonArray(_previewMenu.Items.OfType<MenuItem>().Select(m => (JsonNode)JsonValue.Create(m.Header?.ToString() ?? "")!).ToArray());
            }
            case "previewMenuPick":
            {
                // Clicks the item with this header (or just dismisses the menu when there is none), as a user would.
                var menu = _previewMenu;
                if (menu == null) return false;
                var item = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString() == arg);
                item?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.IsOpen = false;
                return item != null;
            }
            case "raise":
                // Fault injection for the global error handler: an unhandled exception on the UI thread.
                Exception fault = arg == "clipboardBusy"
                    ? new System.Runtime.InteropServices.COMException("OpenClipboard failed", unchecked((int)0x800401D0))
                    : new InvalidOperationException("Injected test error");
                _ = Dispatcher.BeginInvoke(new Action(() => throw fault));
                return true;
            case "uiStrings":
                return new JsonArray(CollectUiStrings().Distinct().Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
            case "answer":
            {
                var d = Application.Current.Windows.OfType<AskDialog>().LastOrDefault();
                if (d == null) return false;
                d.Answer(int.Parse(arg!));
                return true;
            }
            case "snapshot":
                return await Snapshot(arg!);
            case "eval":
                return Preview.CoreWebView2 == null ? null : await Preview.CoreWebView2.ExecuteScriptAsync(arg!);
            case "resize":
            {
                var p = arg!.Split(',');
                WindowState = WindowState.Normal;
                Width = double.Parse(p[0]);
                Height = double.Parse(p[1]);
                return true;
            }
            case "screenPos":
            {
                // Screen position (physical pixels, middle of the line) of a document offset, for real-input demos.
                var tv = Editor.TextArea.TextView;
                tv.EnsureVisualLines();
                var loc = Editor.Document.GetLocation(Math.Clamp(int.Parse(arg!), 0, Editor.Document.TextLength));
                var vp = tv.GetVisualPosition(new ICSharpCode.AvalonEdit.TextViewPosition(loc), ICSharpCode.AvalonEdit.Rendering.VisualYPosition.LineMiddle);
                var inView = vp - tv.ScrollOffset;
                var screen = tv.PointToScreen(inView);
                bool visible = inView.Y >= 0 && inView.Y <= tv.ActualHeight && inView.X >= 0 && inView.X <= tv.ActualWidth;
                return new JsonObject { ["x"] = screen.X, ["y"] = screen.Y, ["visible"] = visible };
            }
            case "editorTopLine":
                return EditorTopLine();
            case "previewTopLine":
                return _previewTopLine;
            case "scrollEditor":
                EditorScrollToLine(double.Parse(arg!));
                return true;
            case "userScrollEditor":
            {
                // Like a mouse-wheel scroll by the user: no sync suppression.
                var tv = Editor.TextArea.TextView;
                Editor.ScrollToVerticalOffset(tv.GetVisualTopByDocumentLine(int.Parse(arg!)));
                return true;
            }
            default:
                throw new InvalidOperationException("unknown command " + cmd);
        }
    }

    private static IEnumerable<string> SplitForTyping(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in text)
        {
            if (c == '\n')
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                yield return "\n";
            }
            else if (c != '\r') sb.Append(c);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>Raises the key events WPF would raise for a physical key press (no modifiers).</summary>
    private void PressKey(Key key)
    {
        var target = Keyboard.FocusedElement as UIElement ?? Editor.TextArea;
        var src = PresentationSource.FromVisual(this)!;
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (preview.Handled) return;
        var down = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, key) { RoutedEvent = Keyboard.KeyDownEvent };
        target.RaiseEvent(down);
    }

    private static JsonArray DialogsJson() => new(Application.Current.Windows.OfType<AskDialog>()
        .Select(d => (JsonNode)new JsonObject { ["title"] = d.TitleValue, ["message"] = d.MessageValue, ["buttons"] = new JsonArray(d.ButtonLabels.Select(b => (JsonNode)JsonValue.Create(b)!).ToArray()) })
        .ToArray());

    private void RunNamedAction(string name, string? arg)
    {
        switch (name)
        {
            case "new": NewDocument(); break;
            case "open": OpenPath(arg!, confirm: true); break;
            case "save": Save(); break;
            case "saveAs": SaveAsPath(arg!); break;
            case "reload": ReloadWithConfirm(); break;
            case "close": Close(); break;
            case "mode": SetMode(Enum.Parse<ViewMode>(arg!), sync: true); break;
            case "theme": SetTheme(Enum.Parse<ThemeChoice>(arg!)); break;
            case "themeButton": ThemeButton_Click(ThemeButton, new RoutedEventArgs()); break;
            case "language": SetLanguage(Enum.Parse<LanguageChoice>(arg!)); break;
            case "ligatures": SetCodeLigatures(bool.Parse(arg!)); break;
            case "outline": SetOutlineVisible(bool.Parse(arg!)); break;
            case "find": ShowFind(false); FindBox.Text = arg ?? ""; RefreshFind(keepPosition: false); break;
            case "findNext": FindStep(1); break;
            case "findPrev": FindStep(-1); break;
            case "replace": ShowFind(true); ReplaceBox.Text = arg ?? ""; break;
            case "replaceAll": ReplaceAll_Click(this, new RoutedEventArgs()); break;
            case "closeFind": CloseFind(); break;
            case "undo": Editor.Undo(); break;
            case "redo": Editor.Redo(); break;
            case "copy": Editor.Copy(); break;
            case "cut": Editor.Cut(); break;
            case "paste": Editor.Paste(); break;
            case "bold": FmtBold_Click(this, new RoutedEventArgs()); break;
            case "italic": FmtItalic_Click(this, new RoutedEventArgs()); break;
            case "heading": MarkdownCommandsHeading(int.Parse(arg!)); break;
            case "bullet": FmtBullet_Click(this, new RoutedEventArgs()); break;
            case "task": FmtTask_Click(this, new RoutedEventArgs()); break;
            case "table": FmtTable_Click(this, new RoutedEventArgs()); break;
            case "codeBlock": FmtCodeBlock_Click(this, new RoutedEventArgs()); break;
            case "eol": ConvertLineEndings(arg == "CRLF" ? "\r\n" : "\n"); break;
            case "encoding": ChangeEncoding(arg == "UTF8BOM" ? new TextFormat(TextEncodingKind.Utf8, true) : TextFormat.Utf8NoBom); break;
            case "zoom": ApplyZoom(double.Parse(arg!), false); break;
            case "checkDisk": CheckExternalChange(); break;
            case "wrap": Editor.WordWrap = bool.Parse(arg!); break;
            case "exportHtmlTo": ExportHtmlTo(arg!); break;
            default: throw new InvalidOperationException("unknown action " + name);
        }
    }

    private void MarkdownCommandsHeading(int level) { if (CanEdit()) StillMark.Editor.MarkdownCommands.SetHeading(Editor, level); }

    /// <summary>
    /// Every interface text of the window and its menus (texts, tooltips, accessible names), leaving out document
    /// content and file names. Used to check that a language switch left nothing untranslated.
    /// </summary>
    private List<string> CollectUiStrings()
    {
        var list = new List<string>();
        void Add(object? o) { if (o is string s && s.Trim().Length > 0) list.Add(s); }
        void Walk(object node)
        {
            if (node is not DependencyObject d) return;
            if (d == Editor || d == OutlineList || d == DocTitle || d == FindBox || d == ReplaceBox) return;
            if (d is FrameworkElement fe)
            {
                if (fe.Tag as string == FileItemTag) return;
                Add(fe.ToolTip);
                Add(AutomationProperties.GetName(fe));
            }
            if (d is TextBlock tb) Add(tb.Text);
            if (d is ContentControl cc) Add(cc.Content);
            if (d is HeaderedItemsControl h) { Add(h.Header); if (h.Header is TextBlock ht) Add(ht.Text); }
            foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        Walk(this);
        if (Editor.ContextMenu != null) Walk(Editor.ContextMenu);
        Walk(BuildMainMenu());
        Walk(BuildHeadingMenu());
        return list;
    }

    private async Task<string> Snapshot(string path)
    {
        UpdateLayout();
        byte[]? png = null;
        if (Preview.CoreWebView2 != null && PreviewHost.IsVisible && Preview.IsVisible)
        {
            using var ms = new MemoryStream();
            await Preview.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            png = ms.ToArray();
        }
        AutomationServer.SaveSnapshot(this, path, PreviewHost, png);
        var dialog = Application.Current.Windows.OfType<AskDialog>().LastOrDefault();
        if (dialog != null)
            AutomationServer.SaveSnapshot(dialog, Path.ChangeExtension(path, ".dialog.png"));
        return path;
    }
}
