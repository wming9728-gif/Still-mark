using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StillMark.Core.Markdown;
using StillMark.Localization;
using StillMark.Services;
using StillMark.Views;
using Microsoft.Web.WebView2.Core;

namespace StillMark;

/// <summary>Live preview: Markdown → HTML (background thread) → WebView2 page; scroll sync and outline.</summary>
public partial class MainWindow
{
    private const string AppHost = "app.stillmark";
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".ico", ".avif", ".tif", ".tiff" };

    private DispatcherTimer _renderTimer = null!;
    private bool _previewReady;
    private object? _pendingRender;
    private int _renderSeq, _appliedSeq;
    private double _previewTopLine;
    private DateTime _ignoreEditorScrollUntil, _ignorePreviewScrollUntil;
    private string? _lastPostedHtml;
    private int _wordCount;
    private Func<string>? _previewFallbackText;

    public sealed record OutlineItem(string Text, int Line, int Level)
    {
        public Thickness Indent => new((Level - 1) * 14, 0, 0, 0);
        public double FontSize => Level <= 1 ? 13 : 12.5;
        public FontWeight Weight => Level <= 1 ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void InitPreviewTimers()
    {
        _renderTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _renderTimer.Tick += (_, _) => RenderNow(resetScroll: false);
    }

    private async Task InitPreviewAsync()
    {
        try
        {
            // Language: only affects WebView2's own dialogs (print); the page and its menu are localized by StillMark.
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection", Language = Loc.Code };
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDir, options);
            await Preview.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            PreviewFallback.Visibility = Visibility.Visible;
            Preview.Visibility = Visibility.Collapsed;
            _previewFallbackText = ex is WebView2RuntimeNotFoundException
                ? () => Loc.T("Preview_NoWebView2")
                : () => Loc.F("Preview_Failed", ex.Message);
            PreviewFallbackText.Text = _previewFallbackText();
            return;
        }

        var core = Preview.CoreWebView2;
        var st = core.Settings;
        st.AreDevToolsEnabled = Debugger.IsAttached || Environment.GetEnvironmentVariable("STILLMARK_DEVTOOLS") == "1";
        st.AreBrowserAcceleratorKeysEnabled = false;
        st.IsStatusBarEnabled = false;
        st.IsZoomControlEnabled = true;
        st.IsPasswordAutosaveEnabled = false;
        st.IsGeneralAutofillEnabled = false;
        st.IsBuiltInErrorPageEnabled = false;
        st.IsWebMessageEnabled = true;
        st.AreDefaultScriptDialogsEnabled = false;

        core.SetVirtualHostNameToFolderMapping(AppHost, AppPaths.WebDir, CoreWebView2HostResourceAccessKind.DenyCors);
        core.AddWebResourceRequestedFilter($"https://{MarkdownRenderer.DocHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnDocResourceRequested;
        core.NavigationStarting += OnPreviewNavigationStarting;
        core.NewWindowRequested += (_, e) => { e.Handled = true; HandleLink(e.Uri); };
        core.WebMessageReceived += OnPreviewMessage;
        core.ContextMenuRequested += OnPreviewContextMenu;
        core.ProcessFailed += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            ShowStatus(Loc.T("Status_PreviewCrashed"));
            _previewReady = false;
            try { Preview.CoreWebView2?.Reload(); } catch { }
        });
        Preview.ZoomFactorChanged += (_, _) => { if (Math.Abs(Preview.ZoomFactor - _zoom) > 0.001) ApplyZoom(Preview.ZoomFactor, fromPreview: true); };
        Preview.ZoomFactor = _zoom;
        ApplyPreviewTheme();
        core.Navigate($"https://{AppHost}/index.html");
    }

    private void ApplyPreviewTheme()
    {
        var c = ThemeManager.Color("Brush.Page");
        Preview.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, c.R, c.G, c.B);
        PostToPreview(new { type = "theme", theme = ThemeManager.IsDark ? "dark" : "light" });
    }

    /// <summary>Programming ligatures of the preview's code font (<c>&lt;=</c> drawn as one sign). Off by default, so code reads exactly as written.</summary>
    private void SetCodeLigatures(bool on)
    {
        _settings.CodeLigatures = on;
        PostToPreview(new { type = "ligatures", on });
    }

    private void PostPreviewStrings() => PostToPreview(new
    {
        type = "strings",
        emptyTitle = Loc.T("Page_EmptyTitle"),
        emptyHint = Loc.T("Page_EmptyHint"),
        imageBroken = Loc.T("Page_ImageBroken"),
    });

    private void PostToPreview(object message)
    {
        if (!_previewReady || Preview.CoreWebView2 == null) return;
        try { Preview.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message)); }
        catch { /* preview being torn down */ }
    }

    // ------------------------------------------------------------------ rendering

    private void ScheduleRender()
    {
        int len = Editor.Document.TextLength;
        _renderTimer.Interval = TimeSpan.FromMilliseconds(len > 1_000_000 ? 900 : len > 200_000 ? 400 : 120);
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private async void RenderNow(bool resetScroll)
    {
        _renderTimer.Stop();
        int seq = ++_renderSeq;
        string text = Editor.Document.Text;
        RenderResult result;
        (int chars, int lines) counts;
        int endLine;
        try
        {
            (result, counts, endLine) = await Task.Run(() => (MarkdownRenderer.Render(text), CountWords(text), EndLine(text)));
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.F("Status_RenderError", ex.Message));
            return;
        }
        if (seq < _appliedSeq) return; // an even newer render already landed
        _appliedSeq = seq;

        _wordCount = counts.chars;
        UpdateWordCount();
        UpdateOutline(result.Headings);
        _codeBg.SetRanges(result.CodeBlocks);

        var msg = new
        {
            type = "render",
            html = result.Html,
            baseHref = MarkdownRenderer.BaseHrefFor(_session.FilePath == null ? null : Path.GetDirectoryName(_session.FilePath)),
            keepScroll = !resetScroll,
            endLine,                        // end-of-document anchor for scroll sync
            title = _session.DisplayName,   // used by print headers
        };
        _lastPostedHtml = result.Html;
        if (_previewReady) PostToPreview(msg);
        else _pendingRender = msg;
    }

    /// <summary>Zero-based source line just past the last non-blank line: where the rendered content ends.</summary>
    private static int EndLine(string text)
    {
        int end = text.Length;
        while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
        return end == 0 ? 0 : text.AsSpan(0, end).Count('\n') + 1;
    }

    private void UpdateWordCount() => StatusCount.Text = Loc.F("Status_Words", _wordCount);

    /// <summary>Chinese-style count: each CJK character counts as one, each run of latin letters/digits as one word.</summary>
    private static (int, int) CountWords(string text)
    {
        int count = 0, lines = 1;
        bool inWord = false;
        foreach (char ch in text)
        {
            if (ch == '\n') lines++;
            if (ch >= 0x3400 && ch <= 0x9FFF || ch >= 0xF900 && ch <= 0xFAFF || ch >= 0x3040 && ch <= 0x30FF || ch >= 0xAC00 && ch <= 0xD7AF)
            {
                count++;
                inWord = false;
            }
            else if (char.IsLetterOrDigit(ch) || ch == '\'' && inWord)
            {
                if (!inWord) { count++; inWord = true; }
            }
            else inWord = false;
        }
        return (count, lines);
    }

    // ------------------------------------------------------------------ outline

    private void UpdateOutline(IReadOnlyList<HeadingInfo> headings)
    {
        var items = headings.Where(h => h.Text.Length > 0).Select(h => new OutlineItem(h.Text, h.Line, h.Level)).ToList();
        if (OutlineList.ItemsSource is List<OutlineItem> old && old.SequenceEqual(items)) return;
        OutlineList.ItemsSource = items;
        OutlineEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OutlineList_Click(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is OutlineItem item) NavigateToLine(item.Line);
    }

    private void OutlineList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OutlineList.SelectedItem is OutlineItem item) NavigateToLine(item.Line);
    }

    private void NavigateToLine(int line0)
    {
        if (_mode != ViewMode.Read)
        {
            var docLine = Editor.Document.GetLineByNumber(Math.Clamp(line0 + 1, 1, Editor.Document.LineCount));
            Editor.CaretOffset = docLine.Offset;
            EditorScrollToLine(line0);
            Editor.TextArea.Focus();
        }
        if (_mode != ViewMode.Edit) PreviewScrollToLine(line0, smooth: true);
    }

    // ------------------------------------------------------------------ scroll sync

    /// <summary>Zero-based (Markdig-style) fractional source line at the top of the editor viewport.</summary>
    private double EditorTopLine()
    {
        var tv = Editor.TextArea.TextView;
        if (Editor.Document.LineCount == 0) return 0;
        double y = tv.ScrollOffset.Y;
        var line = tv.GetDocumentLineByVisualTop(y);
        if (line == null) return 0;
        double top = tv.GetVisualTopByDocumentLine(line.LineNumber);
        double next = line.LineNumber < Editor.Document.LineCount ? tv.GetVisualTopByDocumentLine(line.LineNumber + 1) : top + tv.DefaultLineHeight;
        double frac = next > top ? Math.Clamp((y - top) / (next - top), 0, 1) : 0;
        return line.LineNumber - 1 + frac;
    }

    private void EditorScrollToLine(double line0)
    {
        var doc = Editor.Document;
        var tv = Editor.TextArea.TextView;
        int n = Math.Clamp((int)Math.Floor(line0) + 1, 1, doc.LineCount);
        // No EnsureVisualLines(): forcing a synchronous layout from here can re-enter AvalonEdit's measure pass.
        double top = tv.GetVisualTopByDocumentLine(n);
        double next = n < doc.LineCount ? tv.GetVisualTopByDocumentLine(n + 1) : top + tv.DefaultLineHeight;
        double y = top + (next - top) * Math.Clamp(line0 + 1 - n, 0, 1);
        _ignoreEditorScrollUntil = DateTime.UtcNow.AddMilliseconds(250);
        Editor.ScrollToVerticalOffset(Math.Max(0, y));
    }

    private void PreviewScrollToLine(double line0, bool smooth)
    {
        _ignorePreviewScrollUntil = DateTime.UtcNow.AddMilliseconds(300);
        PostToPreview(new { type = "scrollToLine", line = line0, smooth });
    }

    private bool _editorScrollQueued;

    private void OnEditorScrolled()
    {
        // ScrollOffsetChanged can be raised from inside the editor's layout pass; do the work afterwards.
        if (_mode != ViewMode.Split || DateTime.UtcNow < _ignoreEditorScrollUntil || _editorScrollQueued) return;
        _editorScrollQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _editorScrollQueued = false;
            if (_mode == ViewMode.Split && DateTime.UtcNow >= _ignoreEditorScrollUntil)
                PreviewScrollToLine(EditorTopLine(), smooth: false);
        });
    }

    // ------------------------------------------------------------------ messages from the page

    private void OnPreviewMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        try { msg = JsonDocument.Parse(e.WebMessageAsJson).RootElement; }
        catch { return; }
        string type = msg.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        switch (type)
        {
            case "ready":
                _previewReady = true;
                ApplyPreviewTheme();
                PostPreviewStrings();
                PostToPreview(new { type = "layout", compact = _mode == ViewMode.Split });
                PostToPreview(new { type = "ligatures", on = _settings.CodeLigatures });
                if (_pendingRender != null) { PostToPreview(_pendingRender); _pendingRender = null; }
                else RenderNow(resetScroll: true);
                if (FindBar.Visibility == Visibility.Visible && _mode == ViewMode.Read) RefreshFind(keepPosition: true);
                break;
            case "scroll":
                _previewTopLine = msg.GetProperty("line").GetDouble();
                if (_mode == ViewMode.Split && DateTime.UtcNow >= _ignorePreviewScrollUntil)
                    EditorScrollToLine(_previewTopLine);
                break;
            case "link":
                HandleLink(msg.GetProperty("href").GetString() ?? "");
                break;
            case "dropFiles":
                var dropped = e.AdditionalObjects?.OfType<CoreWebView2File>().Select(x => x.Path).FirstOrDefault();
                if (!string.IsNullOrEmpty(dropped)) Dispatcher.BeginInvoke(() => OpenPath(dropped, confirm: true));
                break;
            case "findResult":
                if (_mode == ViewMode.Read)
                    SetFindCount(msg.GetProperty("count").GetInt32(), msg.GetProperty("index").GetInt32());
                break;
        }
    }

    private void HandleLink(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) return;
        if (uri.Host.Equals(MarkdownRenderer.DocHost, StringComparison.OrdinalIgnoreCase))
        {
            var local = MarkdownRenderer.LocalPathFromDocUrl(uri.GetLeftPart(UriPartial.Path));
            if (local == null) return;
            if (File.Exists(local))
            {
                string ext = Path.GetExtension(local).ToLowerInvariant();
                if (ext is ".md" or ".markdown" or ".mdown" or ".mkd" or ".txt") OpenPath(local, confirm: true);
                else RevealInExplorer(local);
            }
            else if (Directory.Exists(local)) RevealInExplorer(local);
            else ShowStatus(Loc.F("Status_LinkMissing", local));
            return;
        }
        if (uri.Scheme is "http" or "https" or "mailto")
        {
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { AskDialog.Error(this, Loc.T("Dlg_CantOpenLink"), ex.Message); }
        }
    }

    private static void RevealInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false }); } catch { }
    }

    private void OnPreviewNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith($"https://{AppHost}/index.html", StringComparison.OrdinalIgnoreCase)) return;
        e.Cancel = true;
        // A file dropped onto the preview arrives as a file:// navigation.
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.IsFile)
            Dispatcher.BeginInvoke(() => OpenPath(uri.LocalPath, confirm: true));
        else
            Dispatcher.BeginInvoke(() => HandleLink(e.Uri));
    }

    /// <summary>Built-in preview commands the page's right-click menu offers, with their StillMark labels.</summary>
    private static readonly (string Name, string Key)[] PreviewMenuCommands =
    [
        ("copy", "Menu_Copy"), ("selectAll", "Menu_SelectAll"),
        ("copyImage", "Ctx_CopyImage"), ("copyImageLocation", "Ctx_CopyImageLink"), ("copyLinkLocation", "Ctx_CopyLink"),
    ];

    /// <summary>
    /// The preview's right-click menu is drawn by StillMark (WebView2 would label it in the language it started with)
    /// and runs the chosen built-in command through <see cref="CoreWebView2ContextMenuRequestedEventArgs.SelectedCommandId"/>.
    /// </summary>
    private void OnPreviewContextMenu(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        e.Handled = true;
        var available = PreviewMenuCommands
            .Select(c => (Item: e.MenuItems.FirstOrDefault(i => i.Name == c.Name), c.Key))
            .Where(x => x.Item != null && x.Item.IsEnabled)
            .ToList();
        if (available.Count == 0) return;

        var deferral = e.GetDeferral();
        var menu = new ContextMenu { PlacementTarget = Preview, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        foreach (var (item, key) in available)
        {
            var mi = new MenuItem { Header = Loc.T(key), InputGestureText = item!.ShortcutKeyDescription ?? "" };
            int id = item.CommandId;
            mi.Click += (_, _) => e.SelectedCommandId = id;
            menu.Items.Add(mi);
        }
        menu.Closed += (_, _) => { deferral.Complete(); if (_previewMenu == menu) _previewMenu = null; };
        _previewMenu = menu;
        menu.IsOpen = true;
    }

    private ContextMenu? _previewMenu; // the open preview menu (read by the test automation)

    /// <summary>Serves local images referenced by the document. Nothing but image files is ever exposed.</summary>
    private void OnDocResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = Preview.CoreWebView2.Environment;
        string? path = MarkdownRenderer.LocalPathFromDocUrl(e.Request.Uri);
        if (path != null && ImageExtensions.Contains(Path.GetExtension(path)) && File.Exists(path))
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                string mime = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    ".bmp" => "image/bmp",
                    ".svg" => "image/svg+xml",
                    ".ico" => "image/x-icon",
                    ".avif" => "image/avif",
                    _ => "image/tiff",
                };
                e.Response = env.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK",
                    $"Content-Type: {mime}\r\nCache-Control: no-cache\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'");
                return;
            }
            catch { /* fall through to 404 */ }
        }
        e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
    }

    // ------------------------------------------------------------------ print / export

    private void PrintPreview()
    {
        if (Preview.CoreWebView2 == null) return;
        if (_mode == ViewMode.Edit) SetMode(ViewMode.Read, sync: true);
        try { Preview.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }
        catch (Exception ex) { AskDialog.Error(this, Loc.T("Dlg_CantPrint"), ex.Message); }
    }

    private async void ExportHtml()
    {
        if (Preview.CoreWebView2 == null || !_previewReady)
        {
            AskDialog.Error(this, Loc.T("Dlg_CantExport"), Loc.T("Dlg_PreviewNotReady"));
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = $"{Loc.T("Filter_Html")} (*.html)|*.html",
            FileName = Path.ChangeExtension(_session.DisplayName, ".html"),
            InitialDirectory = _session.FilePath != null ? Path.GetDirectoryName(_session.FilePath) : null,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        ExportHtmlTo(dlg.FileName);
    }

    private async void ExportHtmlTo(string fileName)
    {
        try
        {
            RenderNow(resetScroll: false);
            await Task.Delay(300);
            string json = await Preview.CoreWebView2.ExecuteScriptAsync("document.getElementById('content').innerHTML");
            string body = JsonSerializer.Deserialize<string>(json) ?? "";
            body = body.Replace($"https://{MarkdownRenderer.DocHost}/", "file:///");
            string css = File.ReadAllText(Path.Combine(AppPaths.WebDir, "app.css"));
            css = System.Text.RegularExpressions.Regex.Replace(css, @"@font-face\s*\{[^}]*\}", "");
            string theme = ThemeManager.IsDark ? "dark" : "light";
            string title = System.Net.WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(_session.DisplayName));
            string html = $"<!doctype html>\n<html lang=\"zh-CN\" data-theme=\"{theme}\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<title>{title}</title>\n<style>\n{css}\n</style>\n</head>\n<body>\n<main class=\"page\"><article class=\"doc\" data-style=\"mine\"><div class=\"inner\">\n{body}\n</div></article></main>\n</body>\n</html>\n";
            Core.IO.SafeFileWriter.Write(fileName, new System.Text.UTF8Encoding(false).GetBytes(html));
            ShowStatus(Loc.F("Status_Exported", Path.GetFileName(fileName)));
        }
        catch (Exception ex)
        {
            AskDialog.Error(this, Loc.T("Dlg_ExportFailed"), ex.Message);
        }
    }
}
