using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Microsoft.Maui.Cli.UnitTests.Fixtures;

[CollectionDefinition("CLI", DisableParallelization = true)]
public sealed class CliCollection
{
}
