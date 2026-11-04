using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using PCCompanion.Infrastructure.Features;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class HostNotificationsTests
{
    private readonly RecordingLogger<HostNotifications> _logger = new();
    private readonly List<(string Title, string Text)> _shown = [];

    [Fact]
    public void ANotificationShowsFromTheIcon()
    {
        var notifications = Attached();

        Assert.True(notifications.Show("Backup drive", "You can remove the drive.", null));

        Assert.Equal([("Backup drive", "You can remove the drive.")], _shown);
    }

    [Fact]
    public void ASelectionGoesToTheNotificationShownLast()
    {
        var notifications = Attached();
        var selected = new List<string>();
        notifications.Show("Update", "PC Companion 9.9.9 is available.", () => selected.Add("update"));
        notifications.Show("Backup drive", "Select to back up.", () => selected.Add("drive"));

        notifications.Selected();

        Assert.Equal(["drive"], selected);
    }

    [Fact]
    public void ANotificationIsHandledOnce()
    {
        var notifications = Attached();
        var selections = 0;
        notifications.Show("Backup drive", "Select to back up.", () => selections++);

        notifications.Selected();
        notifications.Selected();

        Assert.Equal(1, selections);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Debug && entry.Message.Contains("nothing to do", StringComparison.Ordinal));
    }

    [Fact]
    public void ANotificationWithNothingToDoTakesTheSelection()
    {
        var notifications = Attached();
        var selections = 0;
        notifications.Show("Update", "PC Companion 9.9.9 is available.", () => selections++);
        notifications.Show("Backup drive", "Backed up 45 files.", null);

        notifications.Selected();

        Assert.Equal(0, selections);
    }

    [Fact]
    public void ANotificationWindowsRefusedLeavesTheSelectionToTheOneBefore()
    {
        var refuse = false;
        var notifications = new HostNotifications(_logger);
        notifications.Attach((title, text) => !refuse);
        var selected = new List<string>();
        notifications.Show("Update", "PC Companion 9.9.9 is available.", () => selected.Add("update"));
        refuse = true;

        Assert.False(notifications.Show("Backup drive", "Select to back up.", () => selected.Add("drive")));
        notifications.Selected();

        Assert.Equal(["update"], selected);
    }

    [Fact]
    public void ANotificationBeforeTheIconIsReadyIsDropped()
    {
        var notifications = new HostNotifications(_logger);

        Assert.False(notifications.Show("Backup drive", "Select to back up.", () => { }));

        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public void ASelectionThatFailsIsLogged()
    {
        var notifications = Attached();
        notifications.Show("Backup drive", "Select to back up.", () => throw new InvalidOperationException("Test failure."));

        notifications.Selected();

        Assert.Equal(LogLevel.Error, _logger.Entries[^1].Level);
    }

    private HostNotifications Attached()
    {
        var notifications = new HostNotifications(_logger);
        notifications.Attach((title, text) =>
        {
            _shown.Add((title, text));
            return true;
        });
        return notifications;
    }
}
