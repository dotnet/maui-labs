using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenerativeUI.Sample.Garden.Server.Tests;

/// <summary>
/// Keeps the checked-in OpenAPI snapshot honest. Boots the Garden server in-memory, fetches the live
/// <c>/openapi/v1.json</c>, and asserts it matches <c>tests/snapshots/garden.openapi.json</c>. Run with
/// the environment variable <c>UPDATE_OPENAPI=1</c> to regenerate the snapshot after intentional API
/// changes. Because this test fails on any drift, the committed JSON can never go stale — the reducer
/// tests can rely on it offline.
/// </summary>
public sealed class OpenApiSnapshotTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OpenApiSnapshotTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Generated_document_matches_checked_in_snapshot()
    {
        using var client = _factory.CreateClient();
        var live = Normalize(await client.GetStringAsync("/openapi/v1.json"));

        var snapshotPath = SnapshotPath();
        var regenerate = string.Equals(Environment.GetEnvironmentVariable("UPDATE_OPENAPI"), "1", StringComparison.Ordinal);

        if (regenerate || !File.Exists(snapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllTextAsync(snapshotPath, live);
        }

        var expected = Normalize(await File.ReadAllTextAsync(snapshotPath));

        Assert.True(
            expected == live,
            $"The generated OpenAPI document does not match {snapshotPath}. " +
            "Re-run with UPDATE_OPENAPI=1 to accept intentional API changes.");
    }

    private static string Normalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string SnapshotPath([CallerFilePath] string? callerFilePath = null)
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(callerFilePath)!, "..", "snapshots", "garden.openapi.json"));
}
