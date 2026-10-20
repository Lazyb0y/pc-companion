using System.IO;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using PCCompanion.Localization;
using PCCompanion.Modules.Clipboard.ClipboardActions;

namespace PCCompanion.App.Features.ClipboardActions;

/// <summary>
/// The rule editor's fields and what's wrong with them. A problem shows once its box has
/// something in it; Save is available when the rule has no problem.
/// </summary>
public sealed partial class ClipboardRuleEditor : ObservableObject
{
    private const int OpenAddressIndex = 0;
    private const int RunProgramIndex = 1;

    private string _name;
    private string _pattern;
    private bool _matchCase;
    private string _sample = string.Empty;
    private int _actionIndex;
    private string _address;
    private string _program;
    private string _arguments;

    /// <param name="rule">The rule to edit, or <see langword="null"/> for a new one.</param>
    public ClipboardRuleEditor(ClipboardRule? rule)
    {
        IsNew = rule is null;
        rule ??= new ClipboardRule();
        _name = rule.Name;
        _pattern = rule.Pattern;
        _matchCase = rule.MatchCase;
        _actionIndex = rule.KnownAction == ClipboardRuleAction.RunProgram ? RunProgramIndex : OpenAddressIndex;
        _address = rule.Address;
        _program = rule.Program;
        _arguments = rule.Arguments;
    }

    public bool IsNew { get; }

    public string Title => IsNew ? Strings.ClipboardActionsAddTitle : Strings.ClipboardActionsEditTitle;

    public string Name
    {
        get => _name;
        set => Change(ref _name, value);
    }

    public string Pattern
    {
        get => _pattern;
        set => Change(ref _pattern, value);
    }

    public bool MatchCase
    {
        get => _matchCase;
        set => Change(ref _matchCase, value);
    }

    /// <summary>A sample text to try the pattern on.</summary>
    public string Sample
    {
        get => _sample;
        set => Change(ref _sample, value);
    }

    /// <summary>0 for Open a web address, 1 for Run a program.</summary>
    public int ActionIndex
    {
        get => _actionIndex;
        set => Change(ref _actionIndex, value);
    }

    public bool IsOpenAddress => _actionIndex != RunProgramIndex;

    public bool IsRunProgram => _actionIndex == RunProgramIndex;

    public string Address
    {
        get => _address;
        set => Change(ref _address, value);
    }

    public string Program
    {
        get => _program;
        set => Change(ref _program, value);
    }

    public string Arguments
    {
        get => _arguments;
        set => Change(ref _arguments, value);
    }

    public string PatternProblem => string.IsNullOrEmpty(_pattern) ? string.Empty : ClipboardRuleCheck.CompilePattern(_pattern, _matchCase, out var problem) switch
    {
        null when problem == Modules.Clipboard.ClipboardActions.PatternProblem.NotSupported => Strings.ClipboardActionsPatternNotSupported,
        null => Strings.ClipboardActionsPatternInvalid,
        _ => string.Empty,
    };

    public bool HasPatternProblem => PatternProblem.Length > 0;

    /// <summary>Whether the pattern matches the sample, once both are there and the pattern can be used.</summary>
    public string SampleResult
    {
        get
        {
            if (_sample.Length == 0 || ClipboardRuleCheck.CompilePattern(_pattern, _matchCase, out _) is not { } pattern)
            {
                return string.Empty;
            }

            return ClipboardRuleCheck.Matches(pattern, _sample.Trim()) ? Strings.ClipboardActionsTryMatches : Strings.ClipboardActionsTryNoMatch;
        }
    }

    public bool HasSampleResult => SampleResult.Length > 0;

    public string AddressProblem =>
        string.IsNullOrWhiteSpace(_address) || ClipboardRuleCheck.CheckAddress(_address) == Modules.Clipboard.ClipboardActions.AddressProblem.None
            ? string.Empty
            : Strings.ClipboardActionsAddressNotWeb;

    public bool HasAddressProblem => AddressProblem.Length > 0;

    public string ProgramProblem => string.IsNullOrWhiteSpace(_program) ? string.Empty : ClipboardRuleCheck.CheckProgram(_program.Trim(), File.Exists) switch
    {
        RuleProgramProblem.NotFullPath => Strings.ClipboardActionsProgramNotFullPath,
        RuleProgramProblem.NotExe => Strings.ClipboardActionsProgramNotExe,
        RuleProgramProblem.NotFound => Strings.ClipboardActionsProgramNotFound,
        RuleProgramProblem.CommandInterpreter => Strings.ClipboardActionsProgramInterpreter,
        _ => string.Empty,
    };

    public bool HasProgramProblem => ProgramProblem.Length > 0;

    public bool CanSave
    {
        get
        {
            var rule = ToRule();
            return ClipboardRuleCheck.Check(rule, out _) == RuleProblem.None
                && (IsOpenAddress || ClipboardRuleCheck.CheckProgram(rule.Program, File.Exists) == RuleProgramProblem.None);
        }
    }

    /// <summary>
    /// The rule as edited. It keeps the address, program and arguments of the action not chosen,
    /// so choosing that action again brings them back.
    /// </summary>
    public ClipboardRule ToRule() => new()
    {
        Name = _name.Trim(),
        Pattern = _pattern,
        MatchCase = _matchCase,
        Action = (IsRunProgram ? ClipboardRuleAction.RunProgram : ClipboardRuleAction.OpenAddress).ToString(),
        Address = _address.Trim(),
        Program = _program.Trim(),
        Arguments = _arguments,
    };

    // Every field affects what's shown below the boxes and whether Save is available.
    private void Change<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return;
        }

        OnPropertyChanged(nameof(IsOpenAddress));
        OnPropertyChanged(nameof(IsRunProgram));
        OnPropertyChanged(nameof(PatternProblem));
        OnPropertyChanged(nameof(HasPatternProblem));
        OnPropertyChanged(nameof(SampleResult));
        OnPropertyChanged(nameof(HasSampleResult));
        OnPropertyChanged(nameof(AddressProblem));
        OnPropertyChanged(nameof(HasAddressProblem));
        OnPropertyChanged(nameof(ProgramProblem));
        OnPropertyChanged(nameof(HasProgramProblem));
        OnPropertyChanged(nameof(CanSave));
    }
}
