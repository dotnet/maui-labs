using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Spectre.Console;
using Command = System.CommandLine.Command;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// A writer that records nothing. The command tree is what these tests pin; output formatting is
/// covered elsewhere.
/// </summary>
internal sealed class NoOpDevFlowOutputWriter : IDevFlowOutputWriter
{
    public bool ResolveJsonMode(bool jsonFlag, bool noJsonFlag) => jsonFlag && !noJsonFlag;
    public void WriteResult<T>(T data, bool json, Action<T>? humanFormatter = null) { }
    public void WriteResult<T>(T data, bool json, Action<T, IAnsiConsole> humanFormatter) { }
    public void WriteRawJson(string jsonString) { }
    public void WriteJsonElement(JsonElement element, bool json) { }
    public void WriteActionResult(bool success, string action, string? elementId, bool json, string? humanMessage = null) { }
    public void WriteError(string message, bool json, string errorType = "RuntimeError",
        bool retryable = false, string[]? suggestions = null) { }
    public void WriteJsonLine<T>(T data) { }
    public string FormatJson<T>(T data) => "";
}

/// <summary>
/// The device layer's command-line surface.
/// <para>
/// These pin the shape of the command tree rather than its behaviour, because the handlers talk
/// to a broker. The shape is what breaks silently when options are renamed or a subcommand is
/// dropped, and it is what documentation and skills reference by name.
/// </para>
/// </summary>
public class DeviceCommandsTests
{
    private static Command CreateDevicesCommand()
    {
        var cliAssembly = typeof(Microsoft.Maui.Cli.DevFlow.Broker.BrokerServer).Assembly;
        var type = cliAssembly.GetType("Microsoft.Maui.Cli.DevFlow.DeviceCommands")
            ?? throw new InvalidOperationException("DeviceCommands was not found.");

        var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("DeviceCommands.Create was not found.");

        return (Command)create.Invoke(null, [
            new Option<bool>("--json"),
            new Option<bool>("--no-json"),
            new NoOpDevFlowOutputWriter(),
        ])!;
    }

    [Fact]
    public void DevicesCommand_ExposesTheDeviceLayerVerbs()
    {
        var devices = CreateDevicesCommand();

        var names = devices.Subcommands.Select(c => c.Name).OrderBy(n => n).ToArray();

        Assert.Equal(["boot", "host", "list", "setup", "shutdown"], names);
    }

    [Fact]
    public void HostCommand_ExposesExplicitLifecycleVerbs()
    {
        var host = CreateDevicesCommand().Subcommands.Single(c => c.Name == "host");

        Assert.Equal(
            ["install", "mcp", "start", "status", "stop", "update"],
            host.Subcommands.Select(c => c.Name).OrderBy(n => n).ToArray());
        Assert.All(host.Subcommands, command => Assert.Empty(command.Arguments));
    }

    [Fact]
    public void SetupCommand_TakesNoArguments()
    {
        // It reports what this machine needs; there is nothing to point it at.
        var setup = CreateDevicesCommand().Subcommands.Single(c => c.Name == "setup");

        Assert.Empty(setup.Arguments);
    }

    [Fact]
    public void DevicesCommand_IsNamedForTheDeviceLayer()
    {
        Assert.Equal("devices", CreateDevicesCommand().Name);
    }

    [Theory]
    [InlineData("boot")]
    [InlineData("shutdown")]
    public void ControlCommands_TakeADeviceArgument(string subcommand)
    {
        // Control operations must never guess a target: acting on the wrong device is silent and
        // affects everything that follows.
        var command = CreateDevicesCommand().Subcommands.Single(c => c.Name == subcommand);

        var argument = Assert.Single(command.Arguments);
        Assert.Equal("device", argument.Name);
    }

    [Fact]
    public void ListCommand_TakesNoRequiredArguments()
    {
        var list = CreateDevicesCommand().Subcommands.Single(c => c.Name == "list");

        Assert.Empty(list.Arguments);
    }

    [Fact]
    public void EverySubcommand_HasADescription()
    {
        // Descriptions are the entire help surface; an undescribed command is undiscoverable.
        var devices = CreateDevicesCommand();

        Assert.False(string.IsNullOrWhiteSpace(devices.Description));
        Assert.All(devices.Subcommands, c => Assert.False(string.IsNullOrWhiteSpace(c.Description)));
    }
}
