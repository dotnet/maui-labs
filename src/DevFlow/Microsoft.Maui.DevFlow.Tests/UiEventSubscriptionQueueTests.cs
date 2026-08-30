using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class UiEventSubscriptionQueueTests
{
    [Fact]
    public void Enqueue_WhenQueueIsFull_DropsOldestAndReportsLossBeforeRetainedEvents()
    {
        var queue = new UiEventSubscriptionQueue(2);
        queue.Enqueue("""{"type":"first"}""");
        queue.Enqueue("""{"type":"second"}""");
        queue.Enqueue("""{"type":"third"}""");

        Assert.Equal(2, queue.Count);
        Assert.Equal(1, queue.DroppedCount);

        Assert.True(queue.TryDequeue(out var loss));
        using var lossDocument = JsonDocument.Parse(loss);
        Assert.Equal("loss", lossDocument.RootElement.GetProperty("type").GetString());
        var lossData = lossDocument.RootElement.GetProperty("data");
        Assert.Equal("ui-events", lossData.GetProperty("stream").GetString());
        Assert.Equal("queue-overflow", lossData.GetProperty("reason").GetString());
        Assert.Equal(1, lossData.GetProperty("droppedCount").GetInt64());
        Assert.Equal(1, lossData.GetProperty("totalDroppedCount").GetInt64());

        Assert.True(queue.TryDequeue(out var second));
        Assert.True(queue.TryDequeue(out var third));
        Assert.Equal("""{"type":"second"}""", second);
        Assert.Equal("""{"type":"third"}""", third);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiEventSubscriptionQueue(0));
    }
}
