using System.Diagnostics;
using AIExtensions.Sample.ChatPlayground;

namespace Microsoft.Maui.AI.Chat.Tests;

[Collection("Chat diagnostics")]
public sealed class ChatDiagnosticGroupsTests
{
    [Fact]
    public void ApplySnapshot_ReconcilesInterleavedGroupsWithoutReplacingRetainedGroups()
    {
        var groups = new ChatDiagnosticGroups();
        groups.ApplySnapshot([
            Entry(1, "a"), Entry(2, "b"), Entry(3, "a"), Entry(4, null), Entry(5, "b"),
        ]);
        Assert.Equal(new string?[] { "a", "b", null }, groups.Select(group => group.TraceId));
        Assert.Equal([1L, 3L], groups[0].Select(entry => entry.Sequence));
        Assert.Equal([2L, 5L], groups[1].Select(entry => entry.Sequence));
        var a = groups[0];
        var b = groups[1];
        var uncorrelated = groups[2];

        groups.ApplySnapshot([Entry(3, "a"), Entry(4, null), Entry(5, "b"), Entry(6, "b")]);
        Assert.Equal(new string?[] { "a", null, "b" }, groups.Select(group => group.TraceId));
        Assert.Same(a, groups[0]);
        Assert.Same(uncorrelated, groups[1]);
        Assert.Same(b, groups[2]);
        Assert.Equal([3L], a.Select(entry => entry.Sequence));
        Assert.Equal([5L, 6L], b.Select(entry => entry.Sequence));

        groups.ApplySnapshot([Entry(7, "b"), Entry(8, "c")]);
        Assert.Equal(new string?[] { "b", "c" }, groups.Select(group => group.TraceId));
        Assert.Same(b, groups[0]);
        Assert.Equal([7L], b.Select(entry => entry.Sequence));
        groups.ApplySnapshot([]);
        Assert.Empty(groups);
        groups.ApplySnapshot([Entry(9, "a")]);
        Assert.NotSame(a, Assert.Single(groups));
        Assert.Equal("Trace a", groups[0].Title);
    }

    [Fact]
    public void ApplySnapshot_FollowsBoundedReceiverEvictionAndClear()
    {
        using var receiver = new ChatDiagnostics();
        using var source = new ActivitySource(ChatDiagnostics.SourceName);
        var groups = new ChatDiagnosticGroups();
        for (var i = 0; i < 600; i++)
        {
            using (source.StartActivity("request")) { }
            if (i % 50 == 0)
            {
                groups.ApplySnapshot(receiver.Snapshot().Entries);
                Assert.InRange(groups.Count, 1, ChatDiagnostics.Capacity);
            }
        }
        var snapshot = receiver.Snapshot().Entries;
        groups.ApplySnapshot(snapshot);
        Assert.Equal(ChatDiagnostics.Capacity, groups.Count);
        Assert.All(groups, group => Assert.Single(group));
        Assert.Equal(snapshot, groups.SelectMany(group => group).ToArray());
        receiver.Clear();
        groups.ApplySnapshot(receiver.Snapshot().Entries);
        Assert.Empty(groups);
    }

    private static ChatDiagnosticEntry Entry(long sequence, string? traceId) =>
        new(sequence, "heading", "message", "details", traceId);
}
