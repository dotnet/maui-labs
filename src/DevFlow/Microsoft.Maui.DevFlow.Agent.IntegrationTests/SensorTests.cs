using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "Sensors")]
public class SensorTests : IntegrationTestBase
{
    public SensorTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task List_ReturnsSensors()
    {
        var json = await Client.GetSensorsAsync();

        Assert.True(json.ValueKind != JsonValueKind.Undefined);
        Output.WriteLine($"Sensors: {json}");
    }

    [Fact]
    public async Task Start_Accelerometer_HandlesGracefully()
    {
        try
        {
            var result = await Client.StartSensorAsync("accelerometer", speed: "UI");
            Output.WriteLine($"Start accelerometer result: {result}");

            if (result)
                await Client.StopSensorAsync("accelerometer");
        }
        catch (HttpRequestException ex)
        {
            Output.WriteLine($"Accelerometer not available: {ex.Message}");
        }
    }

    [Fact]
    public async Task Stop_UnstartedSensor_HandlesGracefully()
    {
        try
        {
            var result = await Client.StopSensorAsync("accelerometer");
            Output.WriteLine($"Stop unstarted sensor result: {result}");
        }
        catch (HttpRequestException ex)
        {
            Output.WriteLine($"Stop sensor error (expected): {ex.Message}");
        }
    }

    [Fact]
    public async Task StartStop_Lifecycle_Works()
    {
        var sensorsJson = await Client.GetSensorsAsync();
        var sensorsText = sensorsJson.ToString();

        if (sensorsText.Contains("accelerometer", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var started = await Client.StartSensorAsync("accelerometer");
                Output.WriteLine($"Started: {started}");

                await SettleAsync();

                var stopped = await Client.StopSensorAsync("accelerometer");
                Output.WriteLine($"Stopped: {stopped}");
            }
            catch (HttpRequestException ex)
            {
                Output.WriteLine($"Sensor lifecycle not supported: {ex.Message}");
            }
        }
        else
        {
            Output.WriteLine("No accelerometer available — skipping lifecycle test.");
        }
    }
}
