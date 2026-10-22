using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Localization;
using PCCompanion.Modules.Network.NetworkProfiles;

namespace PCCompanion.App.Features.NetworkProfiles;

/// <summary>
/// The profile editor's fields and what's wrong with them, laid out like Windows' Proxy page. A
/// problem shows once its box has something in it and its part is on; Save is available when the
/// profile has no problem.
/// </summary>
public sealed partial class NetworkProfileEditor : ObservableObject
{
    private readonly IReadOnlyList<string> _otherNames;
    private string _name;
    private bool _autoDetect;
    private bool _useScript;
    private string _scriptAddress;
    private bool _useServer;
    private string _server;
    private string _exceptions;
    private bool _bypassLocal;

    /// <param name="profile">The profile to edit, or a new one to fill in.</param>
    /// <param name="isNew">Whether Save adds the profile, rather than changing it.</param>
    /// <param name="otherNames">The other profiles' names, which this one can't have.</param>
    public NetworkProfileEditor(NetworkProfile profile, bool isNew, IReadOnlyList<string> otherNames)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(otherNames);

        IsNew = isNew;
        _otherNames = otherNames;
        _name = profile.Name;
        _autoDetect = profile.Proxy.AutoDetect;
        _useScript = profile.Proxy.UseScript;
        _scriptAddress = profile.Proxy.ScriptAddress;
        _useServer = profile.Proxy.UseServer;
        _server = profile.Proxy.Server;
        _exceptions = string.Join(';', profile.Proxy.Exceptions);
        _bypassLocal = profile.Proxy.BypassLocal;
    }

    public bool IsNew { get; }

    public string Title => IsNew ? Strings.NetworkProfilesAddTitle : Strings.NetworkProfilesEditTitle;

    public string Name
    {
        get => _name;
        set => Change(ref _name, value);
    }

    public bool AutoDetect
    {
        get => _autoDetect;
        set => Change(ref _autoDetect, value);
    }

    public bool UseScript
    {
        get => _useScript;
        set => Change(ref _useScript, value);
    }

    public string ScriptAddress
    {
        get => _scriptAddress;
        set => Change(ref _scriptAddress, value);
    }

    public bool UseServer
    {
        get => _useServer;
        set => Change(ref _useServer, value);
    }

    public string Server
    {
        get => _server;
        set => Change(ref _server, value);
    }

    /// <summary>The exceptions, separated by semicolons, as in Windows Settings.</summary>
    public string Exceptions
    {
        get => _exceptions;
        set => Change(ref _exceptions, value);
    }

    public bool BypassLocal
    {
        get => _bypassLocal;
        set => Change(ref _bypassLocal, value);
    }

    public string NameProblem => _name.Length == 0 ? string.Empty : NetworkProfilesText.Problem(NetworkProfileCheck.CheckName(_name, _otherNames));

    public bool HasNameProblem => NameProblem.Length > 0;

    public string ScriptProblem => !_useScript || string.IsNullOrWhiteSpace(_scriptAddress)
        ? string.Empty
        : NetworkProfilesText.Problem(NetworkProfileCheck.CheckScriptAddress(_scriptAddress));

    public bool HasScriptProblem => ScriptProblem.Length > 0;

    public string ServerProblem => !_useServer || string.IsNullOrWhiteSpace(_server)
        ? string.Empty
        : NetworkProfilesText.Problem(NetworkProfileCheck.CheckServer(_server));

    public bool HasServerProblem => ServerProblem.Length > 0;

    public string ExceptionsProblem => !_useServer
        ? string.Empty
        : NetworkProfilesText.Problem(NetworkProfileCheck.CheckExceptions(SplitExceptions()));

    public bool HasExceptionsProblem => ExceptionsProblem.Length > 0;

    public bool CanSave => NetworkProfileCheck.Check(ToProfile(), _otherNames) == ProfileProblem.None;

    /// <summary>
    /// The profile as edited. It keeps the values of the parts that are off, so turning them on
    /// again brings the values back.
    /// </summary>
    public NetworkProfile ToProfile() => new()
    {
        Name = _name.Trim(),
        Proxy = new ProxyProfile
        {
            AutoDetect = _autoDetect,
            UseScript = _useScript,
            ScriptAddress = _scriptAddress.Trim(),
            UseServer = _useServer,
            Server = _server.Trim(),
            Exceptions = SplitExceptions(),
            BypassLocal = _bypassLocal,
        },
    };

    // An empty exception between two semicolons is a problem; one at the end isn't, as in Windows Settings.
    private List<string> SplitExceptions()
    {
        var exceptions = _exceptions.Trim().TrimEnd(';');
        return exceptions.Length == 0 ? [] : [.. exceptions.Split(';').Select(exception => exception.Trim())];
    }

    // Every field affects what's shown below the boxes and whether Save is available.
    private void Change<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return;
        }

        OnPropertyChanged(nameof(NameProblem));
        OnPropertyChanged(nameof(HasNameProblem));
        OnPropertyChanged(nameof(ScriptProblem));
        OnPropertyChanged(nameof(HasScriptProblem));
        OnPropertyChanged(nameof(ServerProblem));
        OnPropertyChanged(nameof(HasServerProblem));
        OnPropertyChanged(nameof(ExceptionsProblem));
        OnPropertyChanged(nameof(HasExceptionsProblem));
        OnPropertyChanged(nameof(CanSave));
    }
}
