using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InkPage.Localization;
using InkPage.Services;

namespace InkPage.Views;

public enum AskIcon { Question, Warning, Error, Info }

/// <summary>Themed replacement for MessageBox. Returns the index of the clicked button, or -1 when dismissed.</summary>
public partial class AskDialog : Window
{
    private int _result = -1;
    private readonly int _cancelIndex;

    private AskDialog(string title, string message, string[] buttons, int defaultIndex, int cancelIndex, AskIcon icon, string? detail)
    {
        InitializeComponent();
        _cancelIndex = cancelIndex;
        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        IconText.Text = icon switch
        {
            AskIcon.Warning => "",
            AskIcon.Error => "",
            AskIcon.Info => "",
            _ => "",
        };
        if (icon is AskIcon.Warning or AskIcon.Error)
            IconText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Dirty");
        if (!string.IsNullOrEmpty(detail))
        {
            DetailBox.Text = detail;
            DetailBox.Visibility = Visibility.Visible;
        }
        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            var b = new Button
            {
                Content = buttons[i],
                Margin = new Thickness(8, 0, 0, 0),
                Style = (Style)FindResource(i == defaultIndex ? "PrimaryButton" : "TextButton"),
                IsDefault = i == defaultIndex,
                IsCancel = i == cancelIndex,
            };
            AutomationProperties_SetName(b, buttons[i]);
            b.Click += (_, _) => { _result = idx; DialogResult = true; };
            ButtonPanel.Children.Add(b);
            if (i == defaultIndex) Loaded += (_, _) => b.Focus();
        }
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _cancelIndex < 0) { _result = -1; DialogResult = false; }
        };
    }

    public string TitleValue => TitleText.Text;
    public string MessageValue => MessageText.Text;
    public IEnumerable<string> ButtonLabels => ButtonPanel.Children.OfType<Button>().Select(b => b.Content?.ToString() ?? "");

    /// <summary>Clicks button <paramref name="index"/> (used by the test automation channel).</summary>
    public void Answer(int index)
    {
        if (ButtonPanel.Children[index] is Button b) b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void AutomationProperties_SetName(DependencyObject o, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(o, name);

    public static int Show(Window? owner, string title, string message, string[] buttons,
        int defaultIndex = 0, int cancelIndex = -1, AskIcon icon = AskIcon.Question, string? detail = null)
    {
        var d = new AskDialog(title, message, buttons, defaultIndex, cancelIndex, icon, detail);
        if (owner != null && owner.IsLoaded) d.Owner = owner;
        else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        d.ShowDialog();
        return d._result < 0 && cancelIndex >= 0 ? cancelIndex : d._result;
    }

    public static void Error(Window? owner, string title, string message, string? detail = null) =>
        Show(owner, title, message, [Loc.T("Btn_OK")], 0, 0, AskIcon.Error, detail);

    public static void Info(Window? owner, string title, string message) =>
        Show(owner, title, message, [Loc.T("Btn_OK")], 0, 0, AskIcon.Info);
}
