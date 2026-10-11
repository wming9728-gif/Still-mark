using System.Windows;
using System.Windows.Threading;
using InkPage.Localization;
using InkPage.Services;
using InkPage.Views;

namespace InkPage;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        string? path = null, restore = null, automation = null, lang = null;
        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--restore" && i + 1 < e.Args.Length) restore = e.Args[++i];
            else if (e.Args[i] == "--automation" && i + 1 < e.Args.Length) automation = e.Args[++i];
            else if (e.Args[i] == "--lang" && i + 1 < e.Args.Length) lang = e.Args[++i];
            else if (!e.Args[i].StartsWith("--", StringComparison.Ordinal)) path ??= e.Args[i];
        }

        var settings = AppSettings.Load();
        // --lang en|zh-Hans|system overrides the saved choice for this session only.
        Loc.Apply(Loc.Parse(lang) ?? settings.Language);
        ThemeManager.Apply(settings.Theme);
        var w = new MainWindow(settings, path, restore);
        MainWindow = w;
        w.Show();
#if AUTOMATION
        // Test builds only (see InkPage.csproj); release builds parse and ignore the argument.
        if (automation != null) new AutomationServer(automation, w.HandleAutomation).Start();
#endif
    }

    private DateTime _lastErrorShown = DateTime.MinValue;

    private const int ClipboardCantOpen = unchecked((int)0x800401D0); // CLIPBRD_E_CANT_OPEN

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Another program holding the clipboard makes WPF throw even when the editor merely checks whether
        // Paste is available. Nothing is lost; the next check or paste simply tries again.
        if (e.Exception is System.Runtime.InteropServices.COMException { HResult: ClipboardCantOpen })
        {
            e.Handled = true;
            return;
        }
        // Keep the user's text first, then report. Never show a modal window from here directly: the exception
        // may come from inside a layout/render pass where a nested message loop is not allowed.
        foreach (Window w in Windows)
            if (w is MainWindow mw) mw.EmergencyDraft();
        e.Handled = true;
        var ex = e.Exception;
        try
        {
            var log = System.IO.Path.Combine(AppPaths.DataDir, "error.log");
            System.IO.File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
        if ((DateTime.Now - _lastErrorShown).TotalSeconds < 10) return; // do not spam
        _lastErrorShown = DateTime.Now;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            AskDialog.Error(MainWindow, Loc.T("Dlg_Unexpected"), Loc.T("Dlg_Unexpected_Msg"), ex.ToString()));
    }
}