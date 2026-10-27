using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using PCCompanion.Localization;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.DisplayProfiles;

/// <summary>A profile in the page's list.</summary>
/// <param name="Index">The profile's place in the list, from 0.</param>
/// <param name="Summary">A line for each display, and why the profile can't be used, if it can't.</param>
/// <param name="IsInUse">Whether the setup in use is the profile's.</param>
/// <param name="CanUse">Whether Use is available: the feature is on, and the profile is in the menu and can be used now.</param>
/// <param name="CanUpdate">Whether Update from current setup is available: the current setup is known.</param>
/// <param name="Picture">The profile's arrangement, for a profile in the menu; none for others.</param>
public sealed record DisplayProfileItem(
    int Index,
    DisplayProfile Profile,
    string Name,
    IReadOnlyList<PictureDisplay> Picture,
    double PictureWidth,
    double PictureHeight,
    string Summary,
    bool IsInUse,
    bool IsNotInUse,
    bool CanUse,
    bool CanUpdate,
    string UseName,
    string MoreName,
    bool CanMoveUp,
    bool CanMoveDown);

/// <summary>
/// The Display Profiles page. Every change is saved right away. While the page shows, it reads the
/// displays after each change, so In use and the problems follow changes made elsewhere. It never
/// switches the displays itself: Use asks the host.
/// </summary>
public sealed partial class DisplayProfilesViewModel : FeatureViewModel<DisplayProfilesSettings>
{
    /// <summary>The class name of the page's hidden window, apart from the host's.</summary>
    public const string WindowClassName = "PCCompanion.DisplayProfilesPage";

    private static readonly CompositeFormat UseFormat = CompositeFormat.Parse(Strings.DisplayProfilesUseName);
    private static readonly CompositeFormat MoreFormat = CompositeFormat.Parse(Strings.DisplayProfilesMoreName);
    private static readonly CompositeFormat RefusedFormat = CompositeFormat.Parse(Strings.DisplayProfilesUseRefused);
    private static readonly CompositeFormat ModesNotOfferedFormat = CompositeFormat.Parse(Strings.DisplayProfilesUseModesNotOffered);
    private static readonly CompositeFormat NotConnectedFormat = CompositeFormat.Parse(Strings.DisplayProfilesUseNotConnected);
    private static readonly CompositeFormat NotAsInProfileFormat = CompositeFormat.Parse(Strings.DisplayProfilesUseNotAsInProfile);
    private static readonly CompositeFormat CantReadFormat = CompositeFormat.Parse(Strings.DisplayProfilesUseCantRead);

    private readonly WindowsDisplayProfilesSystem _system = new(WindowClassName);
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly ILogger<DisplayProfilesViewModel> _logger;
    private IDisplayWindow? _window;
    private int _readWaiting;
    private CurrentSetup? _current;
    private bool _remote;
    private string _useProblem = string.Empty;

    public DisplayProfilesViewModel(FeatureSettingsEditor editor, ILogger<DisplayProfilesViewModel> logger)
        : base(editor, DisplayProfilesFeature.Definition)
    {
        _logger = logger;

        // Turning the feature on for the first time adds Original setup, and Use needs it on.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsOn))
            {
                RefreshProfiles();
            }
        };
        RefreshProfiles();
    }

    /// <summary>Raised before <see cref="Profiles"/> is rebuilt, which replaces every item.</summary>
    public event EventHandler? ProfilesRebuilding;

    /// <summary>Raised once <see cref="Profiles"/> is rebuilt.</summary>
    public event EventHandler? ProfilesRebuilt;

    public ObservableCollection<DisplayProfileItem> Profiles { get; } = [];

    public bool HasNoProfiles => Profiles.Count == 0;

    /// <summary>Whether a profile can be added from the current setup: it's known, and there's room for one.</summary>
    public bool CanAdd => _current is not null && Settings.Profiles.Count < DisplayProfilesSettings.MaximumProfiles;

    /// <summary>Whether this is a remote session, where profiles can't be used or added.</summary>
    public bool IsRemote => _remote;

    /// <summary>What the last Use did, when it didn't switch as asked; or an empty string.</summary>
    public string UseProblem
    {
        get => _useProblem;
        private set
        {
            if (SetProperty(ref _useProblem, value))
            {
                OnPropertyChanged(nameof(HasUseProblem));
            }
        }
    }

    public bool HasUseProblem => _useProblem.Length > 0;

    /// <summary>The other profiles' names, which a profile being added or renamed can't have.</summary>
    public IReadOnlyList<string> NamesExcept(int index) =>
        [.. Settings.Profiles.Where((_, at) => at != index).Select(profile => profile.Name)];

    /// <summary>Starts reading the displays after each change, as the page shows.</summary>
    public void Start()
    {
        if (_window is not null)
        {
            return;
        }

        try
        {
            _window = _system.CreateWindow(RequestRead, () => { });
        }
        catch (Win32Exception exception)
        {
            LogCantWatch(_logger, exception);
        }

        RequestRead();
    }

    /// <summary>Stops reading the displays, as the page goes.</summary>
    public void Stop()
    {
        _window?.Dispose();
        _window = null;
    }

    /// <summary>The current setup as a profile without a name, for Add and Update from current setup; null when it can't be read.</summary>
    public async Task<DisplayProfile?> CurrentSetupAsync()
    {
        if (_remote)
        {
            return null;
        }

        try
        {
            _current = await Task.Run(_system.Read).ConfigureAwait(true);
            return _current.ToProfile(string.Empty);
        }
        catch (Win32Exception exception)
        {
            LogCantRead(_logger, exception.NativeErrorCode);
            return null;
        }
    }

    /// <summary>Asks the host to switch to the profile at <paramref name="index"/>, as its menu item does.</summary>
    public async Task UseAsync(int index)
    {
        if (index < 0 || index >= Profiles.Count || !Profiles[index].CanUse)
        {
            return;
        }

        var item = Profiles[index];
        UseProblem = string.Empty;
        var answer = await DisplayProfilesHost.AskAsync(new DisplayProfilesRequest { Number = index + 1, Profile = item.Profile }).ConfigureAwait(true);
        UseProblem = Problem(item, answer);
        if (answer is { Status: DisplayProfilesAnswerStatus.Kept or DisplayProfilesAnswerStatus.Asking })
        {
            LogSwitched(_logger, index + 1, answer.Status);
        }
        else
        {
            LogNotSwitched(_logger, index + 1, answer?.Status, answer?.ErrorCode ?? 0);
        }

        RequestRead();
    }

    public void Add(DisplayProfile profile) => SaveProfiles([.. Settings.Profiles, profile]);

    public void Rename(int index, string name) =>
        SaveProfiles([.. Settings.Profiles.Select((existing, at) => at == index ? existing with { Name = name } : existing)]);

    /// <summary>Replaces a profile's setup with <paramref name="setup"/>'s, keeping its name.</summary>
    public void Update(int index, DisplayProfile setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        SaveProfiles([.. Settings.Profiles.Select((existing, at) => at == index ? existing with { Displays = setup.Displays } : existing)]);
    }

    public void Delete(int index) => SaveProfiles([.. Settings.Profiles.Where((_, at) => at != index)]);

    public void Move(int index, int offset)
    {
        var target = index + offset;
        if (index < 0 || target < 0 || index >= Settings.Profiles.Count || target >= Settings.Profiles.Count)
        {
            return;
        }

        var profiles = Settings.Profiles.ToList();
        (profiles[index], profiles[target]) = (profiles[target], profiles[index]);
        SaveProfiles(profiles);
    }

    [RelayCommand]
    private void OpenDisplaySettings()
    {
        if (!ShellActions.OpenDisplaySettings())
        {
            LogCantOpenDisplaySettings(_logger);
        }
    }

    private static string Problem(DisplayProfileItem item, DisplayProfilesAnswer? answer)
    {
        var profile = item.Profile;
        return answer?.Status switch
        {
            DisplayProfilesAnswerStatus.Kept or DisplayProfilesAnswerStatus.Asking or DisplayProfilesAnswerStatus.AlreadyInUse
                => answer.NotAsInProfile.Count == 0
                    ? string.Empty
                    : string.Format(CultureInfo.CurrentCulture, NotAsInProfileFormat, item.Name, DisplayProfilesText.Names(profile, answer.NotAsInProfile)),
            DisplayProfilesAnswerStatus.Refused when answer.ModesNotOffered.Count > 0
                => string.Format(CultureInfo.CurrentCulture, ModesNotOfferedFormat, item.Name, DisplayProfilesText.Names(profile, answer.ModesNotOffered)),
            DisplayProfilesAnswerStatus.Refused
                => string.Format(CultureInfo.CurrentCulture, RefusedFormat, item.Name, answer.ErrorCode.ToString(CultureInfo.CurrentCulture)),
            DisplayProfilesAnswerStatus.NotConnected
                => string.Format(CultureInfo.CurrentCulture, NotConnectedFormat, item.Name),
            DisplayProfilesAnswerStatus.Failed
                => string.Format(CultureInfo.CurrentCulture, CantReadFormat, answer.ErrorCode.ToString(CultureInfo.CurrentCulture)),
            DisplayProfilesAnswerStatus.UnknownProfile => Strings.DisplayProfilesUseNotRead,
            DisplayProfilesAnswerStatus.RemoteSession => Strings.DisplayProfilesRemoteInfo,
            _ => Strings.DisplayProfilesUseNoAnswer,
        };
    }

    // From any thread: one read, however many changes come meanwhile.
    private void RequestRead()
    {
        if (Interlocked.Exchange(ref _readWaiting, 1) == 0)
        {
            _ = Task.Run(Read);
        }
    }

    // On the thread pool. What Windows refuses to read stays as it was last read.
    private void Read()
    {
        Interlocked.Exchange(ref _readWaiting, 0);
        var remote = _system.IsRemoteSession;
        CurrentSetup? current = null;
        if (!remote)
        {
            try
            {
                current = _system.Read();
            }
            catch (Win32Exception exception)
            {
                LogCantRead(_logger, exception.NativeErrorCode);
            }
        }

        _dispatcher.TryEnqueue(() =>
        {
            _remote = remote;
            _current = remote ? null : current ?? _current;
            OnPropertyChanged(nameof(IsRemote));
            RefreshProfiles();
        });
    }

    private void SaveProfiles(IReadOnlyList<DisplayProfile> profiles)
    {
        Save(Settings with { Profiles = profiles });
        RefreshProfiles();
    }

    // The same checks, recognition and matching as the menu: a profile the menu leaves out isn't
    // used, and the first profile whose setup is in use is the one in use.
    private void RefreshProfiles()
    {
        ProfilesRebuilding?.Invoke(this, EventArgs.Empty);
        Profiles.Clear();
        var profiles = Settings.Profiles;
        var usableNames = new List<string>();
        var inUseFound = false;
        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            var problem = DisplayProfileCheck.Check(profile, usableNames);
            var usable = problem == DisplayProfileProblem.None && index < DisplayProfilesSettings.MaximumProfiles;
            var lines = new List<string>();
            (IReadOnlyList<PictureDisplay> Displays, double Width, double Height) picture = ([], 0, 0);
            var canBeUsed = false;
            var inUse = false;
            if (index >= DisplayProfilesSettings.MaximumProfiles)
            {
                lines.Add(Strings.DisplayProfilesTooMany);
            }
            else if (!usable)
            {
                lines.Add(DisplayProfilesText.NotInMenu(problem));
            }
            else
            {
                usableNames.Add(profile.Name);
                picture = DisplayProfilesPicture.Draw(profile);
                lines.AddRange(DisplayProfilesText.Lines(profile));
                if (_current is { } current)
                {
                    var recognition = ProfileRecognition.Recognize(profile, current);
                    canBeUsed = recognition.CanBeUsed;
                    inUse = canBeUsed && !inUseFound && ProfileRecognition.Matches(profile, current);
                    if (!canBeUsed)
                    {
                        lines.Add(DisplayProfilesText.NotConnected(profile, recognition));
                    }
                    else if (recognition.InConnectionOrder)
                    {
                        lines.Add(Strings.DisplayProfilesInConnectionOrder);
                    }
                }
            }

            inUseFound |= inUse;
            var name = profile.Name.Trim();
            Profiles.Add(new DisplayProfileItem(
                index,
                profile,
                name,
                picture.Displays,
                picture.Width,
                picture.Height,
                string.Join(Environment.NewLine, lines),
                inUse,
                !inUse,
                IsOn && usable && canBeUsed && !inUse && !_remote,
                _current is not null && !_remote,
                string.Format(CultureInfo.CurrentCulture, UseFormat, name),
                string.Format(CultureInfo.CurrentCulture, MoreFormat, name),
                index > 0,
                index < profiles.Count - 1));
        }

        OnPropertyChanged(nameof(HasNoProfiles));
        OnPropertyChanged(nameof(CanAdd));
        ProfilesRebuilt?.Invoke(this, EventArgs.Empty);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't watch the displays, so In use shows the setup as it was when last read.")]
    private static partial void LogCantWatch(ILogger logger, Win32Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows refused to read the displays (error {Error}).")]
    private static partial void LogCantRead(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Display profile {Number} was used from the page: {Status}.")]
    private static partial void LogSwitched(ILogger logger, int number, DisplayProfilesAnswerStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Display profile {Number} wasn't switched to from the page: {Status} (error {Error}).")]
    private static partial void LogNotSwitched(ILogger logger, int number, DisplayProfilesAnswerStatus? status, int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't open the Display page of Windows Settings.")]
    private static partial void LogCantOpenDisplaySettings(ILogger logger);
}
