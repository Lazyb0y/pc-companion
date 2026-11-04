namespace PCCompanion.Modules.Files;

/// <summary>What a rule does when a file with the same name is already there.</summary>
public enum NameConflict
{
    /// <summary>Adds a number to the name, as File Explorer does, such as <c>report (2).pdf</c>.</summary>
    AddNumber,

    /// <summary>Leaves the file where it is.</summary>
    Skip,
}
