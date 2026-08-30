using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class FlowDeclaredCheckpointTests
{
    [Fact]
    public void ApplyDeclaredCheckpoint_ProjectsEveryAgentObservableField()
    {
        var context = Context();

        FlowStateEvidenceProviderRegistry.ApplyDeclaredCheckpoint(context, new MauiFlowCheckpointRequirements
        {
            AppBuildFingerprint = "1.0:1",
            Route = "//native",
            Window = "window-0",
            Modal = "none",
            Locale = "en-US",
            Theme = "Light",
            Orientation = "Portrait",
            DisplayProfile = "411x914@2.6",
        });

        var expected = context.Preconditions!.Expected!;
        Assert.Equal("1.0:1", expected.AppBuildFingerprint);
        Assert.Equal("//native", expected.Route);
        Assert.Equal("window-0", expected.Window);
        Assert.Equal("none", expected.Modal);
        Assert.Equal("en-US", expected.Locale);
        Assert.Equal("Light", expected.Theme);
        Assert.Equal("Portrait", expected.Orientation);
        Assert.Equal("411x914@2.6", expected.DisplayProfile);
    }

    [Fact]
    public void ApplyDeclaredCheckpoint_DoesNotProjectFieldsTheAgentCannotObserve()
    {
        var context = Context();

        FlowStateEvidenceProviderRegistry.ApplyDeclaredCheckpoint(context, new MauiFlowCheckpointRequirements
        {
            Route = "//native",
            SeedFingerprint = "app-seed-1",
            BackendStateFingerprint = "backend-seed-1",
            CollectionItemKey = "item-1",
        });

        var expected = context.Preconditions!.Expected!;
        Assert.Equal("//native", expected.Route);
        // A live agent never reports these, so projecting them would make every run fail the
        // checkpoint comparison instead of enforcing anything. They stay a provider obligation.
        Assert.Null(expected.SeedFingerprint);
        Assert.Null(expected.BackendStateFingerprint);
        Assert.Null(expected.CollectionItemKey);
    }

    [Fact]
    public void ApplyDeclaredCheckpoint_KeepsAProviderSuppliedExpectationOverThePlanDeclaration()
    {
        var context = Context();
        context.Preconditions!.Expected!.Route = "//provider";

        FlowStateEvidenceProviderRegistry.ApplyDeclaredCheckpoint(
            context,
            new MauiFlowCheckpointRequirements { Route = "//native" });

        // A conflicting declaration is not silently overwritten; the evaluator reports it as
        // precondition-declaration-route-mismatch instead.
        Assert.Equal("//provider", context.Preconditions.Expected!.Route);
    }

    [Fact]
    public void ApplyDeclaredCheckpoint_TrimsDeclaredValues()
    {
        var context = Context();

        FlowStateEvidenceProviderRegistry.ApplyDeclaredCheckpoint(
            context,
            new MauiFlowCheckpointRequirements { Route = "  //native  ", Window = "   " });

        Assert.Equal("//native", context.Preconditions!.Expected!.Route);
        Assert.Null(context.Preconditions.Expected.Window);
    }

    [Fact]
    public void ApplyDeclaredCheckpoint_WithNoDeclaration_LeavesTheExpectationUnchanged()
    {
        var context = Context();

        FlowStateEvidenceProviderRegistry.ApplyDeclaredCheckpoint(context, declared: null);

        Assert.Null(context.Preconditions!.Expected!.Route);
    }

    [Fact]
    public void ApplyDeclaredCheckpoint_WithoutPreconditions_DoesNotThrow()
    {
        var context = new MauiFlowRunContext();

        FlowStateEvidenceProviderRegistry.ApplyDeclaredCheckpoint(
            context,
            new MauiFlowCheckpointRequirements { Route = "//native" });

        Assert.Null(context.Preconditions);
    }

    private static MauiFlowRunContext Context() => new()
    {
        Intent = MauiFlowReplayIntents.OrdinaryReplay,
        Preconditions = new MauiFlowReplayPreconditions
        {
            Expected = new MauiFlowCheckpoint(),
            ObservationDeferredUntilLaunch = true,
        },
    };
}
