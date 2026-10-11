using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace InkPage.Services;

public enum ThemeChoice { System, Light, Dark }

/// <summary>Light/dark palettes for the window chrome, matching the preview sheet's colours in web/app.css.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    private static readonly Dictionary<string, string> Light = new()
    {
        ["Brush.Page"] = "#EEF0F2", ["Brush.Surface"] = "#FFFFFF", ["Brush.Fg"] = "#23272E", ["Brush.Mute"] = "#6A717C",
        ["Brush.Line"] = "#D6DADF", ["Brush.Pick"] = "#23272E", ["Brush.PickFg"] = "#FFFFFF", ["Brush.Hover"] = "#E2E5E9",
        ["Brush.Pressed"] = "#D3D7DC", ["Brush.Accent"] = "#2F6FDE", ["Brush.Info"] = "#FFF6E0", ["Brush.InfoLine"] = "#EED9A4",
        ["Brush.Error"] = "#FDECEC", ["Brush.ErrorLine"] = "#F2B8B8", ["Brush.Menu"] = "#FFFFFF", ["Brush.Thumb"] = "#C4C9CF",
        ["Brush.Dirty"] = "#D97706",
    };

    private static readonly Dictionary<string, string> Dark = new()
    {
        ["Brush.Page"] = "#15171B", ["Brush.Surface"] = "#1C1F24", ["Brush.Fg"] = "#E6E8EB", ["Brush.Mute"] = "#8D949E",
        ["Brush.Line"] = "#2C3036", ["Brush.Pick"] = "#E6E8EB", ["Brush.PickFg"] = "#15171B", ["Brush.Hover"] = "#262A30",
        ["Brush.Pressed"] = "#30353C", ["Brush.Accent"] = "#6EA4FF", ["Brush.Info"] = "#2B2618", ["Brush.InfoLine"] = "#4A3F1E",
        ["Brush.Error"] = "#3A1F1F", ["Brush.ErrorLine"] = "#5C2B2B", ["Brush.Menu"] = "#22252A", ["Brush.Thumb"] = "#3A3F46",
        ["Brush.Dirty"] = "#F0A46A",
    };

    public static event Action? Changed;

    public static bool ResolveDark(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Light => false,
        ThemeChoice.Dark => true,
        _ => SystemPrefersDark(),
    };

    public static void Apply(ThemeChoice choice)
    {
        IsDark = ResolveDark(choice);
        var res = Application.Current.Resources;
        foreach (var (key, hex) in IsDark ? Dark : Light)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            res[key] = brush;
        }
        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        Changed?.Invoke();
    }

    public static Color Color(string key) => ((SolidColorBrush)Application.Current.Resources[key]).Color;

    /// <summary>Reads (never writes) the user's app theme preference.</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Per-window title bar colour (DWM attribute on our own window only; no system setting is touched).</summary>
    public static void ApplyTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 from Windows 10 2004 on and 19 on earlier Windows 10 builds.
        if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        // Caption and text colours exist on Windows 11 only; Windows 10 keeps its light/dark title bar.
        var c = Color("Brush.Page");
        int colorRef = c.R | (c.G << 8) | (c.B << 16);
        DwmSetWindowAttribute(hwnd, 35, ref colorRef, sizeof(int)); // DWMWA_CAPTION_COLOR
        var t = Color("Brush.Fg");
        int textRef = t.R | (t.G << 8) | (t.B << 16);
        DwmSetWindowAttribute(hwnd, 36, ref textRef, sizeof(int)); // DWMWA_TEXT_COLOR
    }
}
