using System.Globalization;
using System.Resources;

namespace StillMark.Core;

/// <summary>
/// User-facing messages of the core library, in the current UI culture (Resources/CoreStrings*.resx).
/// Exceptions created with <see cref="Mark{T}"/> carry a message meant to be shown to the user as is.
/// </summary>
public static class CoreText
{
    /// <summary>Key in <see cref="Exception.Data"/> marking a message written for the user.</summary>
    public const string UserMessageKey = "StillMark.UserMessage";

    private static readonly ResourceManager Strings = new("StillMark.Core.Resources.CoreStrings", typeof(CoreText).Assembly);

    public static string T(string key) => Strings.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentUICulture, T(key), args);

    internal static T Mark<T>(T ex) where T : Exception
    {
        ex.Data[UserMessageKey] = true;
        return ex;
    }

    public static bool IsUserMessage(Exception ex) => ex.Data.Contains(UserMessageKey);
}
