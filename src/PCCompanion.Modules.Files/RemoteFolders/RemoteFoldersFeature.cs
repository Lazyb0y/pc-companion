using System;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PCCompanion.Core.Features;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.RemoteFolders;

/// <summary>
/// Shows the files on the user's SFTP servers in File Explorer, through the Cloud Files API. The
/// files' work runs in a process of its own, <c>PCCompanion.RemoteFolders.exe</c>, which the host
/// starts while the feature is on, never with admin rights, and starts again when it ends
/// unexpectedly. See docs/features/files/remote-folders.md.
/// </summary>
public sealed partial class RemoteFoldersFeature : IFeature
{
    // The hidden window through which notifications go to the host's UI thread.
    private const string WindowClassName = "PCCompanion.Host.RemoteFolders";

    private readonly IHostNotifications _notifications;
    private readonly IHostCommands _commands;
    private readonly ILogger _logger;
    private readonly ProviderSupervisor _provider;
    private RemoteFoldersSettings? _applied;
    private MessageWindow? _window;

    public RemoteFoldersFeature(IHostNotifications notifications, IHostCommands commands, TimeProvider time, ILogger<RemoteFoldersFeature> logger)
        : this(StartProvider, AskProviderToStop, notifications, commands, time, logger)
    {
    }

    /// <param name="startProvider">Starts the provider; tests give a fake.</param>
    /// <param name="askProviderToStop">Asks the running provider to stop.</param>
    public RemoteFoldersFeature(Func<RunningProcess> startProvider, Action askProviderToStop, IHostNotifications notifications, IHostCommands commands, TimeProvider time, ILogger<RemoteFoldersFeature> logger)
    {
        _notifications = notifications;
        _commands = commands;
        _logger = logger;
        _provider = new ProviderSupervisor(startProvider, askProviderToStop, time, logger);
        _provider.GaveUp += (_, _) => _window?.BeginInvoke(NotifyStopped);
    }

    public static FeatureInfo Definition { get; } = new("remoteFolders", FilesModule.Id, "RemoteFoldersName", "RemoteFoldersDescription", "");

    /// <summary>Whether this PC can run Remote Folders, for the settings app, which doesn't create the feature.</summary>
    public static FeatureSupport CurrentSupport => FeatureSupport.Supported;

    public FeatureInfo Info => Definition;

    public FeatureSupport Support => CurrentSupport;

    /// <summary>Whether the feature is on with settings it could read.</summary>
    public bool IsRunning => _applied is not null;

    /// <summary>The provider's process while it runs.</summary>
    public int? ProviderProcessId => _provider.ProcessId;

    public void Apply(JsonElement? settings)
    {
        RemoteFoldersSettings read;
        try
        {
            read = RemoteFoldersSettings.Read(settings);
        }
        catch (JsonException exception)
        {
            LogUnreadableSettings(_logger, exception);
            TurnOff();
            return;
        }

        if (!read.Enabled)
        {
            TurnOff();
            return;
        }

        bool changed = read != _applied;
        if (changed)
        {
            LogApplied(_logger, read.Servers.Count, read.UsableServers.Count);
        }

        _applied = read;
        _window ??= new MessageWindow(WindowClassName);

        // The provider reads the settings itself; a change lets it start again after giving up.
        _provider.Run(settingsChanged: changed);
    }

    public void Dispose()
    {
        _applied = null;
        _provider.Dispose();
        _window?.Dispose();
        _window = null;
    }

    // Never with admin rights: a host elevated through UAC starts it with the shell's token.
    private static RunningProcess StartProvider()
    {
        string host = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        return Elevation.IsUacElevated
            ? UnelevatedProcess.StartKept(RemoteFoldersProcess.ExecutablePath, RemoteFoldersProcess.HostArgument + " " + host)
            : RunningProcess.Start(RemoteFoldersProcess.ExecutablePath, [RemoteFoldersProcess.HostArgument, host]);
    }

    private static void AskProviderToStop() => SessionSignal.Send(RemoteFoldersProcess.StopSignalName);

    private void TurnOff()
    {
        if (_applied is not null)
        {
            _provider.Stop();
        }

        _applied = null;
    }

    private void NotifyStopped() =>
        _notifications.Show(Strings.RemoteFoldersStoppedTitle, Strings.RemoteFoldersStoppedMessage, () => _commands.OpenFeatureSettings(Definition.Id));

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Remote Folders settings can't be read, so Remote Folders is off.")]
    private static partial void LogUnreadableSettings(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Remote Folders settings applied: {Servers} servers, {Usable} of them usable.")]
    private static partial void LogApplied(ILogger logger, int servers, int usable);
}
