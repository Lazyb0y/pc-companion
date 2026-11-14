using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PCCompanion.Host;

internal static partial class LanguageSetting
{
    private static CultureInfo? _windowsLanguage;

    /// <summary>
    /// Uses the language from the settings for the host's text, such as the tray menu.
    /// Without a language setting, the Windows display language is used.
    /// </summary>
    public static void Apply(string? language, ILogger logger)
    {
        // The first call happens before the host changes the language.
        _windowsLanguage ??= CultureInfo.CurrentUICulture;

        var culture = _windowsLanguage;
        if (!string.IsNullOrEmpty(language))
        {
            try
            {
                culture = CultureInfo.GetCultureInfo(language, predefinedOnly: true);
            }
            catch (CultureNotFoundException exception)
            {
                LogUnknownLanguage(logger, exception, language);
            }
        }

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The language setting {Language} is not a known language, so the Windows display language is used.")]
    private static partial void LogUnknownLanguage(ILogger logger, CultureNotFoundException exception, string language);
}
