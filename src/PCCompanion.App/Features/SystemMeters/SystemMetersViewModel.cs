using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PCCompanion.App.ViewModels;
using PCCompanion.Localization;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.SystemMeters;

public sealed record ShowOnOption(ShowOn ShowOn, string DisplayName) : Choice(DisplayName);

public sealed record LayoutOption(StripLayout Layout, string DisplayName) : Choice(DisplayName);

public sealed record IconsOption(StripIcons Icons, string DisplayName) : Choice(DisplayName);

public sealed record IntervalOption(int Seconds, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The System Meters page. Every change is saved right away.
/// </summary>
public sealed partial class SystemMetersViewModel : FeatureViewModel<SystemMetersSettings>
{
    // The choices for Update every, in seconds.
    private static readonly int[] Intervals = [1, 2, 5];

    private readonly ILogger<SystemMetersViewModel> _logger;
    private ShowOnOption? _selectedShowOn;
    private LayoutOption? _selectedLayout;
    private IconsOption? _selectedIcons;
    private IntervalOption? _selectedInterval;

    public SystemMetersViewModel(FeatureSettingsEditor editor, ILogger<SystemMetersViewModel> logger)
        : base(editor, SystemMetersFeature.Definition)
    {
        _logger = logger;

        ShowOnOptions =
        [
            new(ShowOn.Taskbar, Strings.SystemMetersShowOnTaskbar),
            new(ShowOn.NotificationArea, Strings.SystemMetersShowOnNotificationArea),
        ];
        LayoutOptions =
        [
            new(StripLayout.ValuesAndNames, Strings.SystemMetersLayoutValuesAndNames),
            new(StripLayout.LargeValues, Strings.SystemMetersLayoutLargeValues),
            new(StripLayout.Compact, Strings.SystemMetersLayoutCompact),
        ];
        IconsOptions =
        [
            new(StripIcons.Color, Strings.SystemMetersIconsColor),
            new(StripIcons.OneColor, Strings.SystemMetersIconsOneColor),
            new(StripIcons.None, Strings.SystemMetersIconsNone),
        ];
        IntervalOptions = [.. Intervals.Select(seconds => new IntervalOption(seconds, PluralStrings.Format("SystemMetersIntervalSeconds", seconds)))];

        _selectedShowOn = ShowOnOptions.First(option => option.ShowOn == Settings.ShowOn);
        _selectedLayout = LayoutOptions.First(option => option.Layout == Settings.Layout);
        _selectedIcons = IconsOptions.First(option => option.Icons == Settings.Icons);

        // An interval the list doesn't offer, such as 3 written by hand, works and shows none chosen.
        _selectedInterval = IntervalOptions.FirstOrDefault(option => option.Seconds == Settings.UpdateIntervalSeconds);
    }

    public IReadOnlyList<ShowOnOption> ShowOnOptions { get; }

    public IReadOnlyList<LayoutOption> LayoutOptions { get; }

    public IReadOnlyList<IconsOption> IconsOptions { get; }

    public IReadOnlyList<IntervalOption> IntervalOptions { get; }

    public bool Cpu
    {
        get => Settings.Cpu;
        set => Change(Settings.Cpu, value, () => Settings with { Cpu = value });
    }

    public bool Memory
    {
        get => Settings.Memory;
        set => Change(Settings.Memory, value, () => Settings with { Memory = value });
    }

    public bool Network
    {
        get => Settings.Network;
        set => Change(Settings.Network, value, () => Settings with { Network = value });
    }

    public ShowOnOption? SelectedShowOn
    {
        get => _selectedShowOn;
        set
        {
            if (value is not null && SetProperty(ref _selectedShowOn, value))
            {
                Save(Settings with { ShowOn = value.ShowOn });
                OnPropertyChanged(nameof(OnTaskbar));
                OnPropertyChanged(nameof(InNotificationArea));
            }
        }
    }

    public LayoutOption? SelectedLayout
    {
        get => _selectedLayout;
        set
        {
            if (value is not null && SetProperty(ref _selectedLayout, value))
            {
                Save(Settings with { Layout = value.Layout });
            }
        }
    }

    public IconsOption? SelectedIcons
    {
        get => _selectedIcons;
        set
        {
            if (value is not null && SetProperty(ref _selectedIcons, value))
            {
                Save(Settings with { Icons = value.Icons });
            }
        }
    }

    public IntervalOption? SelectedInterval
    {
        get => _selectedInterval;
        set
        {
            if (value is not null && SetProperty(ref _selectedInterval, value))
            {
                Save(Settings with { UpdateIntervalSeconds = value.Seconds });
            }
        }
    }

    /// <summary>Whether the meters show on the taskbar, where Layout and Icons apply.</summary>
    public bool OnTaskbar => Settings.ShowOn == ShowOn.Taskbar;

    /// <summary>Whether each meter has its own notification icon, which Windows may hide.</summary>
    public bool InNotificationArea => Settings.ShowOn == ShowOn.NotificationArea;

    [RelayCommand]
    private void OpenTaskbarSettings()
    {
        if (!ShellActions.OpenTaskbarSettings())
        {
            LogCantOpenTaskbarSettings(_logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't open the taskbar settings.")]
    private static partial void LogCantOpenTaskbarSettings(ILogger logger);
}
