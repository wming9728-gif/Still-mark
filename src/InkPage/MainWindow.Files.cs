using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using InkPage.Core;
using InkPage.Core.IO;
using InkPage.Localization;
using InkPage.Services;
using InkPage.Views;
using Microsoft.Win32;

namespace InkPage;

/// <summary>File lifecycle: new / open / save / save as / external changes / recovery drafts.</summary>
public partial class MainWindow
{
    private static string FileFilter =>
        $"{Loc.T("Filter_Markdown")} (*.md;*.markdown;*.mdown;*.mkd)|*.md;*.markdown;*.mdown;*.mkd|{Loc.T("Filter_Text")} (*.txt)|*.txt|{Loc.T("Filter_All")} (*.*)|*.*";

    private readonly DraftStore _drafts = new(AppPaths.DraftsDir);
    private readonly string _draftId = Guid.NewGuid().ToString("N");
    private FileStream? _draftLock;
    private DispatcherTimer _draftTimer = null!;
    private int _textVersion;
    private int _draftedVersion = -1;
    private bool _draftExists;
    private int _draftGeneration;                 // bumped on discard so late background writes are dropped
    private readonly object _draftIoLock = new();

    private FileSystemWatcher? _watcher;
    private DispatcherTimer _watchDebounce = null!;
    private bool _checkingDisk;
    private bool _saving;
    private DateTime? _dismissedDiskStamp;
    private bool _closingConfirmed;

    private void InitFiles()
    {
        try { _draftLock = _drafts.AcquireLock(_draftId); } catch { /* recovery still works, just without the lock */ }
        _draftTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _draftTimer.Tick += (_, _) => WriteDraftIfNeeded();
        _draftTimer.Start();
        _watchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _watchDebounce.Tick += (_, _) => { _watchDebounce.Stop(); CheckExternalChange(); };
    }

    // ------------------------------------------------------------------ loading into the editor

    private void LoadSessionIntoEditor(DocumentSession s, string? textOverride, bool initial = false, bool keepView = false)
    {
        int caret = keepView ? Editor.CaretOffset : 0;
        double scroll = keepView ? Editor.VerticalOffset : 0;

        _session = s;
        _forceDirty = false;
        string text = textOverride ?? s.SavedText;
        _newLine = s.IsUntitled && s.SavedText.Length == 0 ? "\n" : LineEndings.Dominant(s.SavedText);

        var doc = new TextDocument(text);
        Editor.Document = doc;
        Editor.IsReadOnly = s.LoadProblem != null;
        Editor.Options.ConvertTabsToSpaces = !UsesTabIndentation(text);
        ClearEditorFind();

        if (keepView)
        {
            Editor.CaretOffset = Math.Min(caret, doc.TextLength);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Editor.ScrollToVerticalOffset(scroll));
        }
        _textVersion++;
        _lastDirtyShown = !IsDirty; // force refresh
        UpdateDirtyState();
        UpdateFormatStatus();
        UpdateCaretStatus();
        RenderNow(resetScroll: !keepView);
        WatchCurrentFile();
        _dismissedDiskStamp = null;
        if (_infoKind is InfoKind.External or InfoKind.Problem) HideInfo();

        if (!initial && s.FilePath != null)
        {
            _settings.AddRecent(s.FilePath);
            _settings.Save();
        }
    }

    private static bool UsesTabIndentation(string text)
    {
        int tabs = 0, spaces = 0;
        int i = 0;
        while (i < text.Length && tabs + spaces < 2000)
        {
            if (text[i] == '\t') tabs++;
            else if (text[i] == ' ' && i + 1 < text.Length && text[i + 1] == ' ') spaces++;
            int nl = text.IndexOf('\n', i);
            if (nl < 0) break;
            i = nl + 1;
        }
        return tabs > spaces;
    }

    private void OnEditorTextChanged()
    {
        _textVersion++;
        UpdateDirtyState();
        ScheduleRender();
        if (FindBar.Visibility == Visibility.Visible && _mode != ViewMode.Read) ScheduleEditorFindRefresh();
    }

    // ------------------------------------------------------------------ commands

    private void New_Click(object sender, RoutedEventArgs e) => NewDocument();
    private void Open_Click(object sender, RoutedEventArgs e) => OpenWithDialog();
    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void NewDocument()
    {
        if (!ConfirmLeaveCurrent()) return;
        DiscardDraft();
        LoadSessionIntoEditor(DocumentSession.CreateNew(), null);
        if (_mode == ViewMode.Read) SetMode(ViewMode.Split, sync: false);
        Editor.TextArea.Focus();
    }

    private void OpenWithDialog()
    {
        var dlg = new OpenFileDialog { Filter = FileFilter, Title = Loc.T("Dlg_OpenTitle") };
        if (_session.FilePath != null) dlg.InitialDirectory = Path.GetDirectoryName(_session.FilePath);
        if (dlg.ShowDialog(this) == true) OpenPath(dlg.FileName, confirm: true);
    }

    public void OpenPath(string path, bool confirm)
    {
        try { path = Path.GetFullPath(path); } catch { }
        if (_session.FilePath != null && string.Equals(_session.FilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            CheckExternalChange();
            return; // already open
        }
        if (confirm && !ConfirmLeaveCurrent()) return;

        DocumentSession s;
        try
        {
            if (Directory.Exists(path)) throw new IOException(Loc.T("Err_IsFolder")) { Data = { [CoreText.UserMessageKey] = true } };
            s = DocumentSession.Load(path);
        }
        catch (FileNotFoundException)
        {
            _settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            AskDialog.Error(this, Loc.T("Dlg_FileNotFound"), path);
            return;
        }
        catch (DirectoryNotFoundException)
        {
            AskDialog.Error(this, Loc.T("Dlg_FileNotFound"), path);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            AskDialog.Error(this, Loc.T("Dlg_NoReadPermission"), path);
            return;
        }
        catch (IOException ex)
        {
            AskDialog.Error(this, Loc.T("Dlg_CantOpen"), Friendly(ex), path);
            return;
        }

        DiscardDraft();
        LoadSessionIntoEditor(s, null);
        ShowOutlineForNewDocument();
        if (s.LoadProblem != null)
        {
            string problem = s.LoadProblem;
            ShowInfo(InfoKind.Problem, () => Loc.F("Info_CantEditSafely", problem), error: true, null,
                (() => Loc.T("Btn_EditAnyway"), () => { Editor.IsReadOnly = false; UpdateFormatStatus(); HideInfo(); }));
        }
        else if (IsReadOnlyOnDisk(s.FilePath!))
        {
            ShowInfo(InfoKind.Problem, () => Loc.T("Info_ReadOnlyOnDisk"));
        }
        ShowStatus(Loc.F("Status_Opened", s.DisplayName));
    }

    /// <summary>Plain-language explanation for common file errors (system messages may not match the UI language).</summary>
    private static string Friendly(Exception ex)
    {
        if (CoreText.IsUserMessage(ex)) return ex.Message; // already written for the user, in the UI language
        return ex switch
        {
            UnauthorizedAccessException => Loc.T("Err_Unauthorized"),
            DirectoryNotFoundException => Loc.T("Err_DirNotFound"),
            PathTooLongException => Loc.T("Err_PathTooLong"),
            FileNotFoundException => Loc.T("Err_NotFound"),
            IOException io when (io.HResult & 0xFFFF) is 32 or 33 => Loc.T("Err_InUse"),
            IOException io when (io.HResult & 0xFFFF) is 39 or 112 => Loc.T("Err_DiskFull"),
            _ => ex.Message,
        };
    }

    private static bool IsReadOnlyOnDisk(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly); } catch { return false; }
    }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancelled (or saving failed).</summary>
    private bool ConfirmLeaveCurrent()
    {
        if (!IsDirty) return true;
        int r = AskDialog.Show(this, Loc.F("Dlg_SaveChanges", _session.DisplayName),
            Loc.T("Dlg_SaveChanges_Msg"), [Loc.T("Btn_Save"), Loc.T("Btn_DontSave"), Loc.T("Btn_Cancel")], defaultIndex: 0, cancelIndex: 2, icon: AskIcon.Warning);
        switch (r)
        {
            case 0: return Save();
            case 1: DiscardDraft(); return true;
            default: return false;
        }
    }

    public bool Save()
    {
        if (_session.IsUntitled) return SaveAs();
        if (!_session.CanSaveInPlace || IsReadOnlyOnDisk(_session.FilePath!))
        {
            string why = _session.CanSaveInPlace ? Loc.T("Save_ReadOnlyWhy") : Loc.F("Save_UnsafeWhy", _session.LoadProblem);
            int r = AskDialog.Show(this, Loc.T("Dlg_CantSaveInPlace"), Loc.F("Dlg_CantSaveInPlace_Msg", why), [Loc.T("Btn_SaveAs"), Loc.T("Btn_Cancel")], 0, 1, AskIcon.Warning);
            return r == 0 && SaveAs();
        }

        string text = Editor.Document.Text;
        _saving = true;
        try
        {
            var outcome = _session.Save(text);
            if (outcome == SaveOutcome.Conflict)
            {
                var state = _session.CheckDisk(thorough: true);
                int r = state == DiskState.Deleted
                    ? AskDialog.Show(this, Loc.T("Dlg_Deleted"), Loc.F("Dlg_Deleted_Msg", _session.FilePath),
                        [Loc.T("Btn_RecreateSave"), Loc.T("Btn_SaveAs"), Loc.T("Btn_Cancel")], 0, 2, AskIcon.Warning)
                    : AskDialog.Show(this, Loc.T("Dlg_Modified"), Loc.T("Dlg_Modified_Msg"),
                        [Loc.T("Btn_Overwrite"), Loc.T("Btn_SaveAs"), Loc.T("Btn_Cancel")], 2, 2, AskIcon.Warning);
                if (r == 1) { _saving = false; return SaveAs(); }
                if (r != 0) return false;
                outcome = _session.Save(text, overwriteExternalChanges: true);
            }
            AfterSaved(outcome == SaveOutcome.Unchanged ? Loc.T("Status_NoChanges") : Loc.T("Status_Saved"));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            WriteDraftIfNeeded(force: true);
            string reason = Friendly(ex);
            int r = AskDialog.Show(this, Loc.T("Dlg_SaveFailed"), Loc.F("Dlg_SaveFailed_Msg", reason),
                [Loc.T("Btn_SaveAs"), Loc.T("Btn_Close")], 0, 1, AskIcon.Error, ex.Message);
            ShowInfo(InfoKind.Problem, () => Loc.F("Info_LastSaveFailed", reason), error: true);
            if (r == 0) { _saving = false; return SaveAs(); }
            return false;
        }
        finally
        {
            _saving = false;
        }
    }

    public bool SaveAs()
    {
        var dlg = new SaveFileDialog
        {
            Filter = FileFilter,
            Title = Loc.T("Dlg_SaveAs"),
            FileName = _session.DisplayName,
            DefaultExt = ".md",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (_session.FilePath != null) dlg.InitialDirectory = Path.GetDirectoryName(_session.FilePath);
        if (dlg.ShowDialog(this) != true) return false;
        return SaveAsPath(dlg.FileName);
    }

    public bool SaveAsPath(string path)
    {
        string text = Editor.Document.Text;
        _saving = true;
        try
        {
            _session.SaveAs(path, text);
            AfterSaved(Loc.T("Status_Saved"));
            WatchCurrentFile();
            _settings.AddRecent(_session.FilePath!);
            _settings.Save();
            RenderNow(resetScroll: false); // base path for relative images may have changed
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            WriteDraftIfNeeded(force: true);
            AskDialog.Error(this, Loc.T("Dlg_SaveAsFailed"), Loc.F("Dlg_SaveAsFailed_Msg", Friendly(ex)), ex.Message);
            return false;
        }
        finally
        {
            _saving = false;
        }
    }

    private void AfterSaved(string message)
    {
        _forceDirty = false;
        Editor.Document.UndoStack.MarkAsOriginalFile();
        _lastDirtyShown = !_lastDirtyShown;
        UpdateDirtyState();
        UpdateFormatStatus();
        DiscardDraft();
        Editor.IsReadOnly = false;
        if (_infoKind is InfoKind.External or InfoKind.Problem) HideInfo();
        ShowStatus($"{message} · {DateTime.Now:HH:mm:ss}");
    }

    private void ReloadFromDisk(bool keepView)
    {
        if (_session.FilePath == null) return;
        try
        {
            var s = DocumentSession.Load(_session.FilePath);
            DiscardDraft();
            LoadSessionIntoEditor(s, null, keepView: keepView);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AskDialog.Error(this, Loc.T("Dlg_ReloadFailed"), Friendly(ex));
        }
    }

    private void ReloadWithConfirm()
    {
        if (IsDirty)
        {
            int r = AskDialog.Show(this, Loc.T("Dlg_ReloadDiscard"), Loc.T("Dlg_ReloadDiscard_Msg"), [Loc.T("Btn_Reload"), Loc.T("Btn_Cancel")], 1, 1, AskIcon.Warning);
            if (r != 0) return;
        }
        ReloadFromDisk(keepView: true);
    }

    // ------------------------------------------------------------------ encoding / line endings (explicit user actions only)

    private void ConvertLineEndings(string nl)
    {
        var doc = Editor.Document;
        string text = doc.Text;
        string converted = LineEndings.Normalize(text, nl);
        _newLine = nl;
        if (converted != text)
        {
            int caretLine = Editor.TextArea.Caret.Line, caretCol = Editor.TextArea.Caret.Column;
            doc.Replace(0, doc.TextLength, converted);
            Editor.TextArea.Caret.Line = Math.Min(caretLine, doc.LineCount);
            Editor.TextArea.Caret.Column = caretCol;
        }
        UpdateFormatStatus();
        ShowStatus(converted != text ? Loc.T("Status_EolConverted") : Loc.T("Status_EolAlready"));
    }

    private void ChangeEncoding(TextFormat format)
    {
        _session.ChangeFormat(format);
        UpdateDirtyState();
        UpdateFormatStatus();
        ShowStatus(Loc.F("Status_EncodingSet", format.DisplayName));
    }

    // ------------------------------------------------------------------ external modification

    private void WatchCurrentFile()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_session.FilePath == null) return;
        try
        {
            var w = new FileSystemWatcher(Path.GetDirectoryName(_session.FilePath)!, Path.GetFileName(_session.FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            FileSystemEventHandler h = (_, _) => Dispatcher.BeginInvoke(() => { _watchDebounce.Stop(); _watchDebounce.Start(); });
            w.Changed += h; w.Created += h; w.Deleted += h;
            w.Renamed += (_, _) => Dispatcher.BeginInvoke(() => { _watchDebounce.Stop(); _watchDebounce.Start(); });
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch
        {
            // network paths etc.: fall back to checks on window activation
        }
    }

    private DateTime? CurrentDiskStamp()
    {
        try { return _session.FilePath != null && File.Exists(_session.FilePath) ? File.GetLastWriteTimeUtc(_session.FilePath) : DateTime.MinValue; }
        catch { return null; }
    }

    private void CheckExternalChange()
    {
        if (_checkingDisk || _saving || _session.IsUntitled || !IsLoaded) return;
        if (OwnedWindows.Count > 0) return; // a dialog is open; check again later
        _checkingDisk = true;
        try
        {
            var state = _session.CheckDisk();
            if (state == DiskState.Unchanged)
            {
                if (_infoKind == InfoKind.External) HideInfo();
                return;
            }
            var stamp = CurrentDiskStamp();
            if (_dismissedDiskStamp != null && stamp == _dismissedDiskStamp) return;

            if (state == DiskState.Modified)
            {
                if (!IsDirty && _session.CanSaveInPlace)
                {
                    ReloadFromDisk(keepView: true);
                    ShowInfo(InfoKind.General, () => Loc.T("Info_ReloadedExternal"), autoHide: TimeSpan.FromSeconds(6));
                }
                else
                {
                    ShowInfo(InfoKind.External, () => Loc.T("Info_ExternalConflict"), false, null,
                        (() => Loc.T("Btn_LoadDisk"), () => ReloadFromDisk(keepView: true)),
                        (() => Loc.T("Btn_KeepMine"), () => { _dismissedDiskStamp = CurrentDiskStamp(); HideInfo(); }));
                }
            }
            else if (state == DiskState.Deleted)
            {
                _forceDirty = true;
                UpdateDirtyState();
                ShowInfo(InfoKind.External, () => Loc.T("Info_DeletedExternal"), false, null,
                    (() => Loc.T("Btn_SaveAs"), () => SaveAs()));
            }
        }
        finally
        {
            _checkingDisk = false;
        }
    }

    // ------------------------------------------------------------------ recovery drafts

    private void WriteDraftIfNeeded(bool force = false)
    {
        if (!IsDirty) return;
        if (!force && _draftedVersion == _textVersion) return;
        int version = _textVersion;
        string text = Editor.Document.Text;
        string? path = _session.FilePath;
        string? baseSha = _session.KnownDisk?.Sha256;
        int gen = _draftGeneration;
        void Write()
        {
            lock (_draftIoLock)
            {
                if (gen != Volatile.Read(ref _draftGeneration)) return;
                _drafts.Save(_draftId, path, text, baseSha);
            }
        }
        try
        {
            if (force) Write();
            else Task.Run(() => { try { Write(); } catch { } });
            _draftedVersion = version;
            _draftExists = true;
        }
        catch
        {
            // drafts are best effort
        }
    }

    private void DiscardDraft()
    {
        _draftedVersion = -1;
        Interlocked.Increment(ref _draftGeneration);
        if (!_draftExists) return;
        lock (_draftIoLock) _drafts.Delete(_draftId);
        _draftExists = false;
    }

    public void EmergencyDraft()
    {
        try { WriteDraftIfNeeded(force: true); } catch { }
    }

    private void OfferDraftRecovery()
    {
        List<DraftInfo> drafts;
        try
        {
            drafts = _drafts.List().Where(d => d.Id != _draftId && !_drafts.IsLocked(d.Id)).ToList();
        }
        catch { return; }
        if (drafts.Count == 0) return;

        string list = string.Join(Environment.NewLine, drafts.Take(6).Select(d =>
            $"• {(d.OriginalPath != null ? Path.GetFileName(d.OriginalPath) : Loc.T("Draft_Untitled"))} — {d.SavedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} — {d.Preview}"));
        int r = AskDialog.Show(this, Loc.F("Dlg_Drafts", drafts.Count), Loc.T("Dlg_Drafts_Msg"),
            [Loc.T("Btn_Recover"), Loc.T("Btn_DiscardDrafts"), Loc.T("Btn_Later")], 0, 2, AskIcon.Info, list);
        if (r == 1)
        {
            foreach (var d in drafts) _drafts.Delete(d.Id);
            return;
        }
        if (r != 0) return;

        bool first = true;
        foreach (var d in drafts)
        {
            if (first && !IsDirty && _session.IsUntitled && Editor.Document.TextLength == 0)
            {
                RestoreDraft(d);
                first = false;
            }
            else
            {
                try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--restore {d.Id}") { UseShellExecute = false }); }
                catch { }
            }
        }
    }

    private void RestoreDraft(DraftInfo d)
    {
        string text;
        try { text = _drafts.ReadText(d.Id); }
        catch (Exception ex) { AskDialog.Error(this, Loc.T("Dlg_DraftReadFailed"), ex.Message); return; }

        var s = DocumentSession.CreateNew();
        string? noteKey = null;
        if (d.OriginalPath != null)
        {
            if (File.Exists(d.OriginalPath))
            {
                try
                {
                    s = DocumentSession.Load(d.OriginalPath);
                    if (d.BaseSha256 != null && s.KnownDisk != null && s.KnownDisk.Sha256 != d.BaseSha256)
                        noteKey = "Draft_NoteChanged";
                }
                catch { noteKey = "Draft_NoteUnreadable"; }
            }
            else noteKey = "Draft_NoteMissing";
        }
        LoadSessionIntoEditor(s, text);
        _forceDirty = true;
        UpdateDirtyState();
        WriteDraftIfNeeded(force: true); // the draft now lives under this window's id
        _drafts.Delete(d.Id);
        var savedAt = d.SavedAtUtc.ToLocalTime();
        string? original = d.OriginalPath;
        ShowInfo(InfoKind.General, () => Loc.F("Info_DraftRestored", savedAt, noteKey == null ? "" : Loc.F(noteKey, original)));
    }

    // ------------------------------------------------------------------ closing

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingConfirmed) return;
        if (!ConfirmLeaveCurrent())
        {
            e.Cancel = true;
            return;
        }
        _closingConfirmed = true;
        DiscardDraft();
        _draftTimer.Stop();
        _watcher?.Dispose();
        try { _draftLock?.Dispose(); } catch { }
        StoreWindowPlacement();
        _settings.Save();
    }
}
