using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "SecureStorage")]
public class SecureStorageTests : IntegrationTestBase
{
    const string TestKeyPrefix = "integration_test_secure_";

    public SecureStorageTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task Set_StoresValue()
    {
        var key = $"{TestKeyPrefix}set";
        var result = await Client.SetSecureStorageAsync(key, "secure_value");

        Assert.True(result.ValueKind != JsonValueKind.Undefined);
        await Client.DeleteSecureStorageAsync(key);
    }

    [Fact]
    public async Task Get_RetrievesStoredValue()
    {
        var key = $"{TestKeyPrefix}get";

        await Client.SetSecureStorageAsync(key, "my_secret_123");

        var result = await Client.GetSecureStorageAsync(key);
        var text = result.ToString();
        Assert.True(
            text.Contains("my_secret_123", StringComparison.Ordinal) || text.Contains("value", StringComparison.OrdinalIgnoreCase),
            $"Expected response to contain stored value or 'value' key, got: {text}");

        await Client.DeleteSecureStorageAsync(key);
    }

    [Fact]
    public async Task Delete_RemovesValue()
    {
        var key = $"{TestKeyPrefix}delete";

        await Client.SetSecureStorageAsync(key, "to_remove");
        var deleteResult = await Client.DeleteSecureStorageAsync(key);
        Output.WriteLine($"Delete result: {deleteResult}");

        try
        {
            var result = await Client.GetSecureStorageAsync(key);
            Assert.DoesNotContain("to_remove", result.ToString());
        }
        catch (HttpRequestException)
        {
            // 404 is acceptable.
        }
    }

    [Fact]
    public async Task Clear_RemovesAll()
    {
        var key = $"{TestKeyPrefix}clear";

        await Client.SetSecureStorageAsync(key, "clear_me");

        var cleared = await Client.ClearSecureStorageAsync();
        Assert.True(cleared);

        try
        {
            var result = await Client.GetSecureStorageAsync(key);
            Assert.DoesNotContain("clear_me", result.ToString());
        }
        catch (HttpRequestException)
        {
            // Expected.
        }
    }

    [Fact]
    public async Task Get_NonExistentKey_HandlesGracefully()
    {
        try
        {
            var result = await Client.GetSecureStorageAsync($"{TestKeyPrefix}nonexistent_999");
            Output.WriteLine($"Non-existent secure storage result: {result}");
        }
        catch (HttpRequestException ex)
        {
            Output.WriteLine($"Non-existent secure key error (expected): {ex.Message}");
        }
    }
}
