using System.Collections.Generic;
using PCCompanion.Modules.Windows.TitleBarActions;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>The pointer hook's claims and the actions Title Bar Actions takes, recorded instead of done.</summary>
internal sealed class FakeTitleBarActionsSystem : ITitleBarActionsSystem
{
    public PointerKeys HeldKeys { get; set; }

    public nint ForegroundWindow { get; set; }

    /// <summary>What the hook did with each click, by its time; never seen unless a test says so.</summary>
    public Dictionary<uint, PointerInputOutcome> Outcomes { get; } = [];

    public List<PointerClaim?> Claims { get; } = [];

    public PointerClaim? Claimed => Claims.Count > 0 ? Claims[^1] : null;

    public HashSet<nint> WithoutMinimizeButton { get; } = [];

    public HashSet<nint> Topmost { get; } = [];

    public Dictionary<nint, nint> Behind { get; } = [];

    public List<nint> Minimized { get; } = [];

    public List<nint> SentToBack { get; } = [];

    public List<nint> BroughtToFront { get; } = [];

    public int Sounds { get; private set; }

    public int HookSetAgain { get; private set; }

    public bool Refuses { get; set; }

    public void Claim(PointerClaim? claim) => Claims.Add(claim);

    public PointerInputOutcome Outcome(uint time) => Outcomes.GetValueOrDefault(time, PointerInputOutcome.NotSeen);

    public void SetHookAgain() => HookSetAgain++;

    public bool HasMinimizeButton(nint window) => !WithoutMinimizeButton.Contains(window);

    public bool IsTopmost(nint window) => Topmost.Contains(window);

    public nint NextWindowBehind(nint window) => Behind.GetValueOrDefault(window);

    public bool AskToMinimize(nint window)
    {
        if (!Refuses)
        {
            Minimized.Add(window);
        }

        return !Refuses;
    }

    public bool SendToBack(nint window)
    {
        if (!Refuses)
        {
            SentToBack.Add(window);
        }

        return !Refuses;
    }

    public bool BringToFront(nint window)
    {
        BroughtToFront.Add(window);
        return true;
    }

    public void PlayWarningSound() => Sounds++;
}
