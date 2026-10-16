using System;
using System.Text.RegularExpressions;

namespace PCCompanion.Logging;

/// <summary>
/// Replaces the user's profile folder in log text with <c>%USERPROFILE%</c>, so paths in logs
/// don't show the user name.
/// </summary>
public sealed class PathRedactor
{
    public const string Placeholder = "%USERPROFILE%";

    private readonly Regex? _userProfile;

    public PathRedactor(string? userProfile)
    {
        userProfile = userProfile?.TrimEnd('\\', '/');
        if (!string.IsNullOrEmpty(userProfile))
        {
            // Not followed by more of a folder name, so C:\Users\Al doesn't match C:\Users\Alex.
            _userProfile = new Regex(
                Regex.Escape(userProfile) + @"(?![\w.\-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    public static PathRedactor ForCurrentUser() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _userProfile?.Replace(text, Placeholder) ?? text;
    }
}
