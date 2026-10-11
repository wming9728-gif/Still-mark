using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;

namespace InkPage.Localization;

public enum LanguageChoice { System, English, ChineseSimplified }

/// <summary>
/// UI strings. English lives in Strings.resx, Simplified Chinese in Strings.zh-Hans.resx; adding a language is one
/// more Strings.&lt;culture&gt;.resx file. XAML binds through <see cref="TrExtension"/>, so switching the language
/// updates every bound text at once; code-built text listens to <see cref="Changed"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private static readonly ResourceManager Strings = new("InkPage.Localization.Strings", typeof(Loc).Assembly);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo ChineseSimplified = CultureInfo.GetCultureInfo("zh-Hans");
    private static readonly CultureInfo SystemUi = CultureInfo.CurrentUICulture; // captured before we override it

    public static CultureInfo Culture { get; private set; } = Resolve(LanguageChoice.System);
    public static LanguageChoice Choice { get; private set; } = LanguageChoice.System;

    /// <summary>Raised after the language changed (on the UI thread).</summary>
    public static event Action? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Strings.GetString(key, Culture) ?? key;

    public static string T(string key) => Instance[key];
    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>Short code of the language in use: "en" or "zh-Hans".</summary>
    public static string Code => Culture.Name;

    /// <summary>The Windows display language the "System" choice follows.</summary>
    public static string SystemCultureName => SystemUi.Name;

    public static void Apply(LanguageChoice choice)
    {
        Choice = choice;
        Culture = Resolve(choice);
        // Also used by InkPage.Core for its messages.
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(Binding.IndexerName));
        Changed?.Invoke();
    }

    /// <summary>"System" follows the Windows display language: Simplified Chinese for zh-CN / zh-SG, English otherwise.</summary>
    public static CultureInfo Resolve(LanguageChoice choice) => choice switch
    {
        LanguageChoice.English => English,
        LanguageChoice.ChineseSimplified => ChineseSimplified,
        _ => IsSimplifiedChinese(SystemUi) ? ChineseSimplified : English,
    };

    private static bool IsSimplifiedChinese(CultureInfo c)
    {
        if (c.Name == "zh") return true;
        for (var x = c; !string.IsNullOrEmpty(x.Name); x = x.Parent)
            if (x.Name == "zh-Hans") return true;
        return false;
    }

    public static LanguageChoice? Parse(string? s) => s?.ToLowerInvariant() switch
    {
        "system" => LanguageChoice.System,
        "en" or "english" => LanguageChoice.English,
        "zh" or "zh-hans" or "zh-cn" => LanguageChoice.ChineseSimplified,
        _ => null,
    };
}

/// <summary>XAML: <c>Text="{l:Tr Key}"</c>. Bound, so it follows language changes without reloading the window.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension(string key) { Key = key; }

    [ConstructorArgument("key")]
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
