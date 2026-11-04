using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.DriveActions;

/// <summary>A choice in Add a drive: Camera cards, or a drive connected now.</summary>
/// <param name="Index">Its place in the list, from 0.</param>
/// <param name="Details">What it is, or why it can't be added.</param>
/// <param name="Drive">The connected drive, or <see langword="null"/> for Camera cards.</param>
public sealed record AddDriveChoice(int Index, string Name, string Details, bool CanAdd, ConnectedDriveInfo? Drive);

/// <summary>
/// Add a drive: Camera cards, then the drives connected now that count, from the host, each with
/// its letter, kind and size, or why it can't be added. Add stays in the dialog, saying why, when
/// the drive can't be added.
/// </summary>
public sealed partial class AddDriveViewModel(DriveActionsViewModel page) : ObservableObject
{
    private static readonly CompositeFormat NameFormat = CompositeFormat.Parse(Strings.DriveActionsAddDriveName);
    private static readonly CompositeFormat DetailsFormat = CompositeFormat.Parse(Strings.DriveActionsAddDriveDetails);

    private string _status = string.Empty;
    private int _selected = -1;

    public ObservableCollection<AddDriveChoice> Choices { get; } = [];

    /// <summary>What the dialog is doing, or why a drive wasn't added, or an empty string.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => Status.Length > 0;

    public bool CanAdd => _selected >= 0 && _selected < Choices.Count && Choices[_selected].CanAdd;

    public void Select(int index)
    {
        _selected = index;
        OnPropertyChanged(nameof(CanAdd));
    }

    public async Task LoadAsync()
    {
        Choices.Clear();
        Choices.Add(new AddDriveChoice(
            0,
            Strings.DriveActionsCameraCards,
            page.HasCameraCards ? Strings.DriveActionsAddCameraCardsThere : Strings.DriveActionsAddCameraCardsDetails,
            !page.HasCameraCards,
            null));
        Status = Strings.DriveActionsAddLooking;

        var drives = await page.ConnectedDrivesAsync().ConfigureAwait(true);
        foreach (var drive in drives ?? [])
        {
            var name = drive.Label.Trim().Length > 0 ? drive.Label.Trim() : DriveActionsText.Of(drive.Kind);
            var details = drive.AddProblem == AddProblem.None
                ? string.Format(CultureInfo.CurrentCulture, DetailsFormat, DriveActionsText.Of(drive.Kind), ByteSizes.Format(drive.Size))
                : DriveActionsText.Of(drive.AddProblem);
            Choices.Add(new AddDriveChoice(
                Choices.Count,
                string.Format(CultureInfo.CurrentCulture, NameFormat, name, DriveActionsText.Letter(drive.Letter)),
                details,
                drive.AddProblem == AddProblem.None,
                drive));
        }

        Status = drives is null ? Strings.DriveActionsAddNotAnswering : drives.Count == 0 ? Strings.DriveActionsAddNone : string.Empty;
        OnPropertyChanged(nameof(CanAdd));
    }

    /// <summary>Adds the chosen drive; returns whether it did, and otherwise says why.</summary>
    public async Task<bool> AddAsync()
    {
        if (!CanAdd)
        {
            return false;
        }

        var choice = Choices[_selected];
        Status = choice.Drive is null ? page.AddCameraCards() : await page.AddDriveAsync(choice.Drive).ConfigureAwait(true);
        return Status.Length == 0;
    }
}
