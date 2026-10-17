using System;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PCCompanion.Platform.Windows.Tests;

// Only reads Task Scheduler; creating and deleting tasks is checked in Windows Sandbox.
public sealed class ScheduledTasksTests
{
    private const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string Host = @"C:\Program Files\PCCompanion\PCCompanion.Host.exe";
    private static readonly XNamespace Task = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void ASignInTaskStartsTheProgramWithHighestRightsWhenTheUserSignsIn()
    {
        var task = XDocument.Parse(ScheduledTasks.CreateDefinition(Host, "--startup", Sid, atSignIn: true, "Starts the host."));

        Assert.Equal(Sid, Value(task, "Principal", "UserId"));
        Assert.Equal("InteractiveToken", Value(task, "Principal", "LogonType"));
        Assert.Equal("HighestAvailable", Value(task, "Principal", "RunLevel"));
        Assert.Equal(Sid, Value(task, "LogonTrigger", "UserId"));
        Assert.Equal($"\"{Host}\"", Value(task, "Exec", "Command"));
        Assert.Equal("--startup", Value(task, "Exec", "Arguments"));
        Assert.Equal(@"C:\Program Files\PCCompanion", Value(task, "Exec", "WorkingDirectory"));
        Assert.Equal("PT0S", Value(task, "Settings", "ExecutionTimeLimit"));
    }

    [Fact]
    public void AnOnRequestTaskHasNoTriggerAndNoArguments()
    {
        var task = XDocument.Parse(ScheduledTasks.CreateDefinition(Host, null, Sid, atSignIn: false, "Starts the host."));

        Assert.Empty(task.Descendants(Task + "Triggers").Elements());
        Assert.Empty(task.Descendants(Task + "Arguments"));
        Assert.Equal("true", Value(task, "Settings", "AllowStartOnDemand"));
    }

    [Fact]
    public void OnlyAdministratorsCanChangeTheTaskAndTheUserCanRunIt()
    {
        var task = XDocument.Parse(ScheduledTasks.CreateDefinition(Host, null, Sid, atSignIn: false, "Starts the host."));

        Assert.Equal($"D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;{Sid})", Value(task, "RegistrationInfo", "SecurityDescriptor"));
    }

    [Fact]
    public void AMissingTaskDoesNotExistAndAMissingFolderHasNoTasks()
    {
        var folder = $@"\PCCompanion.Tests.{Guid.NewGuid():N}\";

        Assert.False(ScheduledTasks.Exists(folder + "Missing"));
        Assert.Empty(ScheduledTasks.List(folder));
    }

    private static string? Value(XDocument task, string parent, string name) =>
        task.Descendants(Task + parent).Elements(Task + name).SingleOrDefault()?.Value;
}
