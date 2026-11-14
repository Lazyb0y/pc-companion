using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.RemoteFolders;

namespace PCCompanion.App.Features.RemoteFolders;

/// <summary>What Save does with the server's password or key passphrase in Credential Manager.</summary>
/// <param name="Change">Whether it changes; otherwise the saved one stays.</param>
/// <param name="Secret">The new one, or an empty string to delete the saved one, as for a key without a passphrase.</param>
public sealed record SecretChange(bool Change, string Secret)
{
    public static SecretChange Keep { get; } = new(false, string.Empty);
}

/// <summary>
/// The server dialog's fields, what's wrong with them, and <see cref="ConnectAsync"/>, which asks
/// the provider to read the server's key and then to sign in, so the settings app never connects
/// itself. Adding a server: Save is available once the key is checked and the details as they are
/// now have signed in and found the folder. Editing one: only its name and how it signs in change,
/// and a new sign-in has to work before Save. See "Settings" in docs/features/files/remote-folders.md.
/// </summary>
public sealed partial class RemoteServerEditor : ObservableObject, IDisposable
{
    /// <summary>Sign in with: the place of Password and SSH key in the list.</summary>
    public const int PasswordChoice = 0;
    public const int KeyChoice = 1;

    // Every field affects the problems shown, Connect and Save.
    private static readonly string[] RefreshedNames =
    [
        nameof(UsesPassword), nameof(UsesKey), nameof(SecretPlaceholder), nameof(KeyText), nameof(HasKey), nameof(AsksToCheckKey),
        nameof(CanConnect), nameof(ConnectHint), nameof(HasConnectHint), nameof(IsTested),
        nameof(NameProblem), nameof(HasNameProblem), nameof(AddressProblem), nameof(HasAddressProblem), nameof(PortProblem), nameof(HasPortProblem),
        nameof(KeyFileProblem), nameof(HasKeyFileProblem), nameof(RemoteFolderProblem), nameof(HasRemoteFolderProblem), nameof(CanSave),
    ];

    private readonly RemoteServer? _server;
    private readonly IReadOnlyList<string> _otherNames;
    private readonly Func<RemoteFoldersRequest, CancellationToken, Task<RemoteFoldersAnswer?>> _ask;
    private readonly Func<string?> _savedSecret;
    private readonly Func<string, bool> _fileExists;
    private readonly CancellationTokenSource _closed = new();
    private string _name;
    private string _address;
    private string _port;
    private string _userName;
    private int _signIn;
    private string _password = string.Empty;
    private string _keyFile;
    private string _passphrase = string.Empty;
    private string _remoteFolder;
    private RemoteHostKey? _presentedKey;
    private bool _keyChecked;
    private bool _isConnecting;
    private string _connectProblem = string.Empty;
    private Details? _tested;
    private string? _missingFolder;

    /// <param name="server">The server to edit, or null to add one.</param>
    /// <param name="otherNames">The other servers' names, which this one can't have.</param>
    /// <param name="canConnect">Whether Remote Folders is on, so Connect can ask the provider.</param>
    /// <param name="ask">Asks the provider through its pipe.</param>
    /// <param name="savedSecret">Reads the server's saved password or passphrase, for Connect when the box is left empty.</param>
    /// <param name="signInAgain">Whether the dialog opens for Sign in again.</param>
    public RemoteServerEditor(
        RemoteServer? server,
        IReadOnlyList<string> otherNames,
        bool canConnect,
        Func<RemoteFoldersRequest, CancellationToken, Task<RemoteFoldersAnswer?>> ask,
        Func<string?> savedSecret,
        Func<string, bool> fileExists,
        bool signInAgain = false)
    {
        ArgumentNullException.ThrowIfNull(otherNames);
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentNullException.ThrowIfNull(savedSecret);
        ArgumentNullException.ThrowIfNull(fileExists);

        _server = server;
        _otherNames = otherNames;
        _ask = ask;
        _savedSecret = savedSecret;
        _fileExists = fileExists;
        CanConnectNow = canConnect;
        Title = server is null ? Strings.RemoteFoldersAddTitle : signInAgain ? Strings.RemoteFoldersSignInAgainTitle : Strings.RemoteFoldersEditTitle;
        _name = server?.Name ?? string.Empty;
        _address = server?.Host ?? string.Empty;
        _port = (server?.Port ?? RemoteServer.DefaultPort).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _userName = server?.UserName ?? string.Empty;
        _signIn = server?.KnownSignIn == RemoteSignIn.Key ? KeyChoice : PasswordChoice;
        _keyFile = server?.KeyFile ?? string.Empty;
        _remoteFolder = server?.RemoteFolder ?? string.Empty;
        _presentedKey = server?.HostKey;
        _keyChecked = server is not null;
    }

    public bool IsNew => _server is null;

    public bool IsExisting => _server is not null;

    public string Title { get; }

    /// <summary>Whether Remote Folders is on, so Connect can ask the provider.</summary>
    public bool CanConnectNow { get; }

    public bool CannotConnectNow => !CanConnectNow;

    public string Name
    {
        get => _name;
        set => Change(ref _name, value);
    }

    /// <summary>The name a new server gets when the box is left empty: its address.</summary>
    public string NamePlaceholder => _address.Trim().Length > 0 ? _address.Trim() : Strings.RemoteFoldersNamePlaceholder;

    public string Address
    {
        get => _address;
        set
        {
            if (_address != value)
            {
                // Another server, so another key.
                ForgetKey();
            }

            Change(ref _address, value);
            OnPropertyChanged(nameof(NamePlaceholder));
        }
    }

    public string Port
    {
        get => _port;
        set
        {
            if (_port != value)
            {
                ForgetKey();
            }

            Change(ref _port, value);
        }
    }

    public string UserName
    {
        get => _userName;
        set => Change(ref _userName, value);
    }

    /// <summary>Sign in with: <see cref="PasswordChoice"/> or <see cref="KeyChoice"/>.</summary>
    public int SignIn
    {
        get => _signIn;
        set
        {
            if (value is PasswordChoice or KeyChoice)
            {
                Change(ref _signIn, value);
            }
        }
    }

    public bool UsesPassword => _signIn == PasswordChoice;

    public bool UsesKey => _signIn == KeyChoice;

    public string Password
    {
        get => _password;
        set => Change(ref _password, value ?? string.Empty);
    }

    public string KeyFile
    {
        get => _keyFile;
        set => Change(ref _keyFile, value ?? string.Empty);
    }

    public string Passphrase
    {
        get => _passphrase;
        set => Change(ref _passphrase, value ?? string.Empty);
    }

    /// <summary>For a server being edited, whether an empty password or passphrase box keeps the saved one.</summary>
    public string SecretPlaceholder => IsExisting && SameSignIn ? Strings.RemoteFoldersKeepSecretPlaceholder : string.Empty;

    public string RemoteFolder
    {
        get => _remoteFolder;
        set => Change(ref _remoteFolder, value);
    }

    /// <summary>The server's key, as read for the user to check, or the confirmed one of a server being edited.</summary>
    public string KeyText => _presentedKey is { } key ? RemoteServerCheck.KeyText(key) : string.Empty;

    public bool HasKey => _presentedKey is not null;

    /// <summary>Whether the dialog asks the user to check the key: a new server whose key was read.</summary>
    public bool AsksToCheckKey => IsNew && HasKey;

    /// <summary>I checked this key. Checking it signs in at once.</summary>
    public bool KeyChecked
    {
        get => _keyChecked;
        set
        {
            if (Change(ref _keyChecked, value) && value && !_isConnecting)
            {
                _ = ConnectAsync();
            }
        }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        private set
        {
            if (SetProperty(ref _isConnecting, value))
            {
                OnPropertyChanged(nameof(CanConnect));
            }
        }
    }

    /// <summary>Whether Connect is available: Remote Folders is on, nothing runs, and the details can be sent.</summary>
    public bool CanConnect => CanConnectNow && !_isConnecting && ConnectionProblem() == ServerProblem.None && SignInProblem(forConnect: true) == ServerProblem.None;

    /// <summary>Why Connect failed, or an empty string.</summary>
    public string ConnectProblem
    {
        get => _connectProblem;
        private set
        {
            if (SetProperty(ref _connectProblem, value))
            {
                OnPropertyChanged(nameof(HasConnectProblem));
            }
        }
    }

    public bool HasConnectProblem => _connectProblem.Length > 0;

    /// <summary>What to do next, below Connect: check the key, or Connect to check what changed.</summary>
    public string ConnectHint =>
        _isConnecting || HasConnectProblem || !CanConnectNow ? string.Empty
        : ConnectionProblem() != ServerProblem.None ? Strings.RemoteFoldersEnterDetailsHint
        : AsksToCheckKey && !_keyChecked ? Strings.RemoteFoldersCheckKeyHint
        : !NeedsTest || IsTested ? string.Empty
        : SignInProblem(forConnect: true) != ServerProblem.None ? Strings.RemoteFoldersSignInHint
        : Strings.RemoteFoldersConnectHint;

    public bool HasConnectHint => ConnectHint.Length > 0;

    /// <summary>The details signed in and found the folder, as they are now.</summary>
    public bool IsTested => _tested is { } tested && tested == Current();

    // A new server's empty name shows no problem while there's no address to name it after.
    public string NameProblem => EffectiveName.Length == 0 && IsNew ? string.Empty : Text(RemoteServerCheck.CheckName(EffectiveName, _otherNames));

    public bool HasNameProblem => NameProblem.Length > 0;

    public string AddressProblem => IsNew && _address.Length > 0 ? Text(RemoteServerCheck.CheckAddress(_address)) : string.Empty;

    public bool HasAddressProblem => AddressProblem.Length > 0;

    public string PortProblem => IsNew ? Text(RemoteServerCheck.CheckPort(_port)) : string.Empty;

    public bool HasPortProblem => PortProblem.Length > 0;

    public string KeyFileProblem => UsesKey && _keyFile.Length > 0 ? Text(RemoteServerCheck.CheckKeyFile(_keyFile, _fileExists)) : string.Empty;

    public bool HasKeyFileProblem => KeyFileProblem.Length > 0;

    public string RemoteFolderProblem => IsNew && _missingFolder is not null && _remoteFolder.Trim() == _missingFolder ? Strings.RemoteFoldersFolderNotFound : string.Empty;

    public bool HasRemoteFolderProblem => RemoteFolderProblem.Length > 0;

    public bool CanSave =>
        RemoteServerCheck.CheckName(EffectiveName, _otherNames) == ServerProblem.None
        && SignInProblem(forConnect: false) == ServerProblem.None
        && (IsNew
            ? ConnectionProblem() == ServerProblem.None && _keyChecked && _presentedKey is not null && _remoteFolder.Trim().Length > 0 && IsTested
            : !NeedsTest || IsTested);

    /// <summary>What Save does with the password or passphrase.</summary>
    public SecretChange Secret =>
        IsNew || !SameSignIn || TypedSecret.Length > 0 ? new SecretChange(true, TypedSecret) : SecretChange.Keep;

    // The name given, or the address for a new server whose name is left empty.
    private string EffectiveName => _name.Trim().Length > 0 || IsExisting ? _name.Trim() : _address.Trim();

    private string TypedSecret => UsesKey ? _passphrase : _password;

    private bool SameSignIn =>
        _server is { } server && server.KnownSignIn == (UsesKey ? RemoteSignIn.Key : RemoteSignIn.Password) && (!UsesKey || _keyFile.Trim() == server.KeyFile);

    // A new server, or a new sign-in, must sign in before it's saved.
    private bool NeedsTest => IsNew || !SameSignIn || TypedSecret.Length > 0;

    /// <summary>The server as edited. A new one has no ID or folder yet.</summary>
    public RemoteServer ToServer() => (_server ?? new RemoteServer()) with
    {
        Name = EffectiveName,
        Protocol = _server?.Protocol ?? nameof(RemoteProtocol.Sftp),
        Host = _server?.Host ?? _address.Trim(),
        Port = _server?.Port ?? RemoteServerCheck.ParsePort(_port) ?? RemoteServer.DefaultPort,
        UserName = _server?.UserName ?? _userName.Trim(),
        SignIn = UsesKey ? nameof(RemoteSignIn.Key) : nameof(RemoteSignIn.Password),
        KeyFile = UsesKey ? _keyFile.Trim() : string.Empty,
        RemoteFolder = _server?.RemoteFolder ?? _remoteFolder.Trim(),
        HostKey = _server?.HostKey ?? _presentedKey,
    };

    /// <summary>
    /// Connect: for a new server whose key isn't checked yet, reads the key, sending nothing else;
    /// otherwise signs in, trusting only that key, and checks the folder on the server.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (!CanConnect)
        {
            return;
        }

        IsConnecting = true;
        ConnectProblem = string.Empty;
        Refresh();
        try
        {
            if (IsNew && !_keyChecked)
            {
                await ReadKeyAsync().ConfigureAwait(true);
            }
            else
            {
                await SignInAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        {
            // The dialog closed.
        }
        finally
        {
            IsConnecting = false;
            Refresh();
        }
    }

    /// <summary>The dialog closed: a test still running is left.</summary>
    public void Dispose()
    {
        if (!_closed.IsCancellationRequested)
        {
            _closed.Cancel();
        }

        _closed.Dispose();
    }

    private async Task ReadKeyAsync()
    {
        var test = await TestAsync(new RemoteFoldersRequest { Kind = RemoteFoldersRequestKind.TestServer, Server = ToServer() }).ConfigureAwait(true);
        if (test is { Outcome: RemoteTestOutcome.KeyToConfirm, PresentedKey: { } key })
        {
            _presentedKey = key;
            _keyChecked = false;
            OnPropertyChanged(nameof(KeyChecked));
        }
        else
        {
            ConnectProblem = Failure(test);
        }
    }

    private async Task SignInAsync()
    {
        var details = Current();
        var server = ToServer();
        string? secret = TypedSecret.Length > 0 ? TypedSecret : IsExisting && SameSignIn ? _savedSecret() : null;
        var test = await TestAsync(new RemoteFoldersRequest
        {
            Kind = RemoteFoldersRequestKind.TestServer,
            Server = server,
            Secret = secret,
            Key = server.HostKey,
        }).ConfigureAwait(true);

        switch (test)
        {
            case { Outcome: RemoteTestOutcome.Connected }:
                // An empty folder was the home folder, which the test checked.
                if (IsNew && _remoteFolder.Trim().Length == 0 && test.HomeFolder is { Length: > 0 } home)
                {
                    _remoteFolder = home;
                    OnPropertyChanged(nameof(RemoteFolder));
                    details = details with { RemoteFolder = home };
                }

                _tested = details;
                break;

            case { Outcome: RemoteTestOutcome.FolderNotFound }:
                _missingFolder = details.RemoteFolder;
                break;

            case { Outcome: RemoteTestOutcome.Failed, Failure: RemoteFailure.KeyChanged } when IsNew && test.PresentedKey is { } key:
                // The key changed since the user checked it: they check the new one.
                _presentedKey = key;
                _keyChecked = false;
                OnPropertyChanged(nameof(KeyChecked));
                ConnectProblem = Strings.RemoteFoldersConnectKeyChangedNew;
                break;

            default:
                ConnectProblem = Failure(test);
                break;
        }
    }

    private async Task<RemoteTestResult?> TestAsync(RemoteFoldersRequest request)
    {
        var answer = await _ask(request, _closed.Token).ConfigureAwait(true);
        return answer?.Test;
    }

    private string Failure(RemoteTestResult? test) => test switch
    {
        null => Strings.RemoteFoldersConnectNoAnswer,
        { Outcome: RemoteTestOutcome.FolderNotFound } => Strings.RemoteFoldersFolderNotFound,
        { Failure: RemoteFailure.KeyChanged } => IsNew ? Strings.RemoteFoldersConnectKeyChangedNew : Strings.RemoteFoldersConnectKeyChangedEdit,
        { Failure: var failure } => RemoteFoldersText.ConnectFailure(failure),
    };

    private void ForgetKey()
    {
        if (IsNew && _presentedKey is not null)
        {
            _presentedKey = null;
            _keyChecked = false;
            OnPropertyChanged(nameof(KeyChecked));
        }
    }

    private ServerProblem ConnectionProblem()
    {
        if (IsExisting)
        {
            return ServerProblem.None;
        }

        var problem = RemoteServerCheck.CheckAddress(_address);
        if (problem == ServerProblem.None)
        {
            problem = RemoteServerCheck.CheckPort(_port);
        }

        return problem == ServerProblem.None ? RemoteServerCheck.CheckUserName(_userName) : problem;
    }

    // A password is needed, except to keep the saved one; a key file is needed, its passphrase isn't.
    private ServerProblem SignInProblem(bool forConnect)
    {
        // Reading a new server's key sends nothing else.
        if (forConnect && IsNew && !_keyChecked)
        {
            return ServerProblem.None;
        }

        if (UsesKey)
        {
            return RemoteServerCheck.CheckKeyFile(_keyFile, _fileExists);
        }

        return _password.Length == 0 && !(IsExisting && SameSignIn) ? ServerProblem.NoPassword : ServerProblem.None;
    }

    private Details Current() => new(
        _address.Trim(),
        _port.Trim(),
        _userName.Trim(),
        _signIn,
        UsesKey ? _keyFile.Trim() : string.Empty,
        TypedSecret,
        _remoteFolder.Trim(),
        _presentedKey);

    private static string Text(ServerProblem problem) => RemoteFoldersText.Problem(problem);

    private bool Change<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        if (propertyName != nameof(KeyChecked))
        {
            ConnectProblem = string.Empty;
        }

        Refresh();
        return true;
    }

    private void Refresh()
    {
        foreach (string name in RefreshedNames)
        {
            OnPropertyChanged(name);
        }
    }

    // What a test checked: the same details, as they are now, need no other test.
    private sealed record Details(string Address, string Port, string UserName, int SignIn, string KeyFile, string Secret, string RemoteFolder, RemoteHostKey? Key);
}
