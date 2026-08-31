using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class ShellActivationLogicTests
{
    [Fact]
    public void ShellItemRoute_IsNormalizedForSemanticActivation()
    {
        var item = new FlyoutItem { Route = "dialogs" };

        Assert.Equal("//dialogs", ShellActivationLogic.ResolveShellItemRoute(item));
    }

    [Fact]
    public void BlankShellItemRoute_FallsBackToDirectSelection()
    {
        var item = new FlyoutItem { Route = "   " };

        Assert.Null(ShellActivationLogic.ResolveShellItemRoute(item));
    }
}
