using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Localization;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.DisplayProfiles;

/// <summary>Display Profiles' access to Windows, for the feature and its page in the settings app.</summary>
/// <param name="windowClassName">
/// The class name of the hidden window that <see cref="CreateWindow"/> creates: the host's is
/// <see cref="HostWindowClassName"/>, which the settings app finds the host by.
/// </param>
public sealed class WindowsDisplayProfilesSystem(string windowClassName = WindowsDisplayProfilesSystem.HostWindowClassName) : IDisplayProfilesSystem
{
    /// <summary>The class name of the host's hidden window for Display Profiles.</summary>
    public const string HostWindowClassName = "PCCompanion.DisplayProfiles";

    // One task at a time, in the order they're queued, on the thread pool.
    private readonly TaskScheduler _background = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;

    // The serial number hash of each device path read so far: a display's EDID is read once.
    private readonly Lock _serialsGate = new();
    private readonly Dictionary<string, string> _serials = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRemoteSession => DisplayConfiguration.IsRemoteSession;

    public CurrentSetup Read()
    {
        var setup = DisplayConfiguration.Read();
        var serials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in setup.Displays)
        {
            serials[display.DevicePath] = SerialOf(display.DevicePath);
        }

        return new CurrentSetup(setup, serials);
    }

    public void Check(IReadOnlyList<DisplayPath> setup) => DisplayConfiguration.Check(setup);

    public void Apply(IReadOnlyList<DisplayPath> setup) => DisplayConfiguration.Apply(setup);

    public void SaveCurrent() => DisplayConfiguration.SaveCurrent();

    public void ApplySaved() => DisplayConfiguration.ApplySaved();

    public void SetHdr(DisplayTarget target, bool enabled) => DisplayConfiguration.SetHdr(target, enabled);

    public IReadOnlyList<DisplayMode> ListModes(DisplaySource source) => DisplayConfiguration.ListModes(source);

    public IDisplayWindow CreateWindow(Action changed, Action cantAnswer) => new DisplayWindow(windowClassName, changed, cantAnswer);

    public void RunInBackground(Action action) =>
        _ = Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.DenyChildAttach, _background);

    public bool RunInBackgroundAndWait(Action action, TimeSpan timeout) =>
        Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.DenyChildAttach, _background).Wait(timeout);

    // The texts come from the calling thread's language and regional format.
    public IKeepQuestion Ask(TimeSpan time, Action<bool> answered, Action<Exception> failed)
    {
        var language = CultureInfo.CurrentUICulture;
        var formatting = CultureInfo.CurrentCulture;
        var texts = new KeepChangesTexts(
            Strings.AppDisplayName,
            Strings.DisplayProfilesQuestion,
            seconds => PluralStrings.Format("DisplayProfilesQuestionSecondsLeft", seconds, language, formatting),
            Strings.DisplayProfilesQuestionKeep,
            Strings.DisplayProfilesQuestionRevert);
        var rightToLeft = Languages.Resolve(language).TextInfo.IsRightToLeft;
        return new Question(KeepChangesQuestion.Show(texts, time, rightToLeft, answered, failed));
    }

    // A display whose EDID Windows can't give now, such as one that was just disconnected, has no
    // serial number this time, and its EDID is read again the next time.
    private string SerialOf(string devicePath)
    {
        lock (_serialsGate)
        {
            if (_serials.TryGetValue(devicePath, out var known))
            {
                return known;
            }
        }

        string serial;
        try
        {
            serial = CurrentSetup.HashSerial(DisplayEdid.ReadSerialNumberAsync(devicePath).GetAwaiter().GetResult());
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            return string.Empty;
        }

        lock (_serialsGate)
        {
            _serials[devicePath] = serial;
        }

        return serial;
    }

    private sealed class Question(KeepChangesQuestion question) : IKeepQuestion
    {
        public void Close() => question.Close();
    }

    // Tells about display changes, and about the session locking or switching user and the PC
    // going to sleep, when nobody can answer a question.
    private sealed class DisplayWindow : IDisplayWindow
    {
        private readonly MessageWindow _window;
        private readonly DeviceNotifications? _devices;
        private readonly IDisposable? _displays;
        private readonly SessionMonitor? _session;

        public DisplayWindow(string className, Action changed, Action cantAnswer)
        {
            _window = new MessageWindow(className);
            try
            {
                _window.MessageReceived += (_, e) =>
                {
                    if (Displays.IsChangeMessage(e.Message))
                    {
                        changed();
                    }
                };
                _devices = new DeviceNotifications(_window);
                _devices.Suspending += (_, _) => cantAnswer();
                _displays = _devices.WatchDisplays(changed);
                _session = new SessionMonitor(_window);
                _session.Locked += (_, _) => cantAnswer();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Post(Action action) => _window.BeginInvoke(action);

        public IDisposable StartTimer(TimeSpan delay, Action elapsed) =>
            new Timer(_ => Post(elapsed), null, delay, Timeout.InfiniteTimeSpan);

        public void Dispose()
        {
            _session?.Dispose();
            _displays?.Dispose();
            _devices?.Dispose();
            _window.Dispose();
        }
    }
}
