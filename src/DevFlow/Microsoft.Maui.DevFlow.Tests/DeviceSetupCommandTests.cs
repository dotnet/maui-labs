using System.Reflection;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// <c>maui devflow devices setup</c>.
/// <para>
/// The command diagnoses rather than downloads: silently fetching and executing a binary is not
/// something a diagnostic should do. What it must get right is giving each state a <em>different</em>
/// instruction — "not installed" and "installed but unusable" look identical to a user and need
/// opposite responses.
/// </para>
/// </summary>
public class DeviceSetupCommandTests
{
    private static (string State, string Action) Describe(DeviceHostHealth health)
    {
        var type = typeof(Microsoft.Maui.Cli.DevFlow.Broker.BrokerServer).Assembly
            .GetType("Microsoft.Maui.Cli.DevFlow.DeviceCommands")!;
        var method = type.GetMethod("DescribeSetup", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = method.Invoke(null, [health])!;

        var tuple = (ValueTuple<string, string>)result;
        return (tuple.Item1, tuple.Item2);
    }

    [Fact]
    public void AbsentHost_SaysDevFlowStillWorks()
    {
        // Absence is the common case. It must not read as a broken installation.
        var (state, action) = Describe(DeviceHostHealth.Unavailable);

        Assert.Contains("no device host", state, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DevFlow works without it", action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AvailableHost_PointsAtTheNextCommand()
    {
        var (state, action) = Describe(new DeviceHostHealth
        {
            Availability = DeviceHostAvailability.Available,
            Version = "0.1.6",
        });

        Assert.Contains("ready", state, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0.1.6", state);
        Assert.Contains("devices list", action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnauthorizedHost_SaysToRestartIt()
    {
        var (_, action) = Describe(DeviceHostHealth.Unauthorized());

        Assert.Contains("restart", action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IncompatibleHost_SaysToAlignVersions()
    {
        var (state, action) = Describe(DeviceHostHealth.Incompatible("2.0"));

        Assert.Contains("2.0", state);
        Assert.Contains("version", action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotRespondingHost_SaysToStartItAgain()
    {
        var (_, action) = Describe(DeviceHostHealth.NotResponding("gone"));

        Assert.Contains("again", action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryStateGivesADistinctInstruction()
    {
        // The whole point of the command: if two states produced the same advice, the user could
        // not tell which situation they were in.
        var actions = new[]
        {
            Describe(DeviceHostHealth.Unavailable).Action,
            Describe(DeviceHostHealth.NotResponding("gone")).Action,
            Describe(DeviceHostHealth.Unauthorized()).Action,
            Describe(DeviceHostHealth.Incompatible("2.0")).Action,
            Describe(new DeviceHostHealth { Availability = DeviceHostAvailability.Available }).Action,
        };

        Assert.Equal(actions.Length, actions.Distinct(StringComparer.Ordinal).Count());
        Assert.All(actions, a => Assert.False(string.IsNullOrWhiteSpace(a)));
    }
}
