using System;
using System.Linq;
using PCCompanion.Modules.Input.HotCorners;
using Xunit;

namespace PCCompanion.Modules.Input.Tests;

public sealed class HotCornerActionNamesTests
{
    // The page builds its list of actions from this one, so a new action must be added to it.
    [Fact]
    public void ThePageListsEveryActionOnce()
    {
        Assert.Equal(Enum.GetValues<HotCornerAction>().Order(), HotCornerActionNames.InPageOrder.Order());
    }

    [Fact]
    public void ThePageListsNothingFirst()
    {
        Assert.Equal(HotCornerAction.None, HotCornerActionNames.InPageOrder[0]);
    }
}
