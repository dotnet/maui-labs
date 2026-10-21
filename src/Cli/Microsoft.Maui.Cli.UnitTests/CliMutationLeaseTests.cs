using Microsoft.Maui.Cli.DevFlow;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class CliMutationLeaseTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "devflow-lease-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ConsecutiveCommandsShareOneIdentity()
    {
        var first = CliMutationLease.Resolve(explicitId: null, _directory);
        var second = CliMutationLease.Resolve(explicitId: null, _directory);

        Assert.Equal(first, second);
        Assert.Equal(first, File.ReadAllText(Path.Combine(_directory, CliMutationLease.FileName)));
    }

    [Fact]
    public void ExplicitIdentityWinsAndIsNotStored()
    {
        var id = CliMutationLease.Resolve(explicitId: " agent-two ", _directory);

        Assert.Equal("agent-two", id);
        Assert.False(File.Exists(Path.Combine(_directory, CliMutationLease.FileName)));
    }

    [Fact]
    public void CorruptStoredIdentityIsReplaced()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, CliMutationLease.FileName), "not valid\nat all");

        var id = CliMutationLease.Resolve(explicitId: null, _directory);

        Assert.Matches("^[0-9a-f]{32}$", id);
        Assert.Equal(id, CliMutationLease.Resolve(explicitId: null, _directory));
    }
}
