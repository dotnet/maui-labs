using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Microsoft.Maui.Cli.UnitTests.Fixtures;

[CollectionDefinition("CLI", DisableParallelization = true)]
public sealed class CliCollection
{
}

/// <summary>
/// Serializes test classes that mutate
/// <c>StandardErrorToolsLogger.DefaultVerbose</c>.
/// </summary>
/// <remarks>
/// The assembly currently disables parallelization outright, so this is belt-and-braces. It
/// exists so the isolation survives that setting being relaxed: any test class that touches the
/// static must carry <c>[Collection("StandardErrorToolsLogger static state")]</c>, otherwise it
/// can race a class that mutates it and produce order-dependent flakes.
/// </remarks>
[CollectionDefinition("StandardErrorToolsLogger static state", DisableParallelization = true)]
public sealed class StandardErrorToolsLoggerStateCollection
{
}
