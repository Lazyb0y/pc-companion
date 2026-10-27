using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Localization;
using PCCompanion.Modules.Display.DisplayProfiles;

namespace PCCompanion.App.Features.DisplayProfiles;

/// <summary>
/// The name dialog's name, what's wrong with it, and the setup the profile holds, which the
/// dialog shows but doesn't change. Save is available when the name has no problem; the problem
/// shows once the box has something in it.
/// </summary>
public sealed partial class DisplayProfileNameEditor : ObservableObject
{
    private readonly IReadOnlyList<string> _otherNames;
    private string _name;

    /// <param name="profile">The profile to name: one made from the current setup, or one being renamed.</param>
    /// <param name="isNew">Whether Save adds the profile, rather than renaming it.</param>
    /// <param name="otherNames">The other profiles' names, which this one can't have.</param>
    public DisplayProfileNameEditor(DisplayProfile profile, bool isNew, IReadOnlyList<string> otherNames)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(otherNames);

        IsNew = isNew;
        _otherNames = otherNames;
        _name = profile.Name;
        Setup = string.Join(Environment.NewLine, DisplayProfilesText.Lines(profile));
    }

    public bool IsNew { get; }

    public string Title => IsNew ? Strings.DisplayProfilesAddTitle : Strings.DisplayProfilesRenameTitle;

    /// <summary>A line for each of the profile's displays.</summary>
    public string Setup { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(NameProblem));
                OnPropertyChanged(nameof(HasNameProblem));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public string NameProblem => _name.Length == 0 ? string.Empty : DisplayProfilesText.Problem(Problem);

    public bool HasNameProblem => NameProblem.Length > 0;

    public bool CanSave => Problem == DisplayProfileProblem.None;

    private DisplayProfileProblem Problem => DisplayProfileCheck.CheckName(_name, _otherNames);

    /// <summary>The name as it's saved, without spaces at either end.</summary>
    public string ToName() => _name.Trim();
}
