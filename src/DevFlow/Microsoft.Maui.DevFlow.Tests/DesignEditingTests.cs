using System.Net;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class DesignEditingTests
{
    [Theory]
    [InlineData(0, new[] { "new", "a", "b" })]
    [InlineData(1, new[] { "a", "new", "b" })]
    [InlineData(2, new[] { "a", "b", "new" })]
    [InlineData(99, new[] { "a", "b", "new" })]
    [InlineData(null, new[] { "a", "b", "new" })]
    public async Task AddElement_InsertsIntoLayoutAtIndex(int? index, string[] expectedOrder)
    {
        var stack = Stack("add-stack", "a", "b");
        using var harness = await DesignEditingTestHarness.CreateAsync(stack);
        var stackId = await harness.GetElementIdAsync("add-stack");

        var result = await harness.Client.AddElementAsync(stackId, "<Label AutomationId=\"new\" Text=\"Added\" />", index);

        Assert.True(result.Success, result.Error);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal(stackId, result.ParentId);
        Assert.Equal(Array.IndexOf(expectedOrder, "new"), result.Index);
        Assert.NotNull(result.Element);
        Assert.Equal("Label", result.Element!.Type);
        Assert.Equal(expectedOrder, stack.Children.Cast<VisualElement>().Select(v => v.AutomationId));
        Assert.Equal("Added", Assert.IsType<Label>(stack.Children[Array.IndexOf(expectedOrder, "new")]).Text);

        // the returned id resolves immediately
        Assert.NotNull(await harness.Client.GetElementAsync(result.Element.Id));
    }

    [Fact]
    public async Task AddElement_HonorsNamespacesAndNestedContent()
    {
        var stack = Stack("nested-stack");
        using var harness = await DesignEditingTestHarness.CreateAsync(stack);
        var stackId = await harness.GetElementIdAsync("nested-stack");

        var result = await harness.Client.AddElementAsync(
            stackId,
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <Border xmlns="http://schemas.microsoft.com/dotnet/2021/maui" Padding="4">
                <Label Text="Inside" />
            </Border>
            """);

        Assert.True(result.Success, result.Error);
        var border = Assert.IsType<Border>(Assert.Single(stack.Children));
        Assert.Equal("Inside", Assert.IsType<Label>(border.Content).Text);
    }

    [Fact]
    public async Task AddElement_SetsEmptySingleContentParent()
    {
        var border = new Border { AutomationId = "empty-border" };
        using var harness = await DesignEditingTestHarness.CreateAsync(border);
        var borderId = await harness.GetElementIdAsync("empty-border");

        var result = await harness.Client.AddElementAsync(borderId, "<Label Text=\"Content\" />");

        Assert.True(result.Success, result.Error);
        Assert.Null(result.Index);
        Assert.Equal("Content", Assert.IsType<Label>(border.Content).Text);
    }

    [Theory]
    [InlineData("<Label Text=\"x\" />", "content-occupied")]
    [InlineData("<Label Text=\"one\" /><Label Text=\"two\" />", "invalid-xaml")]
    [InlineData("<Label Text=\"unclosed\">", "invalid-xaml")]
    [InlineData("<NotARealControl />", "invalid-xaml")]
    public async Task AddElement_RejectsInvalidRequests(string xaml, string reason)
    {
        var border = new Border { AutomationId = "occupied-border", Content = new Label { Text = "existing" } };
        using var harness = await DesignEditingTestHarness.CreateAsync(border);
        var borderId = await harness.GetElementIdAsync("occupied-border");

        var result = await harness.Client.AddElementAsync(borderId, xaml);

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(reason, result.Reason);
        Assert.Equal("existing", Assert.IsType<Label>(border.Content).Text);
    }

    [Fact]
    public async Task AddElement_RejectsParentThatCannotHoldChildren()
    {
        var label = new Label { AutomationId = "leaf-label" };
        using var harness = await DesignEditingTestHarness.CreateAsync(label);
        var labelId = await harness.GetElementIdAsync("leaf-label");

        var result = await harness.Client.AddElementAsync(labelId, "<Button />");

        Assert.False(result.Success);
        Assert.Equal("not-a-container", result.Reason);
    }

    [Fact]
    public async Task AddElement_UnknownParent_Returns404()
    {
        using var harness = await DesignEditingTestHarness.CreateAsync(Stack("unknown-parent-stack"));

        var result = await harness.Client.AddElementAsync("does-not-exist", "<Label />");

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task RemoveElement_DetachesFromLayoutAndSingleContent()
    {
        var stack = Stack("remove-stack", "a", "b");
        var border = new Border { AutomationId = "remove-border", Content = new Label { AutomationId = "border-child" } };
        using var harness = await DesignEditingTestHarness.CreateAsync(stack, border);
        var stackId = await harness.GetElementIdAsync("remove-stack");

        var fromStack = await harness.Client.RemoveElementAsync(await harness.GetElementIdAsync("a"));
        var fromBorder = await harness.Client.RemoveElementAsync(await harness.GetElementIdAsync("border-child"));

        Assert.True(fromStack.Success, fromStack.Error);
        Assert.Equal(stackId, fromStack.ParentId);
        Assert.Equal(["b"], stack.Children.Cast<VisualElement>().Select(v => v.AutomationId));
        Assert.True(fromBorder.Success, fromBorder.Error);
        Assert.Null(border.Content);
    }

    [Theory]
    [InlineData("a", 2, new[] { "b", "c", "a" })]
    [InlineData("c", 0, new[] { "c", "a", "b" })]
    [InlineData("b", 1, new[] { "a", "b", "c" })]
    public async Task MoveElement_ReordersWithinLayout(string moved, int index, string[] expected)
    {
        var stack = Stack("reorder-stack", "a", "b", "c");
        using var harness = await DesignEditingTestHarness.CreateAsync(stack);
        var stackId = await harness.GetElementIdAsync("reorder-stack");

        var result = await harness.Client.MoveElementAsync(await harness.GetElementIdAsync(moved), stackId, index);

        Assert.True(result.Success, result.Error);
        Assert.Equal(index, result.Index);
        Assert.Equal(expected, stack.Children.Cast<VisualElement>().Select(v => v.AutomationId));
    }

    [Fact]
    public async Task MoveElement_ReparentsAcrossContainers()
    {
        var source = Stack("source-stack", "a", "b");
        var target = new Border { AutomationId = "target-border" };
        using var harness = await DesignEditingTestHarness.CreateAsync(source, target);

        var result = await harness.Client.MoveElementAsync(
            await harness.GetElementIdAsync("b"),
            await harness.GetElementIdAsync("target-border"));

        Assert.True(result.Success, result.Error);
        Assert.Equal(["a"], source.Children.Cast<VisualElement>().Select(v => v.AutomationId));
        Assert.Equal("b", Assert.IsType<Label>(target.Content).AutomationId);
    }

    [Fact]
    public async Task MoveElement_IntoOwnDescendant_IsRejectedAndLeavesTreeUnchanged()
    {
        var inner = Stack("inner-stack", "leaf");
        var outer = new VerticalStackLayout { AutomationId = "outer-stack" };
        outer.Children.Add(inner);
        using var harness = await DesignEditingTestHarness.CreateAsync(outer);

        var result = await harness.Client.MoveElementAsync(
            await harness.GetElementIdAsync("inner-stack"),
            await harness.GetElementIdAsync("inner-stack"));

        Assert.False(result.Success);
        Assert.Equal("invalid-target", result.Reason);
        Assert.Same(inner, Assert.Single(outer.Children));
    }

    [Fact]
    public async Task MoveElement_IntoOccupiedContent_RestoresOriginalPosition()
    {
        var source = Stack("restore-stack", "a", "b");
        var occupied = new Border { AutomationId = "occupied", Content = new Label() };
        using var harness = await DesignEditingTestHarness.CreateAsync(source, occupied);

        var result = await harness.Client.MoveElementAsync(
            await harness.GetElementIdAsync("a"),
            await harness.GetElementIdAsync("occupied"));

        Assert.False(result.Success);
        Assert.Equal("content-occupied", result.Reason);
        // back where it was, index included - not appended to the end
        Assert.Equal(["a", "b"], source.Children.Cast<VisualElement>().Select(v => v.AutomationId));
    }

    [Fact]
    public async Task ClearProperty_RestoresDefaultValue()
    {
        var label = new Label { AutomationId = "clear-label", FontSize = 42, Text = "Keep" };
        using var harness = await DesignEditingTestHarness.CreateAsync(label);
        var labelId = await harness.GetElementIdAsync("clear-label");
        var defaultSize = (double)Label.FontSizeProperty.DefaultValue;

        var result = await harness.Client.ClearPropertyResultAsync(labelId, nameof(Label.FontSize));

        Assert.True(result.Success, result.Error);
        Assert.False(label.IsSet(Label.FontSizeProperty));
        Assert.Equal(defaultSize, label.FontSize);
        Assert.Equal("Keep", label.Text);
    }

    [Fact]
    public async Task ClearProperty_UnknownProperty_Fails()
    {
        var label = new Label { AutomationId = "clear-unknown" };
        using var harness = await DesignEditingTestHarness.CreateAsync(label);

        var result = await harness.Client.ClearPropertyResultAsync(await harness.GetElementIdAsync("clear-unknown"), "NoSuchProperty");

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task StructuralEdits_PublishTreeChangeEvents()
    {
        var stack = Stack("event-stack", "a");
        using var harness = await DesignEditingTestHarness.CreateAsync(stack);
        var stackId = await harness.GetElementIdAsync("event-stack");
        using var socket = await harness.SubscribeUiEventsAsync("treeChange");

        var added = await harness.Client.AddElementAsync(stackId, "<Label />");
        var addEvent = await DesignEditingTestHarness.ReceiveEventAsync(socket, "treeChange",
            data => data.GetProperty("changeType").GetString() == "added");
        Assert.Equal(added.Element!.Id, addEvent.GetProperty("elementId").GetString());
        Assert.Equal(stackId, addEvent.GetProperty("parentId").GetString());

        Assert.True((await harness.Client.RemoveElementAsync(added.Element.Id)).Success);
        var removeEvent = await DesignEditingTestHarness.ReceiveEventAsync(socket, "treeChange",
            data => data.GetProperty("changeType").GetString() == "removed");
        Assert.Equal(added.Element.Id, removeEvent.GetProperty("elementId").GetString());
    }

    [Fact]
    public async Task Highlight_ClearAlwaysSucceeds_AndUnknownElementIs404()
    {
        using var harness = await DesignEditingTestHarness.CreateAsync(Stack("highlight-stack"));

        Assert.True((await harness.Client.HighlightElementAsync(null)).Success);

        var missing = await harness.Client.HighlightElementAsync("does-not-exist");
        Assert.False(missing.Success);
        Assert.Equal(404, missing.StatusCode);
    }

    [Fact]
    public async Task PickMode_TapPublishesElementPickedAndTurnsItselfOff()
    {
        var small = new Label { AutomationId = "picked-label", WidthRequest = 10, HeightRequest = 10 };
        var stack = new VerticalStackLayout { AutomationId = "pick-stack" };
        stack.Children.Add(small);
        using var harness = await DesignEditingTestHarness.CreateWithWindowAsync(stack);
        await harness.GetElementIdAsync("picked-label");
        using var socket = await harness.SubscribeUiEventsAsync("elementPicked");

        Assert.True((await harness.Client.SetPickModeAsync(true)).Success);
        var overlay = harness.Service.GetSelectionOverlay();
        Assert.True(overlay.IsPickMode);

        // simulate the overlay reporting a tap over the label and its layout
        var picked = overlay.OnTapped(harness.App, [stack, small]);

        Assert.NotNull(picked);
        Assert.False(overlay.IsPickMode);
        var pickEvent = await DesignEditingTestHarness.ReceiveEventAsync(socket, "elementPicked");
        Assert.Equal("Label", pickEvent.GetProperty("elementType").GetString());
        Assert.False(string.IsNullOrEmpty(pickEvent.GetProperty("elementId").GetString()));
    }

    [Fact]
    public async Task PickMode_WithoutADiagnosticsOverlay_Returns501_RatherThanWaitingForATapThatCannotArrive()
    {
        // no window, so no IVisualDiagnosticsOverlay to intercept taps
        using var harness = await DesignEditingTestHarness.CreateAsync(Stack("no-overlay-stack"));

        var result = await harness.Client.SetPickModeAsync(true);

        Assert.False(result.Success);
        Assert.Equal(501, result.StatusCode);
        Assert.False(harness.Service.GetSelectionOverlay().IsPickMode);

        // turning it off is still a no-op success: there is nothing to restore
        Assert.True((await harness.Client.SetPickModeAsync(false)).Success);
    }

    [Fact]
    public async Task PickElement_TurnsPickModeOffAgain_WhenTheCallerCancels()
    {
        using var harness = await DesignEditingTestHarness.CreateWithWindowAsync(Stack("cancel-stack"));
        var overlay = harness.Service.GetSelectionOverlay();

        using var cancellation = new CancellationTokenSource();
        var pick = harness.Client.PickElementAsync(TimeSpan.FromMinutes(1), cancellation.Token);
        await DesignEditingTestHarness.WaitForAsync(() => overlay.IsPickMode, "pick mode to turn on", pick);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pick);
        Assert.False(overlay.IsPickMode);
    }

    [Fact]
    public async Task Capabilities_AdvertiseDesignTimeEditing()
    {
        using var harness = await DesignEditingTestHarness.CreateAsync(Stack("caps-stack"));

        using var http = new HttpClient();
        using var document = JsonDocument.Parse(await http.GetStringAsync($"http://localhost:{harness.Port}/api/v1/agent/capabilities"));
        var capabilities = document.RootElement.GetProperty("capabilities");

        foreach (var name in new[] { "ui.edit", "ui.xaml", "ui.highlight" })
            Assert.True(capabilities.GetProperty(name).GetProperty("supported").GetBoolean(), name);
    }

    [Theory]
    [InlineData("POST", "/api/v1/ui/elements/e1/children", "ui.edit")]
    [InlineData("DELETE", "/api/v1/ui/elements/e1", "ui.edit")]
    [InlineData("POST", "/api/v1/ui/elements/e1/move", "ui.edit")]
    [InlineData("DELETE", "/api/v1/ui/elements/e1/properties/Text", "ui.edit")]
    [InlineData("POST", "/api/v1/ui/xaml/reload", "ui.xaml")]
    [InlineData("PUT", "/api/v1/ui/highlight", "ui.highlight")]
    [InlineData("POST", "/api/v1/ui/pick", "ui.highlight")]
    public async Task BackendWithoutEditing_Returns501AndReportsUnsupported(string method, string path, string capability)
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port, RequireMutationLease = false });
        service.StartServerOnly(dispatcher: null);

        using var http = new HttpClient();
        HttpResponseMessage? response = null;
        for (var i = 0; i < 20 && response is null; i++)
        {
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), $"http://localhost:{port}{path}")
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                };
                response = await http.SendAsync(request);
            }
            catch (HttpRequestException)
            {
                await Task.Delay(100);
            }
        }

        using (response)
        {
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.NotImplemented, response!.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(capability, json.RootElement.GetProperty("capability").GetString());
        }

        using var capabilities = JsonDocument.Parse(await http.GetStringAsync($"http://localhost:{port}/api/v1/agent/capabilities"));
        Assert.False(capabilities.RootElement.GetProperty("capabilities").GetProperty(capability).GetProperty("supported").GetBoolean());
    }

    private static VerticalStackLayout Stack(string automationId, params string[] childIds)
    {
        var stack = new VerticalStackLayout { AutomationId = automationId };
        foreach (var id in childIds)
            stack.Children.Add(new Label { AutomationId = id, Text = id });
        return stack;
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
