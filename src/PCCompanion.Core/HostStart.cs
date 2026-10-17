namespace PCCompanion.Core;

/// <summary>What a host does as it starts. See <see cref="HostProcess.DecideStart"/>.</summary>
public enum HostStart
{
    /// <summary>Runs.</summary>
    Run,

    /// <summary>
    /// Exits: started at sign-in with Start with Windows off, or left to the copy the sign-in
    /// task of Always run as administrator starts.
    /// </summary>
    Exit,

    /// <summary>
    /// Runs the on-request task of Always run as administrator and exits. If the task doesn't
    /// start, runs without admin rights.
    /// </summary>
    RunTask,
}
