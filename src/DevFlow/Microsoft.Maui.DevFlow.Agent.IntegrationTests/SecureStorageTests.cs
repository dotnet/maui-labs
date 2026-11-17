using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "SecureStorage")]
// Requires the MAUI sample: SecureStorage is an Essentials API. Native runs filter this out.
[Trait(TestFramework.Trait, TestFramework.Maui)]
public class SecureStorageTests : IntegrationTestBase
{
    const string TestKeyPrefix = "integration_test_secure_";

    public SecureStorageTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task Set_StoresValue()
    {
        if (await AssertUnavailableGtkProviderAsync())
            return;

        var key = $"{TestKeyPrefix}set";
        var result = await Client.SetSecureStorageAsync(key, "secure_value");

        Assert.True(result.ValueKind != JsonValueKind.Undefined);
        await Client.DeleteSecureStorageAsync(key);
    }

    [Fact]
    public async Task Get_RetrievesStoredValue()
    {
        if (await AssertUnavailableGtkProviderAsync())
            return;

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
        if (await AssertUnavailableGtkProviderAsync())
            return;

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
        if (await AssertUnavailableGtkProviderAsync())
            return;

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
        if (await AssertUnavailableGtkProviderAsync())
            return;

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

    async Task<bool> AssertUnavailableGtkProviderAsync()
    {
        if (Platform != "gtk")
            return false;

        var path = $"/api/v1/storage/secure/{TestKeyPrefix}availability";
        using var probe = await GetRawAsync(path);
        if (probe.IsSuccessStatusCode)
            return false;

        await AssertUnavailableResponseAsync(probe);
        var lease = await Client.ControlMutationLeaseAsync("claim");
        Assert.True(lease.YouHold, lease.Error);
        using var setRequest = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = new StringContent("""{"value":"availability-check"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        setRequest.Headers.Add("X-DevFlow-Lease", Client.MutationLeaseId);
        setRequest.Headers.Add("X-DevFlow-Holder", Client.MutationLeaseHolderKind);
        using var set = await Http.SendAsync(setRequest);
        await AssertUnavailableResponseAsync(set);
        using var delete = await DeleteRawAsync(path);
        await AssertUnavailableResponseAsync(delete);
        using var clear = await DeleteRawAsync("/api/v1/storage/secure");
        await AssertUnavailableResponseAsync(clear);
        return true;
    }

    async Task AssertUnavailableResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        var error = document.RootElement.GetProperty("error").GetString();
        Assert.NotNull(error);
        Assert.True(
            error.Contains("Secure storage requires", StringComparison.Ordinal) ||
            error.Contains("org.freedesktop.secrets was not provided", StringComparison.Ordinal) ||
            error.Contains("No service for type 'Microsoft.Maui.Storage.ISecureStorage'", StringComparison.Ordinal),
            $"Expected an unavailable GTK SecureStorage provider error, got: {error}");
        Output.WriteLine($"Expected unavailable GTK SecureStorage provider: {error}");
    }
}
