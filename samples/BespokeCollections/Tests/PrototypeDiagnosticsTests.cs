using System.Text.Json;
using BespokeCollections.Core;
using Xunit;

namespace BespokeCollections.Tests;

public sealed class PrototypeDiagnosticsTests
{
    [Fact]
    public void Report_SerializeRoundTrip_PreservesRunIdentityAndSeparateSnapshots()
    {
        var startedAt = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
        var report = new PrototypeReport("passed", "android", "Release", ["native reuse"],
            12, 12, Snapshot(500, 14), Snapshot(50, 7), 11, 500, 50, 1024, 2048,
            "not-qualified", Guid.NewGuid(), startedAt, startedAt.AddSeconds(20), true);

        var json = JsonSerializer.Serialize(report, PhotoJsonContext.Default.PrototypeReport);
        var result = JsonSerializer.Deserialize(json, PhotoJsonContext.Default.PrototypeReport);

        Assert.NotNull(result);
        Assert.Equal(report.RunId, result.RunId);
        Assert.Equal(report.StartedAt, result.StartedAt);
        Assert.Equal(report.CompletedAt, result.CompletedAt);
        Assert.True(result.ScrollWorkload);
        Assert.Equal("Release", result.Configuration);
        Assert.Equal(500, result.Native.ItemCount);
        Assert.Equal(14, result.Native.Created);
        Assert.Equal(50, result.ReplacementNative.ItemCount);
        Assert.Equal(7, result.ReplacementNative.Created);
        Assert.Equal("not-qualified", result.PerformanceQualification);
    }

    [Fact]
    public void Failure_WithoutNativeHandler_PreservesRunIdentityAndExplicitError()
    {
        var startedAt = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
        var failure = new PrototypeFailure("failed", "Fixture unavailable", null,
            Guid.NewGuid(), startedAt, startedAt.AddSeconds(15), false);

        var json = JsonSerializer.Serialize(failure, PhotoJsonContext.Default.PrototypeFailure);
        var result = JsonSerializer.Deserialize(json, PhotoJsonContext.Default.PrototypeFailure);

        Assert.Equal(failure, result);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("Fixture unavailable", document.RootElement.GetProperty("error").GetString());
        Assert.Equal(failure.RunId, document.RootElement.GetProperty("runId").GetGuid());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("native").ValueKind);
        Assert.False(document.RootElement.TryGetProperty("assertions", out _));
    }

    static NativeMasonrySnapshot Snapshot(int count, int created) =>
        new("android", "RecyclerView", "MasonryManager", true, false,
            count, 6, created, 20, 14, 8, 30, 0, new(0, 0, 400, 700),
            1024, 4096, 2, 0, []);
}
