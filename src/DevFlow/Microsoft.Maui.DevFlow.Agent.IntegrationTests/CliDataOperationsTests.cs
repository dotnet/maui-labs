using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "CliOperations")]
[Trait(TestFramework.Trait, TestFramework.Maui)]
public class CliDataOperationsTests(AppFixture app, ITestOutputHelper output) : IntegrationTestBase(app, output)
{
    [Fact]
    public async Task PreferencesGetCommand_ReadsRealAppValue()
    {
        var key = $"integration_test_cli_{Guid.NewGuid():N}";
        const string value = "CLI extraction 'quoted' & stored value";

        try
        {
            await Client.SetPreferenceAsync(key, value);
            var result = await InvokeCliCommandAsync("storage", "preferences", "get", key, "--json");

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.Stdout);
            Assert.Equal(value, document.RootElement.GetProperty("value").GetString());
        }
        finally
        {
            await Client.DeletePreferenceAsync(key);
        }
    }

    [Fact]
    public async Task ExtensionDescribeCommand_ReadsRealSampleDescriptor()
    {
        var result = await InvokeCliCommandAsync("extensions", "describe", "com.example.diagnostics", "--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Stdout);
        Assert.NotEmpty(document.RootElement.GetProperty("tools").EnumerateArray());
    }
}
