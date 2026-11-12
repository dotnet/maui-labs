using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class ScrollResultTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValidateItemIndex_IndexInsideList_IsAccepted(int index)
    {
        var list = new CollectionView { ItemsSource = new[] { "a", "b", "c" } };

        Assert.Null(MauiDevFlowAgentService.ValidateItemIndex(list, index, -1));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(400)]
    [InlineData(-1)]
    public void ValidateItemIndex_IndexOutsideList_IsRejected(int index)
    {
        var list = new CollectionView { ItemsSource = new ObservableCollection<string> { "a", "b", "c" } };

        var error = MauiDevFlowAgentService.ValidateItemIndex(list, index, -1);

        Assert.NotNull(error);
        Assert.Contains("out of range", error);
    }

    [Fact]
    public void ValidateItemIndex_NonCollectionSource_IsCounted()
    {
        var list = new CollectionView { ItemsSource = Enumerable.Range(0, 5).Select(i => i.ToString()) };

        Assert.Null(MauiDevFlowAgentService.ValidateItemIndex(list, 4, -1));
        Assert.NotNull(MauiDevFlowAgentService.ValidateItemIndex(list, 5, -1));
    }

    [Fact]
    public void ValidateItemIndex_NoItemsSource_IsRejected()
    {
        Assert.NotNull(MauiDevFlowAgentService.ValidateItemIndex(new CollectionView(), 0, -1));
    }

    [Fact]
    public void ValidateItemIndex_GroupedList_ChecksGroupAndItem()
    {
        var list = new CollectionView
        {
            IsGrouped = true,
            ItemsSource = new List<List<string>> { new() { "a", "b" }, new() { "c" } }
        };

        Assert.Null(MauiDevFlowAgentService.ValidateItemIndex(list, 1, 0));
        Assert.Null(MauiDevFlowAgentService.ValidateItemIndex(list, 0, 1));
        Assert.Contains("group 1 has 1 item", MauiDevFlowAgentService.ValidateItemIndex(list, 1, 1));
        Assert.Contains("Group index 2", MauiDevFlowAgentService.ValidateItemIndex(list, 0, 2));
    }

    [Fact]
    public void ValidateItemIndex_GroupedListWithoutGroupIndex_IsLeftToThePlatform()
    {
        // The flat index of a grouped list (headers included or not) is platform-defined.
        var list = new CollectionView
        {
            IsGrouped = true,
            ItemsSource = new List<List<string>> { new() { "a", "b" }, new() { "c" } }
        };

        Assert.Null(MauiDevFlowAgentService.ValidateItemIndex(list, 4, -1));
    }

    [Theory]
    [InlineData(100, 50, 0, 400, 150)]   // inside the range
    [InlineData(380, 50, 0, 400, 400)]   // stops at the end
    [InlineData(400, 50, 0, 400, 400)]   // already at the end
    [InlineData(10, -50, 0, 400, 0)]     // stops at the start
    [InlineData(0, 50, 0, -20, 0)]       // content smaller than the viewport
    public void ClampOffset_StaysInsideTheScrollableRange(double current, double change, double min, double max, double expected)
    {
        Assert.Equal(expected, ScrollMovement.ClampOffset(current, change, min, max));
    }

    [Fact]
    public void Movement_ReportsWhetherTheViewMoved()
    {
        Assert.True(new ScrollMovement(0, -120).Moved);
        Assert.False(new ScrollMovement(0, 0).Moved);
        Assert.False(new ScrollMovement(0.2, -0.3).Moved);
        Assert.True(new ScrollMovement(0, 0).IsMeasured);
        Assert.False(ScrollMovement.Unmeasured.IsMeasured);
    }

    [Fact]
    public void ScrolledResponse_AtTheEnd_StillSucceedsButSaysNothingMoved()
    {
        using var json = JsonDocument.Parse(MauiDevFlowAgentService.ScrolledResponse(new ScrollMovement(0, 0)).Body!);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.False(root.GetProperty("scrolled").GetBoolean());
        Assert.Equal(0, root.GetProperty("actualDeltaY").GetDouble());
    }

    [Fact]
    public void ScrolledResponse_PartialMove_ReportsTheRealDelta()
    {
        using var json = JsonDocument.Parse(MauiDevFlowAgentService.ScrolledResponse(new ScrollMovement(0, -20)).Body!);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("scrolled").GetBoolean());
        Assert.Equal(-20, root.GetProperty("actualDeltaY").GetDouble());
        Assert.Equal("Scrolled", root.GetProperty("message").GetString());
    }

    [Fact]
    public void ScrolledResponse_Unmeasured_KeepsTheOldShape()
    {
        using var json = JsonDocument.Parse(MauiDevFlowAgentService.ScrolledResponse(ScrollMovement.Unmeasured).Body!);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Scrolled", root.GetProperty("message").GetString());
        Assert.False(root.TryGetProperty("scrolled", out _));
    }
}
