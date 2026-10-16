using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PCCompanion.Host;

internal static partial class LanguageSetting
{
    /// <summary>
    /// Uses the language from the settings for the host's text, such as the tray menu.
    /// Without a language setting, the Windows display language is used.
    /// </summary>
    public static void Apply(string? language, ILogger logger)
    {
        if (string.IsNullOrEmpty(language))
        {
            return;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(language, predefinedOnly: true);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException exception)
        {
            LogUnknownLanguage(logger, exception, language);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The language setting {Language} is not a known language, so the Windows display language is used.")]
    private static partial void LogUnknownLanguage(ILogger logger, CultureNotFoundException exception, string language);
}
