using System;
using System.Collections.Generic;
using System.Linq;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>What a connected drive is to Drive Actions.</summary>
public enum RecognitionOutcome
{
    /// <summary>It doesn't count, such as one of the PC's own disks.</summary>
    NotCounted,

    /// <summary>BitLocker keeps it locked, so it can't be recognized until it's unlocked.</summary>
    Locked,

    /// <summary>It's a drive in the list: its ID file and serial numbers match.</summary>
    Recognized,

    /// <summary>It has a drive's ID file, but other serial numbers: a copy of the drive, or the same card in another reader.</summary>
    LookAlike,

    /// <summary>It isn't in the list, and has a <c>DCIM</c> folder, while Camera cards is in the list.</summary>
    CameraCards,

    /// <summary>It isn't in the list.</summary>
    NotRecognized,
}

/// <param name="Drive">For <see cref="RecognitionOutcome.Recognized"/>, <see cref="RecognitionOutcome.LookAlike"/> and <see cref="RecognitionOutcome.CameraCards"/>: its number in the list.</param>
public sealed record Recognition(RecognitionOutcome Outcome, int Drive = -1);

/// <summary>Why a connected drive can't be added to the list.</summary>
public enum AddProblem
{
    None,

    /// <summary>It doesn't count, such as one of the PC's own disks.</summary>
    NotCounted,

    /// <summary>BitLocker keeps it locked.</summary>
    Locked,

    /// <summary>Nothing can be written to it, so it can't get an ID file.</summary>
    ReadOnly,

    /// <summary>It's in the list already.</summary>
    AlreadyAdded,

    /// <summary>It has the ID file of a drive in the list, with other serial numbers; Use this drive is for that.</summary>
    LookAlike,
}

/// <summary>
/// Recognizes connected drives by their ID file and serial numbers, never by their names. See
/// "Recognizing a drive" in docs/features/files/drive-actions.md.
/// </summary>
public static class DriveRecognizer
{
    /// <summary>
    /// What <paramref name="connected"/> is among <paramref name="drives"/>, the list's drives, of which
    /// only those whose review in <paramref name="reviews"/> has no problem count.
    /// </summary>
    public static Recognition Recognize(ConnectedDrive connected, IReadOnlyList<DriveEntry> drives, IReadOnlyList<DriveReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(connected);
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(reviews);

        if (connected.Kind is null)
        {
            return new Recognition(RecognitionOutcome.NotCounted);
        }

        if (connected.IsLocked)
        {
            return new Recognition(RecognitionOutcome.Locked);
        }

        var usable = Enumerable.Range(0, Math.Min(drives.Count, reviews.Count)).Where(index => reviews[index].Problem == DriveProblem.None).ToArray();
        if (connected.IdFileText is { } text && DriveIdentity.ReadId(text) is { } id)
        {
            var withId = usable.Where(index => drives[index].KnownKind == DriveEntryKind.Drive && drives[index].Id == id).ToArray();
            if (withId.Length > 0)
            {
                var volumeSerial = DriveIdentity.HashVolumeSerial(connected.VolumeSerial);
                var hardwareSerial = DriveIdentity.HashHardwareSerial(connected.HardwareSerial);
                foreach (var index in withId)
                {
                    if (drives[index].VolumeSerial == volumeSerial && drives[index].HardwareSerial == hardwareSerial)
                    {
                        return new Recognition(RecognitionOutcome.Recognized, index);
                    }
                }

                // Nothing goes to or comes from a drive that carries another drive's ID.
                return new Recognition(RecognitionOutcome.LookAlike, withId[0]);
            }
        }

        if (connected.HasDcim && usable.FirstOrDefault(index => drives[index].KnownKind == DriveEntryKind.CameraCards, -1) is >= 0 and var cards)
        {
            return new Recognition(RecognitionOutcome.CameraCards, cards);
        }

        return new Recognition(RecognitionOutcome.NotRecognized);
    }

    /// <summary>Why <paramref name="connected"/> can't be added to the list, or <see cref="AddProblem.None"/>.</summary>
    public static AddProblem CheckAdd(ConnectedDrive connected, IReadOnlyList<DriveEntry> drives, IReadOnlyList<DriveReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(connected);

        var recognition = Recognize(connected, drives, reviews);
        return recognition.Outcome switch
        {
            RecognitionOutcome.NotCounted => AddProblem.NotCounted,
            RecognitionOutcome.Locked => AddProblem.Locked,
            RecognitionOutcome.Recognized => AddProblem.AlreadyAdded,
            RecognitionOutcome.LookAlike => AddProblem.LookAlike,
            _ when connected.IsReadOnly => AddProblem.ReadOnly,
            _ => AddProblem.None,
        };
    }

    /// <summary>
    /// The list's entry for <paramref name="connected"/>, added with the ID <paramref name="id"/>
    /// that was written to its ID file, named after its label or, without one, <paramref name="fallbackName"/>.
    /// </summary>
    public static DriveEntry NewEntry(ConnectedDrive connected, string id, string fallbackName)
    {
        ArgumentNullException.ThrowIfNull(connected);
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(fallbackName);

        var name = connected.Label.Trim();
        return new DriveEntry
        {
            Name = name.Length == 0 ? fallbackName : name[..Math.Min(name.Length, DriveActionsSettings.MaximumNameLength)],
            Kind = nameof(DriveEntryKind.Drive),
            Id = id,
            VolumeSerial = DriveIdentity.HashVolumeSerial(connected.VolumeSerial),
            HardwareSerial = DriveIdentity.HashHardwareSerial(connected.HardwareSerial),
        };
    }

    /// <summary>
    /// <paramref name="drive"/> with the serial numbers of <paramref name="connected"/>, which has its
    /// ID file, for Use this drive after the user confirms.
    /// </summary>
    public static DriveEntry WithSerialsOf(DriveEntry drive, ConnectedDrive connected)
    {
        ArgumentNullException.ThrowIfNull(drive);
        ArgumentNullException.ThrowIfNull(connected);

        return drive with
        {
            VolumeSerial = DriveIdentity.HashVolumeSerial(connected.VolumeSerial),
            HardwareSerial = DriveIdentity.HashHardwareSerial(connected.HardwareSerial),
        };
    }
}
