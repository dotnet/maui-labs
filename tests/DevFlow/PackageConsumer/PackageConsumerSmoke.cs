using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Testing.PackageConsumer;

/// <summary>
/// Compile-only references that prove the package's framework-neutral public surface can be
/// consumed without a project reference.
/// </summary>
public static class PackageConsumerSmoke
{
    public static IReadOnlyList<Type> PublicContracts { get; } =
    [
        typeof(MauiFlow),
        typeof(FlowMarkdown),
        typeof(FlowValidator),
        typeof(MauiFlowRunner),
        typeof(MauiFlowRunnerOptions),
        typeof(MauiTestPlan),
        typeof(MauiFlowRunReport),
        typeof(IMauiFlowDriver),
    ];
}
