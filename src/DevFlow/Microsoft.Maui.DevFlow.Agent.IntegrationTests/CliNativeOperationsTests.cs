using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "CliOperations")]
public class CliNativeOperationsTests(AppFixture app, ITestOutputHelper output) : IntegrationTestBase(app, output)
{
    [Fact]
    public async Task QueryCommand_ResolvesExistingNativeControl()
    {
        await NavigateToMainPageAsync();

        var result = await InvokeCliCommandAsync("ui", "query", "--automationId", "AddButton", "--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Stdout);
        Assert.Contains(document.RootElement.EnumerateArray(),
            element => element.GetProperty("automationId").GetString() == "AddButton");
    }

    [Fact]
    public async Task FillCommand_ChangesExistingEntryAndReturnsFixtureLease()
    {
        await NavigateToMainPageAsync();
        var previousText = (await FindElementAsync("NewTodoEntry")).Text ?? "";
        const string value = "CLI extraction 'quoted' & native input";

        try
        {
            var result = await InvokeCliCommandWithLeaseAsync(
                "ui", "fill", value, "--automationId", "NewTodoEntry", "--json");

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.Stdout);
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            await WaitForAsync(async () => (await FindElementAsync("NewTodoEntry")).Text == value);
        }
        finally
        {
            var entry = await FindElementAsync("NewTodoEntry");
            Assert.True(await Client.FillAsync(entry.Id, previousText));
        }
    }
}
