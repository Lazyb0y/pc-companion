using System;
using System.Collections.Generic;
using System.Drawing;
using PCCompanion.Modules.Windows.EndApp;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Windows.Tests;

/// <summary>Records what End App shows, asks and does outside its thread, without any real window, dialog or thread.</summary>
internal sealed class FakeEndAppView : IEndAppView, IEndAppHost
{
    /// <summary>Whether showing the covering window gets it the front.</summary>
    public bool TakesFront { get; set; } = true;

    public bool IsCovering { get; private set; }

    public int CoveringShown { get; private set; }

    public (Rectangle Bounds, int Thickness, bool CanEnd)? Frame { get; private set; }

    public bool? CanEndPointer { get; private set; }

    public List<string> Titles { get; } = [];

    public List<FakeQuestion> Questions { get; } = [];

    public FakeQuestion Question => Questions[^1];

    public int TaskManagerOpened { get; private set; }

    public List<(string Title, string Text)> Notifications { get; } = [];

    public bool ShowCovering()
    {
        CoveringShown++;
        IsCovering = true;
        return TakesFront;
    }

    public void HideCovering() => IsCovering = false;

    public void SetPointer(bool canEnd) => CanEndPointer = canEnd;

    public void SetTitle(string title) => Titles.Add(title);

    public void ShowFrame(Rectangle bounds, int thickness, bool canEnd) => Frame = (bounds, thickness, canEnd);

    public void HideFrame() => Frame = null;

    public IEndAppQuestion Ask(QuestionPage page, AppIcon? icon, Rectangle? area, Action<int> answered)
    {
        var question = new FakeQuestion(page, answered);
        Questions.Add(question);
        return question;
    }

    public void OpenTaskManager() => TaskManagerOpened++;

    public void Notify(string title, string text) => Notifications.Add((title, text));

    public void Background(Action work) => work();

    internal sealed class FakeQuestion(QuestionPage page, Action<int> answered) : IEndAppQuestion
    {
        public QuestionPage Page { get; private set; } = page;

        public int Changes { get; private set; }

        public bool Closed { get; private set; }

        public void Change(QuestionPage page)
        {
            Page = page;
            Changes++;
        }

        // Windows answers a closed dialog as canceled.
        public void Close()
        {
            Closed = true;
            answered(-1);
        }

        public void Answer(int button) => answered(button);
    }
}
