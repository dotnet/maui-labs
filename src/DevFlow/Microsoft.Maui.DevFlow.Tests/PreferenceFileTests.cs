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

    public void Dispose() => File.Delete(_path);

    private sealed class TestService : DevFlowAgentService
    {
        public static IReadOnlyCollection<string> Read(string path, string? sharedName = null, bool nestedStore = false)
            => ReadPreferenceFileKeys(path, sharedName, nestedStore);
    }
}
