using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "Preferences")]
public class PreferencesTests : IntegrationTestBase
{
    const string TestKeyPrefix = "integration_test_";

    public PreferencesTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task Set_StoresValue()
    {
        var key = $"{TestKeyPrefix}set_test";
        var result = await Client.SetPreferenceAsync(key, "hello world");

        Assert.True(result.ValueKind != JsonValueKind.Undefined);
        await Client.DeletePreferenceAsync(key);
    }

    [Fact]
    public async Task Get_RetrievesStoredValue()
    {
        var key = $"{TestKeyPrefix}get_test";

        await Client.SetPreferenceAsync(key, "test_value_123");

        var result = await Client.GetPreferenceAsync(key);
        Assert.Contains("test_value_123", result.ToString());

        await Client.DeletePreferenceAsync(key);
    }

    [Fact]
    public async Task List_IncludesSetKey()
    {
        var key = $"{TestKeyPrefix}list_test";

        await Client.SetPreferenceAsync(key, "list_value");

        var list = await Client.GetPreferencesAsync();
        Assert.Contains(key, list.ToString());

        await Client.DeletePreferenceAsync(key);
    }

    [Fact]
    public async Task Delete_RemovesKey()
    {
        var key = $"{TestKeyPrefix}delete_test";

        await Client.SetPreferenceAsync(key, "to_delete");
        await Client.DeletePreferenceAsync(key);

        await WaitForAsync(async () =>
        {
            var list = await Client.GetPreferencesAsync();
            return !list.ToString().Contains(key, StringComparison.Ordinal);
        }, timeoutMs: 5000, pollIntervalMs: 250);
    }

    [Fact]
    public async Task Clear_RemovesAllTestKeys()
    {
        var key1 = $"{TestKeyPrefix}clear_1";
        var key2 = $"{TestKeyPrefix}clear_2";

        await Client.SetPreferenceAsync(key1, "value1");
        await Client.SetPreferenceAsync(key2, "value2");

        var cleared = await Client.ClearPreferencesAsync();
        Assert.True(cleared);

        var list = await Client.GetPreferencesAsync();
        var text = list.ToString();
        Assert.DoesNotContain(key1, text);
        Assert.DoesNotContain(key2, text);
    }

    [Fact]
    public async Task Get_NonExistentKey_HandlesGracefully()
    {
        try
        {
            var result = await Client.GetPreferenceAsync($"{TestKeyPrefix}nonexistent_999");
            Output.WriteLine($"Non-existent preference result: {result}");
        }
        catch (HttpRequestException ex)
        {
            Output.WriteLine($"Non-existent preference error (expected): {ex.Message}");
        }
    }

    [Fact]
    public async Task Set_TypedValue_Works()
    {
        var key = $"{TestKeyPrefix}typed_test";

        var result = await Client.SetPreferenceAsync(key, "42", type: "int");
        Assert.True(result.ValueKind != JsonValueKind.Undefined);

        var value = await Client.GetPreferenceAsync(key, type: "int");
        Assert.Contains("42", value.ToString());

        await Client.DeletePreferenceAsync(key);
    }
}
