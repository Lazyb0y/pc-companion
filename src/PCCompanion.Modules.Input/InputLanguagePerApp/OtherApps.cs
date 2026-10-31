namespace PCCompanion.Modules.Input.InputLanguagePerApp;

/// <summary>What apps without a rule get.</summary>
public enum OtherApps
{
    /// <summary>Each app gets back the input method it had when the user last left it.</summary>
    Remember,

    /// <summary>They aren't switched: Windows does what its own setting says.</summary>
    Keep,

    /// <summary>They all switch to one input method.</summary>
    Use,
}
