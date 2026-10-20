using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PCCompanion.Localization;
using PCCompanion.Modules.Desktop.SystemMeters;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.SystemMeters;

public sealed record ShowOnOption(ShowOn ShowOn, string DisplayName);

public sealed record LayoutOption(StripLayout Layout, string DisplayName);

public sealed record IconsOption(StripIcons Icons, string DisplayName);

public sealed record IntervalOption(int Seconds, string DisplayName);

/// <summary>
/// The System Meters page. Every change is saved right away.
/// </summary>
public sealed partial class SystemMetersViewModel : ObservableObject
{
    // The choices for Update every, in seconds.
    private static readonly int[] Intervals = [1, 2, 5];

    private readonly FeatureSettingsEditor _editor;
    private readonly ILogger<SystemMetersViewModel> _logger;
    private SystemMetersSettings _settings;
    private ShowOnOption? _selectedShowOn;
    private LayoutOption? _selectedLayout;
    private IconsOption? _selectedIcons;
    private IntervalOption? _selectedInterval;
    private bool _saveFailed;

    public SystemMetersViewModel(FeatureSettingsEditor editor, ILogger<SystemMetersViewModel> logger)
    {
        _editor = editor;
        _logger = logger;
        _settings = editor.Read(SystemMetersFeature.Definition.Id, SystemMetersSettings.Read);

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

        _selectedShowOn = ShowOnOptions.First(option => option.ShowOn == _settings.ShowOn);
        _selectedLayout = LayoutOptions.First(option => option.Layout == _settings.Layout);
        _selectedIcons = IconsOptions.First(option => option.Icons == _settings.Icons);

        // An interval the list doesn't offer, such as 3 written by hand, works and shows none chosen.
        _selectedInterval = IntervalOptions.FirstOrDefault(option => option.Seconds == _settings.UpdateIntervalSeconds);
    }

    public static string Glyph => SystemMetersFeature.Definition.Glyph;

    public IReadOnlyList<ShowOnOption> ShowOnOptions { get; }

    public IReadOnlyList<LayoutOption> LayoutOptions { get; }

    public IReadOnlyList<IconsOption> IconsOptions { get; }

    public IReadOnlyList<IntervalOption> IntervalOptions { get; }

    public bool IsOn
    {
        get => _settings.Enabled;
        set
        {
            if (value != _settings.Enabled)
            {
                Save(_settings with { Enabled = value });
                OnPropertyChanged();
            }
        }
    }

    public bool Cpu
    {
        get => _settings.Cpu;
        set
        {
            if (value != _settings.Cpu)
            {
                Save(_settings with { Cpu = value });
                OnPropertyChanged();
            }
        }
    }

    public bool Memory
    {
        get => _settings.Memory;
        set
        {
            if (value != _settings.Memory)
            {
                Save(_settings with { Memory = value });
                OnPropertyChanged();
            }
        }
    }

    public bool Network
    {
        get => _settings.Network;
        set
        {
            if (value != _settings.Network)
            {
                Save(_settings with { Network = value });
                OnPropertyChanged();
            }
        }
    }

    public ShowOnOption? SelectedShowOn
    {
        get => _selectedShowOn;
        set
        {
            if (value is not null && SetProperty(ref _selectedShowOn, value))
            {
                Save(_settings with { ShowOn = value.ShowOn });
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
                Save(_settings with { Layout = value.Layout });
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
                Save(_settings with { Icons = value.Icons });
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
                Save(_settings with { UpdateIntervalSeconds = value.Seconds });
            }
        }
    }

    /// <summary>Whether the meters show on the taskbar, where Layout and Icons apply.</summary>
    public bool OnTaskbar => _settings.ShowOn == ShowOn.Taskbar;

    /// <summary>Whether each meter has its own notification icon, which Windows may hide.</summary>
    public bool InNotificationArea => _settings.ShowOn == ShowOn.NotificationArea;

    /// <summary>True when the last change couldn't be saved.</summary>
    public bool SaveFailed
    {
        get => _saveFailed;
        private set => SetProperty(ref _saveFailed, value);
    }

    [RelayCommand]
    private void OpenTaskbarSettings()
    {
        if (!ShellActions.OpenTaskbarSettings())
        {
            LogCantOpenTaskbarSettings(_logger);
        }
    }

    private void Save(SystemMetersSettings settings)
    {
        _settings = settings;
        SaveFailed = !_editor.Save(SystemMetersFeature.Definition.Id, settings.Write());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't open the taskbar settings.")]
    private static partial void LogCantOpenTaskbarSettings(ILogger logger);
}
