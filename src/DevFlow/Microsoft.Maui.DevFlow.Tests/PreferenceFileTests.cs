using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class PreferenceFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"devflow-preferences-{Guid.NewGuid():N}.json");

    [Fact]
    public void Read_MissingFile_ReturnsEmptyKeys()
    {
        Assert.Empty(TestService.Read(_path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n ")]
    [InlineData("{")]
    public void Read_InterruptedWrite_ThrowsJsonException(string content)
    {
        File.WriteAllText(_path, content);
        Assert.ThrowsAny<JsonException>(() => TestService.Read(_path));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"not a store\"")]
    public void Read_InvalidRoot_ThrowsInvalidDataException(string content)
    {
        File.WriteAllText(_path, content);
        Assert.Throws<InvalidDataException>(() => TestService.Read(_path));
    }

    [Fact]
    public void Read_EmptyObject_ReturnsEmptyKeys()
    {
        File.WriteAllText(_path, "{}");
        Assert.Empty(TestService.Read(_path));
    }

    [Fact]
    public void Read_FlatStore_PreservesAllKeys()
    {
        File.WriteAllText(_path, """{"":"empty key","theme":"dark","enabled":true}""");
        Assert.Equal(new[] { "", "theme", "enabled" }, TestService.Read(_path));
    }

    [Theory]
    [InlineData(null, "default_key")]
    [InlineData("", "default_key")]
    [InlineData("shared", "shared_key")]
    [InlineData(" ", "whitespace_key")]
    public void Read_NestedStore_SelectsExactBucket(string? sharedName, string expectedKey)
    {
        File.WriteAllText(_path, """{"":{"default_key":"v"},"shared":{"shared_key":"v"}," ":{"whitespace_key":"v"}}""");
        Assert.Equal(new[] { expectedKey }, TestService.Read(_path, sharedName, nestedStore: true));
    }

    [Fact]
    public void Read_NestedStoreMissingBucket_ReturnsEmptyKeys()
    {
        File.WriteAllText(_path, """{"other":{"key":"v"}}""");
        Assert.Empty(TestService.Read(_path, "missing", nestedStore: true));
    }

    [Theory]
    [InlineData("""{"shared":null}""")]
    [InlineData("""{"shared":[]}""")]
    public void Read_InvalidBucket_ThrowsInvalidDataException(string content)
    {
        File.WriteAllText(_path, content);
        Assert.Throws<InvalidDataException>(() => TestService.Read(_path, "shared", nestedStore: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("shared")]
    public async Task List_NativeEmpty_ReportsCompleteAndForwardsContainer(string? sharedName)
    {
        var invoked = false;
        using var service = new TestService(name =>
        {
            invoked = true;
            Assert.Equal(sharedName, name);
            return Array.Empty<string>();
        });
        var request = new HttpRequest();
        if (sharedName is not null)
            request.QueryParams["sharedName"] = sharedName;

        var response = await service.ListAsync(request);
        using var document = JsonDocument.Parse(response.Body!);
        var result = document.RootElement;

        Assert.True(invoked);
        Assert.Empty(result.GetProperty("keys").EnumerateArray());
        Assert.Equal("native", result.GetProperty("source").GetString());
        Assert.True(result.GetProperty("complete").GetBoolean());
        Assert.Equal(sharedName, result.GetProperty("sharedName").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("enumerationError").ValueKind);
    }

    [Fact]
    public async Task List_FailedFileRead_ReportsIncompleteAndError()
    {
        File.WriteAllText(_path, "");
        using var service = new TestService(_ => TestService.Read(_path));

        var response = await service.ListAsync(new HttpRequest());
        using var document = JsonDocument.Parse(response.Body!);
        var result = document.RootElement;

        Assert.Equal("registry", result.GetProperty("source").GetString());
        Assert.False(result.GetProperty("complete").GetBoolean());
        Assert.StartsWith("Native preference enumeration failed:",
            result.GetProperty("enumerationError").GetString());
    }

    [Fact]
    public async Task List_UnsupportedEnumeration_ReportsIncompleteAndReason()
    {
        using var service = new TestService(_ => null);

        var response = await service.ListAsync(new HttpRequest());
        using var document = JsonDocument.Parse(response.Body!);
        var result = document.RootElement;

        Assert.Equal("registry", result.GetProperty("source").GetString());
        Assert.False(result.GetProperty("complete").GetBoolean());
        Assert.Equal("Native preference enumeration is not available.",
            result.GetProperty("enumerationError").GetString());
    }

    public void Dispose() => File.Delete(_path);

    private sealed class TestService : MauiDevFlowAgentService
    {
        private readonly Func<string?, IReadOnlyCollection<string>?> _enumerate;

        public TestService(Func<string?, IReadOnlyCollection<string>?> enumerate)
            => _enumerate = enumerate;

        protected override IReadOnlyCollection<string>? EnumerateNativePreferenceKeys(string? sharedName)
            => _enumerate(sharedName);

        public Task<HttpResponse> ListAsync(HttpRequest request) => HandlePreferencesList(request);

        public static IReadOnlyCollection<string> Read(string path, string? sharedName = null, bool nestedStore = false)
            => ReadPreferenceFileKeys(path, sharedName, nestedStore);
    }
}
