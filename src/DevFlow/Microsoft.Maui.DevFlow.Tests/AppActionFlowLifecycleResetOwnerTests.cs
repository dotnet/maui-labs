using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The owner resets an app by invoking an action inside it, so every refusal below happens before
/// the app is touched. A reset that runs and then cannot be attested has already destroyed the
/// state it existed to restore.
/// </summary>
public class AppActionFlowLifecycleResetOwnerTests
{
    private static AppActionFlowLifecycleResetOwner Owner()
        => new(agentPort: 1, "com.example.app", "device-1", "app:tfm:1.0");

    [Fact]
    public async Task ResetAsync_RefusesAStrategyItCannotPerform()
    {
        var outcome = await Owner().ResetAsync(new FlowLifecycleResetRequest
        {
            RequiredStrategy = "uninstall-reinstall",
        });

        Assert.False(outcome.Succeeded);
        Assert.Equal("repair-reset-strategy-unsupported", outcome.FailureCode);
    }

    [Fact]
    public async Task ResetAsync_RefusesABackendSeedItCannotApply()
    {
        var outcome = await Owner().ResetAsync(new FlowLifecycleResetRequest
        {
            RequiredStrategy = AppActionFlowLifecycleResetOwner.ResetStrategy,
            RequiresBackendSeed = true,
        });

        Assert.False(outcome.Succeeded);
        Assert.Equal("repair-backend-seed-unsupported", outcome.FailureCode);
    }

    [Fact]
    public async Task ResetAsync_RefusesAPinnedCollectionItemItCannotSeed()
    {
        var outcome = await Owner().ResetAsync(new FlowLifecycleResetRequest
        {
            RequiredStrategy = AppActionFlowLifecycleResetOwner.ResetStrategy,
            RequiresCollectionItemKey = true,
        });

        Assert.False(outcome.Succeeded);
        Assert.Equal("repair-collection-item-unsupported", outcome.FailureCode);
    }

    [Fact]
    public async Task ResetAsync_RefusesWhenTheAppRegistersNoResetAction()
    {
        // Nothing is listening on this port, so the action list cannot be read. An app that cannot
        // be asked is treated as not supporting the protocol rather than assumed to support it.
        var outcome = await Owner().ResetAsync(new FlowLifecycleResetRequest
        {
            RequiredStrategy = AppActionFlowLifecycleResetOwner.ResetStrategy,
        });

        Assert.False(outcome.Succeeded);
        Assert.Equal("repair-reset-action-unavailable", outcome.FailureCode);
    }

    [Fact]
    public async Task GetAppliedStateAsync_IsNullBeforeTheOwnerHasResetAnything()
    {
        Assert.Null(await Owner().GetAppliedStateAsync());
    }

    [Fact]
    public void ResetStrategy_IsNotAPackageLevelStrategyName()
    {
        // Naming uninstall-reinstall or pm-clear would claim a package-level guarantee that an
        // in-process action does not provide, and would also restart the process the validation is
        // fenced to.
        Assert.Equal("app-action-reset", AppActionFlowLifecycleResetOwner.ResetStrategy);
        Assert.DoesNotContain("uninstall", AppActionFlowLifecycleResetOwner.ResetStrategy);
        Assert.DoesNotContain("clear", AppActionFlowLifecycleResetOwner.ResetStrategy);
    }
}
