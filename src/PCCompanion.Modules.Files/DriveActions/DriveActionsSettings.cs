using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCCompanion.Core.Features;

namespace PCCompanion.Modules.Files.DriveActions;

/// <summary>What an entry in the list of drives stands for.</summary>
public enum DriveEntryKind
{
    /// <summary>One drive, recognized by its ID file and serial numbers.</summary>
    Drive,

    /// <summary>Any drive with a <c>DCIM</c> folder at its root, as camera cards have.</summary>
    CameraCards,
}

/// <summary>What happens when a drive in the list is connected.</summary>
public enum WhenConnectedChoice
{
    /// <summary>A notification asks; selecting it runs the drive's actions.</summary>
    Ask,

    /// <summary>The drive's actions run at once.</summary>
    Act,

    /// <summary>Nothing, until the user runs the actions from the page or the menu.</summary>
    Nothing,
}

/// <summary>What an action does.</summary>
public enum DriveActionKind
{
    /// <summary>Copies files from a folder on the drive to a folder on the PC.</summary>
    Import,

    /// <summary>Copies folders on the PC to a folder on the drive.</summary>
    BackUp,

    /// <summary>Opens a folder, on the drive or on the PC, in File Explorer.</summary>
    OpenFolder,
}

/// <summary>
/// One of a drive's actions. Which properties it uses depends on <see cref="Action"/>. See
/// "Actions" in docs/features/files/drive-actions.md.
/// </summary>
public sealed record DriveAction
{
    /// <summary>The folder imports start from, on the drive, without its letter.</summary>
    public const string DefaultFrom = "DCIM";

    /// <summary>The folder on the drive that backups go to, without its letter.</summary>
    public const string DefaultBackupDestination = "Backup";

    /// <summary>
    /// The action's name: <c>Import</c>, <c>BackUp</c> or <c>OpenFolder</c>. It's kept as written, so
    /// an action this version doesn't know, such as one from a newer version, keeps it when the
    /// settings are saved, and isn't used; see <see cref="KnownAction"/>.
    /// </summary>
    public string Action { get; set; } = nameof(DriveActionKind.Import);

    /// <summary>For an import: the folder on the drive, without its letter, whose files and subfolders are imported.</summary>
    public string From { get; set; } = DefaultFrom;

    /// <summary>For an import: the extensions without the dot, of which a file's must be one.</summary>
    public IReadOnlyList<string> Extensions { get; set; } = [];

    /// <summary>
    /// For an import: the folder on the PC, which may end in subfolders made from the file, such as
    /// <c>C:\Users\Ana\Pictures\{year}\{month}</c>. For a backup: the folder on the drive, without
    /// its letter, that the folders go into.
    /// </summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>For an import: <c>AddNumber</c> or <c>Skip</c>, kept as written as <see cref="Action"/> is.</summary>
    public string IfExists { get; set; } = nameof(NameConflict.AddNumber);

    /// <summary>For a backup: the folders on the PC, each copied into a folder of the same name in <see cref="Destination"/>.</summary>
    public IReadOnlyList<string> Folders { get; set; } = [];

    /// <summary>For Open a folder: a full path on the PC, or a folder on the drive without its letter.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>What the action does, or <see langword="null"/> when this version doesn't know it.</summary>
    [JsonIgnore]
    public DriveActionKind? KnownAction => Known<DriveActionKind>(Action);

    /// <summary>What an import does when the name is taken, or <see langword="null"/> when this version doesn't know it.</summary>
    [JsonIgnore]
    public NameConflict? KnownIfExists => Known<NameConflict>(IfExists);

    // Compares the lists item by item, so the same settings read twice are equal.
    public bool Equals(DriveAction? other) =>
        other is not null && Action == other.Action && From == other.From && Extensions.SequenceEqual(other.Extensions)
        && Destination == other.Destination && IfExists == other.IfExists && Folders.SequenceEqual(other.Folders) && Folder == other.Folder;

    public override int GetHashCode() => HashCode.Combine(Action, From, Extensions.Count, Destination, IfExists, Folders.Count, Folder);

    // The JSON reader checks nullable annotations on properties but not on a list's items, so a
    // null in the file's lists reads as one.
    internal DriveAction WithoutNulls() => this with
    {
        Extensions = [.. Extensions.Where(extension => extension is not null)],
        Folders = [.. Folders.Where(folder => folder is not null)],
    };

    internal static T? Known<T>(string name)
        where T : struct, Enum =>
        Enum.GetValues<T>().Cast<T?>().FirstOrDefault(value => string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A drive in the list, or Camera cards, with its actions, in order.</summary>
public sealed record DriveEntry
{
    /// <summary>The name the page, the menu and the notifications show, up to 60 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>Drive</c> or <c>CameraCards</c>, kept as written as an action's name is.</summary>
    public string Kind { get; set; } = nameof(DriveEntryKind.Drive);

    /// <summary>For a drive: the random ID in its ID file, as 32 hexadecimal digits.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>For a drive: the hash of its volume serial number.</summary>
    public string VolumeSerial { get; set; } = string.Empty;

    /// <summary>For a drive: the hash of its device's hardware serial number, empty for a device without one.</summary>
    public string HardwareSerial { get; set; } = string.Empty;

    /// <summary><c>Ask</c>, <c>Act</c> or <c>Nothing</c>, kept as written as an action's name is.</summary>
    public string WhenConnected { get; set; } = nameof(WhenConnectedChoice.Ask);

    /// <summary>Whether PC Companion asks Windows to eject the drive when its actions are done.</summary>
    public bool EjectWhenDone { get; set; }

    public IReadOnlyList<DriveAction> Actions { get; set; } = [];

    [JsonIgnore]
    public DriveEntryKind? KnownKind => DriveAction.Known<DriveEntryKind>(Kind);

    [JsonIgnore]
    public WhenConnectedChoice? KnownWhenConnected => DriveAction.Known<WhenConnectedChoice>(WhenConnected);

    // Compares the actions one by one, not the lists, so the same settings read twice are equal.
    public bool Equals(DriveEntry? other) =>
        other is not null && Name == other.Name && Kind == other.Kind && Id == other.Id && VolumeSerial == other.VolumeSerial
        && HardwareSerial == other.HardwareSerial && WhenConnected == other.WhenConnected && EjectWhenDone == other.EjectWhenDone
        && Actions.SequenceEqual(other.Actions);

    public override int GetHashCode() => HashCode.Combine(Name, Kind, Id, WhenConnected, EjectWhenDone, Actions.Count);

    internal DriveEntry WithoutNulls() => this with
    {
        Actions = [.. Actions.Where(action => action is not null).Select(action => action.WithoutNulls())],
    };
}

/// <summary>
/// Drive Actions settings, stored in <c>features.driveActions</c>. See docs/features/files/drive-actions.md.
/// </summary>
public sealed record DriveActionsSettings : IFeatureSettings<DriveActionsSettings>
{
    /// <summary>The most drives in the list, Camera cards included; later ones are left out.</summary>
    public const int MaximumDrives = 10;

    /// <summary>The most actions of a drive; later ones are left out.</summary>
    public const int MaximumActions = 10;

    /// <summary>The most folders of a backup.</summary>
    public const int MaximumFolders = 10;

    /// <summary>The longest name of a drive.</summary>
    public const int MaximumNameLength = 60;

    public bool Enabled { get; set; }

    public IReadOnlyList<DriveEntry> Drives { get; set; } = [];

    /// <summary>
    /// Reads the feature's section, or returns the defaults when there is none. A null in a list,
    /// such as after a hand edit, is left out.
    /// </summary>
    /// <exception cref="JsonException">The section doesn't match the format.</exception>
    public static DriveActionsSettings Read(JsonElement? section)
    {
        var settings = FeatureSettings.Read(section, DriveActionsJsonContext.Default.DriveActionsSettings, () => new DriveActionsSettings());
        return settings with { Drives = [.. settings.Drives.Where(drive => drive is not null).Select(drive => drive.WithoutNulls())] };
    }

    public JsonElement Write() => FeatureSettings.Write(this, DriveActionsJsonContext.Default.DriveActionsSettings);

    public DriveActionsSettings WithEnabled(bool enabled) => this with { Enabled = enabled };

    // Compares the drives one by one, not the lists, so the same settings read twice are equal.
    public bool Equals(DriveActionsSettings? other) =>
        other is not null && Enabled == other.Enabled && Drives.SequenceEqual(other.Drives);

    public override int GetHashCode() => HashCode.Combine(Enabled, Drives.Count);
}
