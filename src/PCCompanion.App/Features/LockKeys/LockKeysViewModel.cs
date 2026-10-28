using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PCCompanion.App.ViewModels;
using PCCompanion.Localization;
using PCCompanion.Modules.Input.LockKeys;
using PCCompanion.Platform.Windows;

namespace PCCompanion.App.Features.LockKeys;

public sealed record PositionOption(NoticePosition Position, string DisplayName) : Choice(DisplayName);

/// <summary>
/// The Lock Keys page. Every change is saved right away.
/// </summary>
public sealed partial class LockKeysViewModel : FeatureViewModel<LockKeysSettings>
{
    private static readonly CompositeFormat DurationFormat = CompositeFormat.Parse(Strings.LockKeysDurationValue);

    private readonly ILogger<LockKeysViewModel> _logger;
    private PositionOption? _selectedPosition;

    public LockKeysViewModel(FeatureSettingsEditor editor, ILogger<LockKeysViewModel> logger)
        : base(editor, LockKeysFeature.Definition)
    {
        _logger = logger;

        PositionOptions =
        [
            new(NoticePosition.BottomCenter, Strings.LockKeysPositionBottomCenter),
            new(NoticePosition.TopCenter, Strings.LockKeysPositionTopCenter),
            new(NoticePosition.TopLeft, Strings.LockKeysPositionTopLeft),
        ];
        _selectedPosition = PositionOptions.First(option => option.Position == Settings.Position);
    }

    public static double MinimumDuration => LockKeysSettings.MinimumDurationMilliseconds;

    public static double MaximumDuration => LockKeysSettings.MaximumDurationMilliseconds;

    public IReadOnlyList<PositionOption> PositionOptions { get; }

    public bool CapsLock
    {
        get => Settings.CapsLock;
        set => ChangeKey(Settings.CapsLock, value, () => Settings with { CapsLock = value });
    }

    public bool NumLock
    {
        get => Settings.NumLock;
        set => ChangeKey(Settings.NumLock, value, () => Settings with { NumLock = value });
    }

    public bool ScrollLock
    {
        get => Settings.ScrollLock;
        set => ChangeKey(Settings.ScrollLock, value, () => Settings with { ScrollLock = value });
    }

    public bool ShowWhenOff
    {
        get => Settings.ShowWhenOff;
        set => Change(Settings.ShowWhenOff, value, () => Settings with { ShowWhenOff = value });
    }

    /// <summary>Whether no key shows notices, which the page says.</summary>
    public bool NoKeyChosen => !Settings.CapsLock && !Settings.NumLock && !Settings.ScrollLock;

    public PositionOption? SelectedPosition
    {
        get => _selectedPosition;
        set
        {
            if (value is not null && SetProperty(ref _selectedPosition, value))
            {
                Save(Settings with { Position = value.Position });
            }
        }
    }

    public double Duration
    {
        get => Settings.DurationMilliseconds;
        set
        {
            var milliseconds = (int)Math.Round(value);
            if (Change(Settings.DurationMilliseconds, milliseconds, () => Settings with { DurationMilliseconds = milliseconds }))
            {
                OnPropertyChanged(nameof(DurationText));
            }
        }
    }

    /// <summary>The duration in seconds, such as "2 s" or "1,5 s", in the user's number format.</summary>
    public string DurationText => string.Format(CultureInfo.CurrentCulture, DurationFormat, Settings.DurationMilliseconds / 1000.0);

    public bool IgnoreFullScreenApps
    {
        get => Settings.IgnoreFullScreenApps;
        set => Change(Settings.IgnoreFullScreenApps, value, () => Settings with { IgnoreFullScreenApps = value });
    }

    public bool KeepCapsLockBadge
    {
        get => Settings.KeepCapsLockBadge;
        set => Change(Settings.KeepCapsLockBadge, value, () => Settings with { KeepCapsLockBadge = value });
    }

    private void ChangeKey(bool current, bool value, Func<LockKeysSettings> changed, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (Change(current, value, changed, propertyName))
        {
            OnPropertyChanged(nameof(NoKeyChosen));
        }
    }

    [RelayCommand]
    private void OpenKeyboardSettings()
    {
        if (!ShellActions.OpenKeyboardAccessibilitySettings())
        {
            LogCantOpenKeyboardSettings(_logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows didn't open the keyboard accessibility settings.")]
    private static partial void LogCantOpenKeyboardSettings(ILogger logger);
}
