using System;
using System.Collections.Generic;
using PCCompanion.Modules.Files.FolderRules;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Files.Tests;

/// <summary>
/// A Recycle Bin that answers at once with <see cref="Outcome"/>, or, for a file in
/// <see cref="Asking"/>, says Windows asks and then waits until the test answers.
/// </summary>
internal sealed class FakeRecycleBin(FakeFolderRulesSystem system) : IRecycleBin
{
    private readonly Dictionary<string, Action<RecycleResult>> _waiting = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What happens to the files, unless they're in <see cref="Asking"/>.</summary>
    public RecycleResult Outcome { get; set; } = new(RecycleOutcome.Recycled);

    /// <summary>The files Windows asks about, as for one too big for the Recycle Bin.</summary>
    public HashSet<string> Asking { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsDisposed { get; private set; }

    public void Recycle(string path, Action asking, Action<RecycleResult> done)
    {
        system.Note($"recycle {path}");
        if (Asking.Contains(path))
        {
            lock (_waiting)
            {
                _waiting[path] = done;
            }

            asking();
            return;
        }

        Finish(path, Outcome, done);
    }

    /// <summary>Answers Windows' question about a file, as the user would.</summary>
    public void Answer(string path, RecycleOutcome outcome)
    {
        Action<RecycleResult> done;
        lock (_waiting)
        {
            done = _waiting[path];
            _waiting.Remove(path);
        }

        Finish(path, new RecycleResult(outcome), done);
    }

    public void Dispose() => IsDisposed = true;

    private void Finish(string path, RecycleResult result, Action<RecycleResult> done)
    {
        if (result.Outcome is RecycleOutcome.Recycled or RecycleOutcome.DeletedForGood)
        {
            system.Remove(path);
        }

        done(result);
    }
}
