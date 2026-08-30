using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class DeviceStepRecordingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"devflow-device-step-recording-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void PairedDeviceTap_IsPersistedAfterTheCurrentAppStep()
    {
        var recordings = new FlowRecordingStore();
        var coordinator = new BrokerFlowCoordinator(
            recordings,
            new FlowRecordingSpoolStore(_root));
        var started = coordinator.Start(
            "agent",
            "device step",
            "app",
            "android",
            preconditions: null);
        Assert.True(started.Ok);
        Assert.True(coordinator.Observe("agent", new FlowObservation
        {
            Action = FlowActions.Tap,
            AutomationId = "open-dialog",
            Type = "Button",
        }).Ok);

        var observed = coordinator.ObserveDeviceTap("agent", 120, 640);
        var stopped = coordinator.Stop("agent");

        Assert.True(observed.Ok);
        Assert.True(observed.Fragile);
        Assert.True(stopped.Ok, stopped.Error);
        var parsed = FlowMarkdown.Parse(stopped.Markdown!);
        Assert.True(parsed.Ok, parsed.Error);
        var deviceStep = Assert.Single(DeviceStep.FromExtensionData(parsed.Flow!.ExtensionData));
        Assert.Equal(1, deviceStep.AfterStep);
        Assert.Equal(120, deviceStep.X);
        Assert.Equal(640, deviceStep.Y);
    }
}
