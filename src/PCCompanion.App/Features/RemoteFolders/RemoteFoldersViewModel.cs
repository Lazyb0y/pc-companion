using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.RemoteFolders;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.RemoteFolders;

/// <summary>A server in the page's list. What the provider says about it changes in place, so the list isn't rebuilt.</summary>
public sealed partial class RemoteServerItem : ObservableObject
{
    private string _state = string.Empty;
    private string _accessibleName = string.Empty;
    private string _problem = string.Empty;
    private bool _canSignInAgain;
    private bool _canTrustKey;
    private string _keyChange = string.Empty;
    private string _conflicts = string.Empty;
    private string _notMade = string.Empty;
    private string _hidden = string.Empty;
    private bool _canSync;

    public RemoteServerItem(int index, RemoteServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        Index = index;
        Server = server;
        Name = server.Name.Trim();
        Summary = Format(RemoteFoldersViewModel.SummaryFormat, RemoteFoldersText.Isolated(server.RemoteFolder), RemoteFoldersText.Isolated(RemoteServerCheck.Address(server)));
        FolderText = RemoteFoldersText.Isolated(server.Folder);
        IsUsable = server.IsUsable;
        OpenFolderName = Format(RemoteFoldersViewModel.OpenFolderFormat, Name);
        SyncNowName = Format(RemoteFoldersViewModel.SyncNowFormat, Name);
        MoreName = Format(RemoteFoldersViewModel.MoreFormat, Name);
    }

    public int Index { get; }

    public RemoteServer Server { get; }

    public string Name { get; }

    /// <summary>The folder on the server and the address, such as "/home/ana on ana@nas.example.lan:22".</summary>
    public string Summary { get; }

    public bool IsUsable { get; }

    /// <summary>The folder on this PC.</summary>
    public string FolderText { get; }

    public string OpenFolderName { get; }

    public string SyncNowName { get; }

    public string MoreName { get; }

    /// <summary>The state, such as "Connected, 2 changes waiting", or an empty string while Remote Folders is off.</summary>
    public string State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    /// <summary>The summary, and the state below it.</summary>
    public string Description => _state.Length == 0 ? Summary : Summary + Environment.NewLine + _state;

    /// <summary>The name with the state, as screen readers read the server, such as "Home server, connected".</summary>
    public string AccessibleName
    {
        get => _accessibleName;
        private set => SetProperty(ref _accessibleName, value);
    }

    /// <summary>What's wrong and what to do about it, or an empty string.</summary>
    public string Problem
    {
        get => _problem;
        private set
        {
            if (SetProperty(ref _problem, value))
            {
                OnPropertyChanged(nameof(HasProblem));
                OnPropertyChanged(nameof(HasDetails));
            }
        }
    }

    public bool HasProblem => _problem.Length > 0;

    public bool CanSignInAgain
    {
        get => _canSignInAgain;
        private set => SetProperty(ref _canSignInAgain, value);
    }

    public bool CanTrustKey
    {
        get => _canTrustKey;
        private set => SetProperty(ref _canTrustKey, value);
    }

    /// <summary>The key the user confirmed and the one the server shows now, one per line.</summary>
    public string KeyChange
    {
        get => _keyChange;
        private set
        {
            if (SetProperty(ref _keyChange, value))
            {
                OnPropertyChanged(nameof(HasKeyChange));
            }
        }
    }

    public bool HasKeyChange => _keyChange.Length > 0;

    /// <summary>The key the server shows now, for Trust the new key.</summary>
    public RemoteHostKey? PresentedKey { get; private set; }

    public string Conflicts
    {
        get => _conflicts;
        private set
        {
            if (SetProperty(ref _conflicts, value))
            {
                OnPropertyChanged(nameof(HasConflicts));
                OnPropertyChanged(nameof(HasDetails));
            }
        }
    }

    public bool HasConflicts => _conflicts.Length > 0;

    /// <summary>The changes the server refused and the files it didn't take.</summary>
    public string NotMade
    {
        get => _notMade;
        private set
        {
            if (SetProperty(ref _notMade, value))
            {
                OnPropertyChanged(nameof(HasNotMade));
                OnPropertyChanged(nameof(HasDetails));
            }
        }
    }

    public bool HasNotMade => _notMade.Length > 0;

    /// <summary>The entries on the server that can't be shown, by reason.</summary>
    public string Hidden
    {
        get => _hidden;
        private set
        {
            if (SetProperty(ref _hidden, value))
            {
                OnPropertyChanged(nameof(HasHidden));
                OnPropertyChanged(nameof(HasDetails));
            }
        }
    }

    public bool HasHidden => _hidden.Length > 0;

    /// <summary>Whether the server has a problem, conflicts, changes that weren't made or entries that aren't shown, so its card opens.</summary>
    public bool HasDetails => HasProblem || HasConflicts || HasNotMade || HasHidden;

    /// <summary>Whether Sync now is available: the server's folder is running.</summary>
    public bool CanSync
    {
        get => _canSync;
        private set => SetProperty(ref _canSync, value);
    }

    /// <summary>How many changes wait for the server, as last known.</summary>
    public int ChangesWaiting { get; private set; }

    /// <summary>
    /// Shows what the provider last said: <paramref name="answer"/> is null when it doesn't answer,
    /// and <paramref name="starting"/> says whether it may still be starting.
    /// </summary>
    /// <returns>Whether the state changed.</returns>
    public bool Update(bool isOn, RemoteFoldersAnswer? answer, bool starting)
    {
        var status = answer?.Servers.FirstOrDefault(server => string.Equals(server.Id, Server.Id, StringComparison.OrdinalIgnoreCase));
        string state = !isOn ? string.Empty
            : !IsUsable ? Strings.RemoteFoldersStateNotUsable
            : RemoteFoldersText.State(status, answer is not null, starting);
        bool changed = state != _state;
        State = state;
        AccessibleName = state.Length == 0 ? Name : Format(RemoteFoldersViewModel.AccessibleFormat, Name, state);

        var known = isOn ? status : null;
        Problem = !IsUsable ? Strings.RemoteFoldersNotUsableProblem
            : known?.State switch
            {
                RemoteServerState.SignInFailed => Strings.RemoteFoldersSignInFailedProblem,
                RemoteServerState.KeyChanged => Strings.RemoteFoldersKeyChangedProblem,
                RemoteServerState.NoSafeAlgorithm => Strings.RemoteFoldersNoSafeAlgorithmProblem,
                _ => string.Empty,
            };
        CanSignInAgain = known?.State == RemoteServerState.SignInFailed;
        PresentedKey = known is { State: RemoteServerState.KeyChanged, PresentedKey: { } presented } ? presented : null;
        CanTrustKey = PresentedKey is not null;
        KeyChange = PresentedKey is { } key && Server.HostKey is { } old
            ? Format(RemoteFoldersViewModel.OldKeyFormat, RemoteFoldersText.Isolated(RemoteServerCheck.KeyText(old))) + Environment.NewLine
                + Format(RemoteFoldersViewModel.NewKeyFormat, RemoteFoldersText.Isolated(RemoteServerCheck.KeyText(key)))
            : string.Empty;
        Conflicts = known is null ? string.Empty : RemoteFoldersText.Conflicts(known.Conflicts);
        NotMade = known is null ? string.Empty : RemoteFoldersText.NotMade(known.Skipped, known.FailedUploads);
        Hidden = known is null ? string.Empty : RemoteFoldersText.Hidden(known.Hidden);
        ChangesWaiting = known?.ChangesWaiting ?? 0;
        CanSync = known is not null;
        return changed;
    }

    private static string Format(CompositeFormat format, params object[] values) => string.Format(CultureInfo.CurrentCulture, format, values);
}

/// <summary>
/// The Remote Folders page. The servers are saved right away; a server is added or changed when
/// the server dialog's Save is chosen, after its password goes to Credential Manager. While the
/// page shows and the feature is on, it asks the provider for the servers' states every 2 seconds,
/// off the UI thread.
/// </summary>
public sealed partial class RemoteFoldersViewModel : FeatureViewModel<RemoteFoldersSettings>
{
    internal static readonly CompositeFormat SummaryFormat = CompositeFormat.Parse(Strings.RemoteFoldersServerSummary);
    internal static readonly CompositeFormat AccessibleFormat = CompositeFormat.Parse(Strings.RemoteFoldersServerAccessibleName);
    internal static readonly CompositeFormat OpenFolderFormat = CompositeFormat.Parse(Strings.RemoteFoldersOpenFolderName);
    internal static readonly CompositeFormat SyncNowFormat = CompositeFormat.Parse(Strings.RemoteFoldersSyncNowName);
    internal static readonly CompositeFormat MoreFormat = CompositeFormat.Parse(Strings.RemoteFoldersMoreName);
    internal static readonly CompositeFormat OldKeyFormat = CompositeFormat.Parse(Strings.RemoteFoldersOldKey);
    internal static readonly CompositeFormat NewKeyFormat = CompositeFormat.Parse(Strings.RemoteFoldersNewKey);
    private static readonly CompositeFormat RemovedFormat = CompositeFormat.Parse(Strings.RemoteFoldersRemoved);

    private static readonly TimeSpan StatusEvery = TimeSpan.FromSeconds(2);

    // How long the provider may not answer before the page says it isn't running: it starts within seconds.
    private static readonly TimeSpan StartsWithin = TimeSpan.FromSeconds(15);

    private readonly ILogger<RemoteFoldersViewModel> _logger;
    private readonly string _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private DispatcherQueueTimer? _timer;
    private bool _asking;
    private RemoteFoldersAnswer? _status;
    private Stopwatch? _unanswered;
    private bool _isNotRunning;
    private string _actionProblem = string.Empty;
    private string _removed = string.Empty;
    private string? _removedFolder;

    public RemoteFoldersViewModel(FeatureSettingsEditor editor, ILogger<RemoteFoldersViewModel> logger)
        : base(editor, RemoteFoldersFeature.Definition)
    {
        _logger = logger;
        RootFolder = RemoteServerCheck.RootFolder(_userProfile);
        RootFolderText = RemoteFoldersText.Isolated(RootFolder);
        IsProfileSupported = IsOnNtfs(_userProfile);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsOn))
            {
                _status = null;
                _unanswered = null;
                IsNotRunning = false;
                OnPropertyChanged(nameof(CanAdd));
                UpdateStates();
                _ = AskStatusAsync();
            }
        };
        RefreshServers();
    }

    /// <summary>Raised before <see cref="Servers"/> is rebuilt, which replaces every item.</summary>
    public event EventHandler? ServersRebuilding;

    /// <summary>Raised once <see cref="Servers"/> is rebuilt.</summary>
    public event EventHandler? ServersRebuilt;

    /// <summary>Raised with a server's new state, for screen readers.</summary>
    public event EventHandler<string>? StateChanged;

    public ObservableCollection<RemoteServerItem> Servers { get; } = [];

    public bool HasNoServers => Servers.Count == 0;

    /// <summary>The folder that holds the servers' folders.</summary>
    public string RootFolder { get; }

    public string RootFolderText { get; }

    /// <summary>Whether the user profile is on an NTFS drive, which Windows needs for the folders.</summary>
    public bool IsProfileSupported { get; }

    public bool IsProfileNotSupported => !IsProfileSupported;

    /// <summary>Whether Add server is available: it needs the provider, so the feature is on.</summary>
    public bool CanAdd => IsOn && IsProfileSupported && Settings.Servers.Count < RemoteFoldersSettings.MaximumServers;

    /// <summary>Whether the feature is on but the provider hasn't answered for a while.</summary>
    public bool IsNotRunning
    {
        get => _isNotRunning;
        private set => SetProperty(ref _isNotRunning, value);
    }

    /// <summary>Why the last action on a server failed, or an empty string.</summary>
    public string ActionProblem
    {
        get => _actionProblem;
        private set
        {
            if (SetProperty(ref _actionProblem, value))
            {
                OnPropertyChanged(nameof(HasActionProblem));
            }
        }
    }

    public bool HasActionProblem => _actionProblem.Length > 0;

    /// <summary>After a server was removed: where its files on this PC are.</summary>
    public string Removed
    {
        get => _removed;
        private set
        {
            if (SetProperty(ref _removed, value))
            {
                OnPropertyChanged(nameof(HasRemoved));
            }
        }
    }

    public bool HasRemoved => _removed.Length > 0;

    /// <summary>Whether Remote Folders is on, so the server dialog's Connect can ask the provider.</summary>
    public bool CanConnect => IsOn;

    /// <summary>Asks the provider through its pipe, off the UI thread.</summary>
    public static Task<RemoteFoldersAnswer?> AskAsync(RemoteFoldersRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => RemoteFoldersPipe.AskAsync(request, cancellationToken), cancellationToken);

    /// <summary>The server's saved password or key passphrase, or null.</summary>
    public string? SavedSecret(RemoteServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        try
        {
            return CredentialStore.Read(RemoteFoldersProcess.CredentialTarget(server.Id));
        }
        catch (Win32Exception exception)
        {
            LogCredentialFailed(_logger, exception.NativeErrorCode);
            return null;
        }
    }

    /// <summary>The other servers' names, which a server being added or edited can't have.</summary>
    public IReadOnlyList<string> NamesExcept(int index) =>
        [.. Settings.Servers.Where((_, at) => at != index).Select(server => server.Name)];

    /// <summary>Starts asking for the servers' states, as the page shows.</summary>
    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = StatusEvery;
        _timer.Tick += (_, _) => _ = AskStatusAsync();
        _timer.Start();
        _ = AskStatusAsync();
    }

    /// <summary>Stops asking, as the page goes.</summary>
    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>
    /// Adds a server from the dialog: its password or passphrase goes to Credential Manager first,
    /// so the provider finds it, then the server with a new ID and folder goes to the settings.
    /// </summary>
    public void Add(RemoteServer server, SecretChange secret)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(secret);

        ClearMessages();
        string? folder = RemoteServerCheck.NewFolder(RootFolder, server.Name, Settings.Servers.Select(other => other.Folder), Path.Exists);
        if (folder is null)
        {
            ActionProblem = Strings.RemoteFoldersNoFolderName;
            return;
        }

        var added = server with { Id = RemoteServer.NewId(), Folder = folder };
        if (!SaveSecret(added, secret))
        {
            return;
        }

        SaveServers([.. Settings.Servers, added]);
        LogAdded(_logger, Settings.Servers.Count);
    }

    /// <summary>Changes the server at <paramref name="index"/>, as edited in the dialog.</summary>
    public void Replace(int index, RemoteServer server, SecretChange secret)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(secret);
        if (index < 0 || index >= Settings.Servers.Count)
        {
            return;
        }

        ClearMessages();
        if (!SaveSecret(server, secret))
        {
            return;
        }

        if (server != Settings.Servers[index])
        {
            SaveServers([.. Settings.Servers.Select((existing, at) => at == index ? server : existing)]);
        }
    }

    /// <summary>
    /// Removes the server at <paramref name="index"/>: the provider then disconnects its folder,
    /// and its password or passphrase leaves Credential Manager. Its files on this PC stay.
    /// </summary>
    public void Remove(int index)
    {
        if (index < 0 || index >= Settings.Servers.Count)
        {
            return;
        }

        ClearMessages();
        var server = Settings.Servers[index];
        SaveServers([.. Settings.Servers.Where((_, at) => at != index)]);
        if (SaveFailed)
        {
            return;
        }

        try
        {
            CredentialStore.Delete(RemoteFoldersProcess.CredentialTarget(server.Id));
        }
        catch (Win32Exception exception)
        {
            LogCredentialFailed(_logger, exception.NativeErrorCode);
        }

        LogRemoved(_logger, index + 1);
        _removedFolder = Directory.Exists(server.Folder) ? server.Folder : null;
        Removed = _removedFolder is null ? string.Empty : string.Format(CultureInfo.CurrentCulture, RemovedFormat, server.Name.Trim());
    }

    /// <summary>Trusts the key the server shows now, once the provider checked that it still does.</summary>
    public async Task TrustNewKeyAsync(int index)
    {
        if (index < 0 || index >= Servers.Count || Servers[index].PresentedKey is not { } key)
        {
            return;
        }

        ClearMessages();
        var server = Servers[index].Server;
        var answer = await AskAsync(new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.CheckKey, ServerId = server.Id, Key = key }, CancellationToken.None).ConfigureAwait(true);
        if (answer?.KeyMatches != true)
        {
            ActionProblem = answer is null ? Strings.RemoteFoldersConnectNoAnswer : Strings.RemoteFoldersKeyNotShownNow;
            return;
        }

        int at = IndexOf(server);
        if (at >= 0)
        {
            SaveServers([.. Settings.Servers.Select((existing, place) => place == at ? existing with { HostKey = key } : existing)]);
            LogKeyTrusted(_logger, at + 1);
        }
    }

    /// <summary>Lists the server's folders again, and tries it again at once.</summary>
    public async Task SyncNowAsync(int index) => await SendAsync(index, RemoteFoldersRequestKind.SyncNow).ConfigureAwait(true);

    /// <summary>Tries the server again, with the password or passphrase as it's now saved.</summary>
    public async Task SignInAgainAsync(RemoteServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        await SendAsync(IndexOf(server), RemoteFoldersRequestKind.SignInAgain).ConfigureAwait(true);
    }

    /// <summary>Opens the server's folder in File Explorer; its conflicts are no longer listed.</summary>
    public void OpenFolder(int index)
    {
        if (index < 0 || index >= Servers.Count)
        {
            return;
        }

        var server = Servers[index].Server;
        OpenInExplorer(server.Folder);
        if (Servers[index].HasConflicts)
        {
            _ = SendAsync(index, RemoteFoldersRequestKind.ConflictsSeen);
        }
    }

    [RelayCommand]
    private void OpenRemoteFolders()
    {
        Directory.CreateDirectory(RootFolder);
        OpenInExplorer(RootFolder);
    }

    [RelayCommand]
    private void OpenRemovedFolder()
    {
        if (_removedFolder is { } folder)
        {
            OpenInExplorer(folder);
        }
    }

    private static bool IsOnNtfs(string folder)
    {
        try
        {
            return Path.GetPathRoot(folder) is { Length: > 0 } root && string.Equals(new DriveInfo(root).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private int IndexOf(RemoteServer server) =>
        Settings.Servers.ToList().FindIndex(existing => string.Equals(existing.Id, server.Id, StringComparison.OrdinalIgnoreCase));

    private void OpenInExplorer(string folder)
    {
        if (!Directory.Exists(folder))
        {
            ActionProblem = Strings.RemoteFoldersFolderNotThere;
            return;
        }

        try
        {
            string explorer = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var process = Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { folder } });
        }
        catch (Win32Exception exception)
        {
            LogCantOpenFolder(_logger, exception.NativeErrorCode);
        }
    }

    private async Task SendAsync(int index, RemoteFoldersRequestKind kind)
    {
        if (index < 0 || index >= Settings.Servers.Count)
        {
            return;
        }

        var answer = await AskAsync(new RemoteFoldersRequest { Kind = kind, ServerId = Settings.Servers[index].Id }, CancellationToken.None).ConfigureAwait(true);
        if (answer is not { Done: true })
        {
            LogNotDone(_logger, kind);
        }

        await AskStatusAsync().ConfigureAwait(true);
    }

    // Credential Manager first: a server the provider sees before its password would fail to sign in.
    private bool SaveSecret(RemoteServer server, SecretChange secret)
    {
        if (!secret.Change)
        {
            return true;
        }

        string target = RemoteFoldersProcess.CredentialTarget(server.Id);
        try
        {
            if (secret.Secret.Length == 0)
            {
                CredentialStore.Delete(target);
            }
            else
            {
                CredentialStore.Write(target, server.UserName, secret.Secret, server.Name);
            }

            return true;
        }
        catch (Win32Exception exception)
        {
            LogCredentialFailed(_logger, exception.NativeErrorCode);
            ActionProblem = Strings.RemoteFoldersCredentialFailed;
            return false;
        }
    }

    private void ClearMessages()
    {
        ActionProblem = string.Empty;
        Removed = string.Empty;
        _removedFolder = null;
    }

    private async Task AskStatusAsync()
    {
        if (_asking)
        {
            return;
        }

        _asking = true;
        try
        {
            var status = IsOn
                ? await AskAsync(new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.Status }, CancellationToken.None).ConfigureAwait(true)
                : null;
            _status = status;
            if (status is null && IsOn)
            {
                _unanswered ??= Stopwatch.StartNew();
            }
            else
            {
                _unanswered = null;
            }

            IsNotRunning = _unanswered?.Elapsed >= StartsWithin;
            UpdateStates();
        }
        finally
        {
            _asking = false;
        }
    }

    // Changes of state are announced once the provider answers, not as the page opens.
    private void UpdateStates()
    {
        foreach (var item in Servers)
        {
            if (item.Update(IsOn, _status, !IsNotRunning) && item.State.Length > 0 && _status is not null)
            {
                StateChanged?.Invoke(this, item.AccessibleName);
            }
        }
    }

    private void SaveServers(IReadOnlyList<RemoteServer> servers)
    {
        Save(Settings with { Servers = servers });
        RefreshServers();
    }

    private void RefreshServers()
    {
        ServersRebuilding?.Invoke(this, EventArgs.Empty);
        Servers.Clear();
        for (int index = 0; index < Settings.Servers.Count; index++)
        {
            var item = new RemoteServerItem(index, Settings.Servers[index]);
            item.Update(IsOn, _status, !IsNotRunning);
            Servers.Add(item);
        }

        OnPropertyChanged(nameof(HasNoServers));
        OnPropertyChanged(nameof(CanAdd));
        ServersRebuilt?.Invoke(this, EventArgs.Empty);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "A server was added; {Count} servers now.")]
    private static partial void LogAdded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server} was removed.")]
    private static partial void LogRemoved(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Server {Server}: its new key was trusted.")]
    private static partial void LogKeyTrusted(ILogger logger, int server);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credential Manager refused (error {Error}).")]
    private static partial void LogCredentialFailed(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File Explorer couldn't be started (error {Error}).")]
    private static partial void LogCantOpenFolder(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The Remote Folders process didn't do {Request}.")]
    private static partial void LogNotDone(ILogger logger, RemoteFoldersRequestKind request);
}
