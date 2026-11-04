using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PCCompanion.Modules.Files.DriveActions;
using PCCompanion.Platform.Windows;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Modules.Files.Tests;

// Each test runs the feature's thread with its own hidden window, drives in memory and a pipe of
// its own name, so no drive or file is touched.
public sealed class DriveActionsThreadTests : IDisposable
{
    private const string Id = "5f0c9a0e3b1d4c7e9a2f6b8d1c3e5a7f";
    private const uint Serial = 0xA8D4F2F8;
    private const string HardwareSerial = "0LVF0HQ5";

    private static readonly SpecialFolders Places = new(@"C:\Windows", [@"C:\Program Files"], @"C:\Users\Ana\AppData\Local\PCCompanion", []);

    private readonly TemporaryFolder _data = new();
    private readonly FakeDriveActionsSystem _system = new();
    private readonly string _pipeName = "PCCompanion.Tests." + Guid.NewGuid().ToString("N");
    private DriveActionsThread? _thread;

    public DriveActionsThreadTests()
    {
        _system.AddDrive('E', label: "BACKUP", serial: Serial, hardwareSerial: HardwareSerial);
        _system.AddFile($@"E:\{DriveIdentity.IdFileName}", size: 32, attributes: FileAttributes.Hidden | FileAttributes.System, text: Id);
    }

    private static DriveActionsSettings Settings => new()
    {
        Enabled = true,
        Drives =
        [
            new DriveEntry
            {
                Name = "Backup drive",
                Id = Id,
                VolumeSerial = DriveIdentity.HashVolumeSerial(Serial),
                HardwareSerial = DriveIdentity.HashHardwareSerial(HardwareSerial),
                WhenConnected = "Nothing",
                Actions = [new DriveAction { Action = "OpenFolder", Folder = "" }],
            },
        ],
    };

    [Fact]
    public async Task TheSettingsAppsRequestsAreAnsweredOnTheThread()
    {
        Start();

        var answer = await AskAsync(new DriveActionsRequest { Kind = DriveActionsRequestKind.Drives });

        var drive = Assert.Single(answer!.Drives);
        Assert.Equal(("E", RecognitionOutcome.Recognized, 0), (drive.Letter, drive.Outcome, drive.Drive));
    }

    [Fact]
    public async Task ARequestThatIsntOneIsRefused()
    {
        Start();

        var answer = await RequestPipe.AskAsync(_pipeName, "{\"kind\":\"Format\"}"u8.ToArray(), TimeSpan.FromSeconds(5), DriveActionsPipe.MaximumAnswerLength, TestContext.Current.CancellationToken);

        Assert.NotNull(answer);
        using var json = JsonDocument.Parse(answer);
        Assert.Equal("Refused", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task OnceTheThreadEndsThePipeIsGone()
    {
        Start();
        Assert.NotNull(await AskAsync(new DriveActionsRequest { Kind = DriveActionsRequestKind.History }));

        _thread!.Stop();
        Assert.True(_thread.Join(TimeSpan.FromSeconds(10)));
        _thread = null;

        Assert.Null(await DriveActionsPipe.AskAsync(_pipeName, new DriveActionsRequest(), TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (_thread is not null)
        {
            _thread.Stop();
            Assert.True(_thread.Join(TimeSpan.FromSeconds(10)));
        }

        _data.Dispose();
    }

    private void Start() =>
        _thread = new DriveActionsThread(
            Settings,
            _system,
            thread => new ThreadHost(thread),
            new DriveRecordsStore(Path.Join(_data.FullPath, DriveRecordsStore.FolderName)),
            new DriveActionsHistoryStore(Path.Join(_data.FullPath, DriveActionsHistoryStore.FileName)),
            Places,
            TimeProvider.System,
            _pipeName,
            NullLogger.Instance);

    private Task<DriveActionsAnswer?> AskAsync(DriveActionsRequest request) =>
        DriveActionsPipe.AskAsync(_pipeName, request, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

    // Runs the runner's work on the thread, and leaves out what the host's UI thread would do.
    private sealed class ThreadHost(DriveActionsThread thread) : IDriveActionsHost
    {
        public void Notify(string title, string text, Action? selected)
        {
        }

        public void Watch(char letter)
        {
        }

        public void Unwatch(char letter)
        {
        }

        public void OpenFolder(string path)
        {
        }

        public void OpenPage()
        {
        }

        public void Publish(DriveActionsState state)
        {
        }

        public void Later(Action work) => thread.Send(_ => work());
    }
}
