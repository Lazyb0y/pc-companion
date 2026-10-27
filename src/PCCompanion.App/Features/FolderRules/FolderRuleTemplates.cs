using System;
using System.IO;
using PCCompanion.Localization;
using PCCompanion.Modules.Files.FolderRules;

namespace PCCompanion.App.Features.FolderRules;

/// <summary>What Add rule starts from.</summary>
public enum RuleTemplate
{
    Empty,

    /// <summary>Every file into a subfolder for its extension.</summary>
    SortByType,

    /// <summary>Every file into <c>{year}\{month}</c>.</summary>
    SortByMonth,

    /// <summary>Installers into an Installers folder in the folder.</summary>
    Installers,

    /// <summary>Documents into the user's Documents folder.</summary>
    Documents,
}

/// <summary>
/// The rules Add rule starts from, which open in the rule editor with the files they'd act on.
/// A rule needs a condition, so the two that sort every file say so with the name pattern <c>*</c>.
/// </summary>
public static class FolderRuleTemplates
{
    /// <param name="folder">The watched folder the rule is for.</param>
    public static FolderRule Create(RuleTemplate template, string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return template switch
        {
            RuleTemplate.SortByType => new FolderRule { Name = Strings.FolderRulesTemplateSortByType, Names = ["*"], Destination = Path.Join(folder, "{extension}") },
            RuleTemplate.SortByMonth => new FolderRule { Name = Strings.FolderRulesTemplateSortByMonth, Names = ["*"], Destination = Path.Join(folder, "{year}", "{month}") },
            RuleTemplate.Installers => new FolderRule
            {
                Name = Strings.FolderRulesTemplateInstallersName,
                Extensions = FileKinds.Extensions(FileKind.Installers),
                Destination = Path.Join(folder, Strings.FolderRulesTemplateInstallersName),
            },
            RuleTemplate.Documents => new FolderRule
            {
                Name = Strings.FolderRulesTemplateDocumentsName,
                Extensions = FileKinds.Extensions(FileKind.Documents),
                Destination = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            },
            _ => new FolderRule(),
        };
    }
}
