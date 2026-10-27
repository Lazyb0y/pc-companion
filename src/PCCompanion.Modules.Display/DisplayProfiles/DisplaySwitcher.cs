using System;
using System.Collections.Generic;
using System.ComponentModel;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>How a switch to a profile went.</summary>
public enum SwitchOutcome
{
    /// <summary>The setup in use already is the profile's, so nothing changed.</summary>
    AlreadyInUse,

    /// <summary>A display that's on in the profile isn't connected, so nothing changed.</summary>
    NotConnected,

    /// <summary>Windows refused the setup, or the adapters have no source for each picture, so nothing changed.</summary>
    Refused,

    /// <summary>Applied and saved at once, since it can't leave a display black.</summary>
    Kept,

    /// <summary>Applied, not saved yet: the user is asked whether to keep it.</summary>
    Asking,
}

/// <summary>A switch's outcome, and what it found.</summary>
/// <param name="Question">For <see cref="SwitchOutcome.Asking"/>: the number of the question to ask, which its answer gives back.</param>
/// <param name="Error">For <see cref="SwitchOutcome.Refused"/>: Windows' error, if Windows refused.</param>
/// <param name="ModesNotOffered">For <see cref="SwitchOutcome.Refused"/>: the profile's displays whose mode Windows doesn't list.</param>
/// <param name="NotAsInProfile">The profile's displays that Windows applied in its own way.</param>
/// <param name="HdrError">Windows' error when it refused to turn HDR on or off, if it did.</param>
public sealed record SwitchResult(
    SwitchOutcome Outcome,
    int Question = 0,
    int? Error = null,
    IReadOnlyList<int>? ModesNotOffered = null,
    IReadOnlyList<int>? NotAsInProfile = null,
    int? HdrError = null);

/// <summary>How a switch that waited for an answer ended.</summary>
public enum AnswerOutcome
{
    /// <summary>The user kept it, and it's saved in Windows.</summary>
    Kept,

    /// <summary>The setup from before is back.</summary>
    Reverted,

    /// <summary>
    /// Windows refused the setup from before, such as after a display was unplugged, so the
    /// setup Windows saved last for the displays connected now is back.
    /// </summary>
    RevertedToSaved,
}

/// <summary>
/// Switches to profiles, and keeps or reverts a switch that could leave a display black once the
/// user answers, or can't. A setup is saved in Windows only once it's kept. Choosing another
/// profile while a switch waits switches again; reverting then goes back to the last setup that was
/// kept. Its methods wait for Windows, so they run on the thread pool, one at a time and in order.
/// See "Switching" and "Keep or revert" in docs/features/display/display-profiles.md.
/// </summary>
public sealed class DisplaySwitcher(IDisplaySystem system)
{
    // The setup that was kept last, while a switch waits for an answer; null otherwise.
    private DisplaySetup? _kept;

    // The number of the question a switch waits for; any other answer is late.
    private int _question;

    /// <summary>Whether a switch waits for an answer.</summary>
    public bool IsAsking => _kept is not null;

    /// <summary>
    /// Switches to a profile that passed the checks: checks the setup with Windows, applies it
    /// without saving, sets HDR, and then saves it at once or asks to keep it.
    /// </summary>
    /// <exception cref="Win32Exception">Windows refused to read the displays before the switch; nothing changed.</exception>
    public SwitchResult Switch(DisplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var current = system.Read();
        var recognition = ProfileRecognition.Recognize(profile, current);
        if (!recognition.CanBeUsed)
        {
            return new SwitchResult(SwitchOutcome.NotConnected);
        }

        if (ProfileRecognition.Matches(profile, current))
        {
            return new SwitchResult(SwitchOutcome.AlreadyInUse, IsAsking ? _question : 0);
        }

        var setup = ProfileSetup.Build(profile, current, recognition);
        if (setup is null)
        {
            return new SwitchResult(SwitchOutcome.Refused);
        }

        try
        {
            system.Check(setup);
        }
        catch (Win32Exception exception)
        {
            return Refused(exception);
        }

        // A switch while another waits is compared with the setup that was kept, not the one it waits about.
        var kept = _kept ?? current.Setup;
        var asks = ProfileSetup.AsksToKeep(kept, setup);
        try
        {
            system.Apply(setup);
        }
        catch (Win32Exception exception)
        {
            return Refused(exception);
        }

        // From here on the setup changed, so whatever happens next, it's kept or asked about.
        int? hdrError = null;
        IReadOnlyList<int> notAsInProfile = [];
        try
        {
            var applied = system.Read();
            var hdr = ProfileSetup.HdrToSet(setup, applied.Setup);
            foreach (var (target, on) in hdr)
            {
                try
                {
                    system.SetHdr(target, on);
                }
                catch (Win32Exception exception)
                {
                    hdrError = exception.NativeErrorCode;
                }
            }

            if (hdr.Count > 0)
            {
                applied = system.Read();
            }

            notAsInProfile = ProfileRecognition.DisplaysNotAsInProfile(profile, applied, ProfileRecognition.Recognize(profile, applied));
        }
        catch (Win32Exception)
        {
            // The displays can't be read now; the next change reads them again.
        }

        _question++;
        if (asks)
        {
            _kept = kept;
            return new SwitchResult(SwitchOutcome.Asking, _question, NotAsInProfile: notAsInProfile, HdrError: hdrError);
        }

        _kept = null;
        system.SaveCurrent();
        return new SwitchResult(SwitchOutcome.Kept, NotAsInProfile: notAsInProfile, HdrError: hdrError);

        SwitchResult Refused(Win32Exception exception) => new(
            SwitchOutcome.Refused,
            Error: exception.NativeErrorCode,
            ModesNotOffered: ProfileSetup.ModesNotOffered(profile, current, recognition, system.ListModes));
    }

    /// <summary>
    /// The user's answer to a question: keeping saves the setup in Windows; anything else reverts.
    /// </summary>
    /// <returns>How the switch ended; <see langword="null"/> for a question that's no longer asked.</returns>
    /// <exception cref="Win32Exception">Windows refused to save the setup, or to go back to its saved one.</exception>
    public AnswerOutcome? Answer(int question, bool kept)
    {
        if (question != _question || _kept is null)
        {
            return null;
        }

        if (!kept)
        {
            return Revert();
        }

        _kept = null;
        _question++;
        system.SaveCurrent();
        return AnswerOutcome.Kept;
    }

    /// <summary>
    /// Reverts a switch that waits for an answer, when the user can't answer: the PC locks, sleeps
    /// or switches to another user, the feature turns off, or PC Companion exits.
    /// </summary>
    /// <returns>How the switch ended; <see langword="null"/> when none waited.</returns>
    /// <exception cref="Win32Exception">Windows refused to go back to its saved setup.</exception>
    public AnswerOutcome? Interrupt() => _kept is null ? null : Revert();

    // Applies the setup that was kept, as Windows reported it, with its HDR; when Windows refuses
    // it, such as after a display was unplugged, goes back to the setup Windows saved.
    private AnswerOutcome Revert()
    {
        var kept = _kept!;
        _kept = null;
        _question++;
        try
        {
            system.Apply(kept.Paths);
        }
        catch (Exception exception) when (exception is Win32Exception or ArgumentException)
        {
            system.ApplySaved();
            return AnswerOutcome.RevertedToSaved;
        }

        try
        {
            foreach (var (target, on) in ProfileSetup.HdrToSet(kept.Paths, system.Read().Setup))
            {
                system.SetHdr(target, on);
            }
        }
        catch (Win32Exception)
        {
            // The setup is back; HDR stays as the switch left it.
        }

        return AnswerOutcome.Reverted;
    }
}
