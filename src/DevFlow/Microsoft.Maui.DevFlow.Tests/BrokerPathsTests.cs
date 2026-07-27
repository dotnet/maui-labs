using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class BrokerPathsTests : IDisposable
{
    private readonly string _directoryPath = Path.Combine(
        Path.GetTempPath(),
        $"mauidevflow-broker-paths-{Guid.NewGuid():N}");

    [Fact]
    public void RestrictBrokerStatePermissions_OnUnix_UsesOwnerOnlyModes()
    {
        if (OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory(_directoryPath);
        var stateFilePath = Path.Combine(_directoryPath, "broker.json.tmp");

        BrokerPaths.RestrictConfigDirectoryPermissions(_directoryPath);
        File.WriteAllText(stateFilePath, "{}");
        BrokerPaths.RestrictStateFilePermissions(stateFilePath);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(_directoryPath));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(stateFilePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
            Directory.Delete(_directoryPath, recursive: true);
    }
}