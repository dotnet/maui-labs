using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "UiActions")]
public class UiActionTests : IntegrationTestBase
{
    public UiActionTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task Fill_Entry_SetsText()
    {
        await NavigateToMainPageAsync();
        var entry = await FindElementAsync("NewTodoEntry");

        var result = await Client.FillAsync(entry.Id, "Integration test text");
        if (!result)
        {
            await SettleAsync();
            entry = await FindElementAsync("NewTodoEntry");
            result = await Client.FillAsync(entry.Id, "Integration test text");
        }

        Assert.True(result);

        await SettleAsync();
        var updated = await FindElementAsync("NewTodoEntry");
        Assert.Equal("Integration test text", updated.Text);

        await Client.ClearAsync(entry.Id);
    }

    [Fact]
    public async Task Clear_Entry_RemovesText()
    {
        await NavigateToMainPageAsync();
        var entry = await FindElementAsync("NewTodoEntry");

        await Client.FillAsync(entry.Id, "Text to clear");
        await SettleAsync();

        var result = await Client.ClearAsync(entry.Id);
        Assert.True(result);

        await SettleAsync();
        var updated = await FindElementAsync("NewTodoEntry");
        Assert.True(string.IsNullOrEmpty(updated.Text));
    }

    [Fact]
    public async Task Focus_Entry_SetsFocus()
    {
        await NavigateToMainPageAsync();
        var entry = await FindElementAsync("NewTodoEntry");

        var result = await Client.FocusAsync(entry.Id);
        if (!result)
        {
            await SettleAsync();
            entry = await FindElementAsync("NewTodoEntry");
            result = await Client.FocusAsync(entry.Id);
        }

        Assert.True(result);
    }

    [Fact]
    public async Task Tap_Button_TriggersAction()
    {
        await NavigateToMainPageAsync();
        var entry = await FindElementAsync("NewTodoEntry");
        var addButton = await FindElementAsync("AddButton");

        await Client.FillAsync(entry.Id, "Integration Test Todo");
        await SettleAsync();

        var tapResult = await Client.TapAsync(addButton.Id);
        Assert.True(tapResult);

        await SettleAsync();
        var countLabel = await FindElementAsync("CountLabel");
        Assert.NotNull(countLabel.Text);
        Assert.Contains("items", countLabel.Text!);

        await CleanupAddedTodoAsync("Integration Test Todo");
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Tap_ReadOnlyInput_InvokesGesture()
    {
        await NavigateToPageAsync("//gestures", "TapInput");
        try
        {
            var before = (await FindElementAsync("InputTapStatusLabel")).Text;
            var input = await FindElementAsync("TapInput");

            Assert.True(await Client.TapAsync(input.Id));
            await WaitForAsync(async () => (await FindElementAsync("InputTapStatusLabel")).Text != before);

            Assert.StartsWith("input tap: ", (await FindElementAsync("InputTapStatusLabel")).Text);
            Assert.Equal("Tap to select an item", (await FindElementAsync("TapInput")).Text);
        }
        finally
        {
            await NavigateToMainPageAsync();
        }
    }

    [Fact]
    public async Task Fill_AndTap_AddsTodo()
    {
        await NavigateToMainPageAsync();
        await SettleAsync(1000);

        var entry = await FindElementAsync("NewTodoEntry");
        var addButton = await FindElementAsync("AddButton");

        await Client.ClearAsync(entry.Id);
        await SettleAsync();
        await Client.FocusAsync(entry.Id);
        await SettleAsync();
        await Client.FillAsync(entry.Id, "IntegrationTodo123");
        await SettleAsync(1000);

        await Client.TapAsync(addButton.Id);

        await WaitForAsync(async () =>
        {
            var items = await Client.QueryAsync(text: "IntegrationTodo123");
            return items.Count > 0;
        }, timeoutMs: 5000, pollIntervalMs: 500);

        var addedItems = await Client.QueryAsync(text: "IntegrationTodo123");
        Assert.NotEmpty(addedItems);

        await CleanupAddedTodoAsync("IntegrationTodo123");
    }

    [Fact]
    public async Task Navigate_ToRoute_ChangesPage()
    {
        await NavigateToPageAsync("//interactions", "StatusLabel");
        var statusLabel = await TryFindElementAsync("StatusLabel");
        Assert.NotNull(statusLabel);

        await NavigateToMainPageAsync();
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Navigate_ToMultipleRoutes_Works()
    {
        await NavigateToPageAsync("//interactions", "StatusLabel");
        var interactionsEl = await TryFindElementAsync("StatusLabel");
        Assert.NotNull(interactionsEl);

        await NavigateToPageAsync("//network", "GetPostsButton");
        var networkEl = await TryFindElementAsync("GetPostsButton");
        Assert.NotNull(networkEl);

        await NavigateToPageAsync("//dialogs", "DialogStatusLabel");
        var dialogEl = await TryFindElementAsync("DialogStatusLabel");
        Assert.NotNull(dialogEl);

        await NavigateToMainPageAsync();
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Back_Action_GoesBack()
    {
        await NavigateToMainPageAsync();
        await Client.NavigateAsync("//interactions");
        await SettleAsync();

        var response = await PostRawAsync("/api/v1/ui/actions/back", new { });
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Scroll_Element_Succeeds()
    {
        await NavigateToMainPageAsync();
        var todoList = await TryFindElementAsync("TodoList");

        if (todoList == null)
        {
            Output.WriteLine("TodoList not found — skipping scroll test.");
            return;
        }

        var result = await Client.ScrollAsync(todoList.Id, deltaY: 100);
        Assert.True(result);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Resize_Window_Succeeds()
    {
        var result = await Client.ResizeAsync(800, 600);
        Assert.True(result);

        await SettleAsync();
        await Client.ResizeAsync(1024, 768);
    }

    [Fact]
    public async Task Tap_CheckBox_TogglesState()
    {
        await NavigateToMainPageAsync();
        await SettleAsync();

        var checkBoxes = await Client.QueryAsync(automationId: "TodoCheckBox");
        if (checkBoxes.Count == 0)
        {
            Output.WriteLine("No TodoCheckBox found — skipping checkbox toggle test.");
            return;
        }

        var checkBox = checkBoxes[0];
        var tapResult = await Client.TapAsync(checkBox.Id);
        Assert.True(tapResult);

        await SettleAsync();

        checkBoxes = await Client.QueryAsync(automationId: "TodoCheckBox");
        if (checkBoxes.Count > 0)
            await Client.TapAsync(checkBoxes[0].Id);
    }

    [Fact]
    public async Task Tap_InteractionPageButton_UpdatesStatus()
    {
        await NavigateToPageAsync("//interactions", "StatusLabel");

        var button = await FindElementAsync("TestButton");
        await Client.TapAsync(button.Id);

        await SettleAsync();
        var statusLabel = await FindElementAsync("StatusLabel");
        Assert.NotNull(statusLabel.Text);
        Assert.Contains("button", statusLabel.Text!, StringComparison.OrdinalIgnoreCase);

        await NavigateToMainPageAsync();
    }

    [Fact]
    public async Task Tap_Switch_TogglesState()
    {
        await NavigateToPageAsync("//interactions", "TestSwitch");

        var sw = await FindElementAsync("TestSwitch");
        var tapResult = await Client.TapAsync(sw.Id);
        Assert.True(tapResult);

        await SettleAsync();
        var statusLabel = await FindElementAsync("StatusLabel");
        Assert.NotNull(statusLabel.Text);
        Assert.Contains("switch", statusLabel.Text!, StringComparison.OrdinalIgnoreCase);

        sw = await FindElementAsync("TestSwitch");
        await Client.TapAsync(sw.Id);

        await NavigateToMainPageAsync();
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Tap_ImageButton_TriggersAction()
    {
        await NavigateToPageAsync("//interactions", "TestImageButton");

        var imageButton = await FindElementAsync("TestImageButton");
        var tapResult = await Client.TapAsync(imageButton.Id);
        Assert.True(tapResult);

        await SettleAsync();
        var statusLabel = await FindElementAsync("StatusLabel");
        Assert.NotNull(statusLabel.Text);
        Assert.Contains("image button", statusLabel.Text!, StringComparison.OrdinalIgnoreCase);

        await NavigateToMainPageAsync();
    }

    [Fact]
    public async Task Batch_MultipleActions_ExecutesAll()
    {
        await NavigateToMainPageAsync();
        var entry = await FindElementAsync("NewTodoEntry");
        var addButton = await FindElementAsync("AddButton");

        var response = await PostRawAsync("/api/v1/ui/actions/batch", new
        {
            actions = new object[]
            {
                new { action = "fill", elementId = entry.Id, text = "Batch Test Todo" },
                new { action = "tap", elementId = addButton.Id },
            },
            continueOnError = false,
        });

        Assert.True(response.IsSuccessStatusCode);
        await SettleAsync();

        await CleanupAddedTodoAsync("Batch Test Todo");
    }

    [Fact]
    public async Task Key_ReturnsExpectedResponse()
    {
        var response = await PostRawAsync("/api/v1/ui/actions/key", new { key = "Return" });

        Assert.True(
            response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotImplemented,
            $"Expected 200 or 501, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Gesture_ReturnsExpectedResponse()
    {
        var response = await PostRawAsync("/api/v1/ui/actions/gesture", new
        {
            type = "swipe",
            direction = "up",
            distance = 100,
        });

        Assert.True(
            response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotImplemented,
            $"Expected 200 or 501, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Gesture_UnknownType_IsRejected()
    {
        using var response = await PostRawAsync("/api/v1/ui/actions/gesture", new { type = "wiggle" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (TestFramework.IsNative)
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            Assert.Contains("not supported", document.RootElement.GetProperty("error").GetString());
        }
        else
            Assert.Contains("Supported types", body);
    }

    [Fact]
    public async Task Capabilities_AdvertisesSupportedGestures()
    {
        var caps = await GetJsonAsync("/api/v1/agent/capabilities");
        var actions = caps.GetProperty("capabilities").GetProperty("ui.actions");
        Assert.True(actions.GetProperty("supported").GetBoolean());
        if (TestFramework.IsNative)
        {
            Assert.Equal(1, actions.GetProperty("version").GetInt32());
            var features = actions.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()).ToArray();
            Assert.Contains("gesture", features);
            Assert.Contains("tap", features);
            return;
        }
        var gestures = actions.GetProperty("gestures")
            .EnumerateArray().Select(g => g.GetString()).ToArray();

        Assert.Contains("pinch", gestures);
        Assert.Contains("pan", gestures);
        Assert.Contains("rotate", gestures);
    }

    // ========== gestures against GestureTestPage ==========
    // These assert the status label changed, not just that the call returned 200 — the
    // point is that the gesture genuinely reached the app's own recognizer.

    private Task NavigateToGesturePageAsync() => NavigateToPageAsync("//gestures", "PinchTarget");

    [Fact]
    [Trait(TestFramework.Trait, TestFramework.Maui)]
    public async Task Pinch_FiresPinchGestureRecognizer()
    {
        await NavigateToGesturePageAsync();
        var target = await FindElementAsync("PinchTarget");

        var result = await Client.PinchAsync(target.Id, scale: 2.0);

        Assert.True(result.Success, result.Error);
        Assert.Equal("recognizer", result.HandledBy);

        await SettleAsync();
        var status = await FindElementAsync("PinchStatusLabel");
        // The page accumulates e.Scale, so a 2x request lands at 2.00 however many steps it took.
        Assert.Contains("scale=2.00", status.Text);
    }

    [Fact]
    [Trait(TestFramework.Trait, TestFramework.Maui)]
    public async Task Pinch_ZoomOut_ReportsScaleBelowOne()
    {
        await NavigateToGesturePageAsync();
        var target = await FindElementAsync("PinchTarget");

        var result = await Client.PinchAsync(target.Id, scale: 0.5);

        Assert.True(result.Success, result.Error);

        await SettleAsync();
        var status = await FindElementAsync("PinchStatusLabel");
        Assert.Contains("scale=0.50", status.Text);
    }

    [Fact]
    [Trait(TestFramework.Trait, TestFramework.Maui)]
    public async Task Pan_FiresPanGestureRecognizerWithCumulativeTotals()
    {
        await NavigateToGesturePageAsync();
        var target = await FindElementAsync("PanTarget");

        var result = await Client.PanAsync(target.Id, deltaX: 0, deltaY: -150);

        Assert.True(result.Success, result.Error);
        Assert.Equal("recognizer", result.HandledBy);

        await SettleAsync();
        var status = await FindElementAsync("PanStatusLabel");
        Assert.Contains("dy=-150", status.Text);
    }

    [Theory]
    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [InlineData("up")]
    [InlineData("down")]
    [InlineData("left")]
    [InlineData("right")]
    public async Task Swipe_FiresSwipeGestureRecognizer(string direction)
    {
        await NavigateToGesturePageAsync();
        var target = await FindElementAsync("SwipeTarget");

        var result = await Client.GestureDetailedAsync("swipe", target.Id, direction);

        Assert.True(result.Success, result.Error);
        Assert.Equal("recognizer", result.HandledBy);

        await SettleAsync();
        var status = await FindElementAsync("SwipeStatusLabel");
        Assert.Equal($"swipe: {direction}", status.Text);
    }

    // ========== raw-touch tier: views that take their touches directly ==========
    // A GraphicsView overrides the platform touch methods and installs no gesture recognizer,
    // so nothing above can service it. Android delivers real MotionEvents; iOS and Mac Catalyst
    // synthesise UITouches, which the sample opts into with AgentOptions.EnableSyntheticTouch,
    // and only to an allow-list of views. The status label reports the whole sequence, so these
    // assert what the app actually received rather than an HTTP 200.

    private bool SupportsRawTouchInjection =>
        Platform.Equals("android", StringComparison.OrdinalIgnoreCase)
        || Platform.Equals("ios", StringComparison.OrdinalIgnoreCase)
        || Platform.Equals("maccatalyst", StringComparison.OrdinalIgnoreCase);

    private async Task<ElementInfo> FindRawTouchTargetAsync()
    {
        await NavigateToGesturePageAsync();
        // MakeVisible can leave an edge against the viewport clip; both pinch fingers
        // need to hit the canvas, not just its centre.
        Assert.True(await Client.ScrollAsync("RawTouchTarget", animated: false, scrollToPosition: "center"));
        await SettleAsync();
        return await WaitForRawTouchViewportAsync();
    }

    private async Task<ElementInfo> WaitForRawTouchViewportAsync()
    {
        ElementInfo? target = null;
        await WaitForAsync(async () =>
        {
            target = await FindElementAsync("RawTouchTarget");
            var viewport = await FindElementAsync("GestureScrollView");
            var bounds = target.WindowBounds;
            var visible = viewport.WindowBounds;
            if (bounds == null || visible == null || bounds.Width <= 4 || bounds.Height <= 4
                || bounds.X < visible.X + 4 || bounds.Y < visible.Y + 4
                || bounds.X + bounds.Width > visible.X + visible.Width - 4
                || bounds.Y + bounds.Height > visible.Y + visible.Height - 4)
                return false;

            foreach (var x in new[] { bounds.X + 2, bounds.X + bounds.Width - 2 })
            foreach (var y in new[] { bounds.Y + 2, bounds.Y + bounds.Height - 2 })
            {
                using var hit = JsonDocument.Parse(await Client.HitTestAsync(x, y));
                var elements = hit.RootElement.GetProperty("elements");
                if (elements.GetArrayLength() == 0 || elements[0].GetProperty("id").GetString() != target.Id)
                    return false;
            }

            Output.WriteLine($"Raw-touch canvas ({bounds.X}, {bounds.Y}, {bounds.Width}, {bounds.Height}) " +
                $"is inside viewport ({visible.X}, {visible.Y}, {visible.Width}, {visible.Height}); all four corners hit {target.Id}.");
            return true;
        });
        return Assert.IsType<ElementInfo>(target);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Pinch_OnRawTouchView_ArrivesAsTwoTouches()
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Raw-touch injection is not available on {Platform}.");
            return;
        }

        var target = await FindRawTouchTargetAsync();

        var result = await Client.PinchAsync(target.Id, scale: 0.5);

        Assert.True(result.Success, result.Error);
        Assert.Equal("native", result.HandledBy);

        await SettleAsync();
        var status = await FindElementAsync("RawTouchStatusLabel");
        // Two simultaneous pointers is the whole point: a single-pointer approximation
        // cannot produce a scale at all.
        Assert.Contains("maxtouches=2", status.Text);
        Assert.Contains("scale=0.50", status.Text);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Pan_OnRawTouchView_DeliversTheWholeDrag()
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Raw-touch injection is not available on {Platform}.");
            return;
        }

        var target = await FindRawTouchTargetAsync();

        var result = await Client.PanAsync(target.Id, deltaX: 80, deltaY: 0);

        Assert.True(result.Success, result.Error);
        Assert.Equal("native", result.HandledBy);

        await SettleAsync();
        var status = await FindElementAsync("RawTouchStatusLabel");
        Assert.Contains("dx=80", status.Text);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Pan_OnRawTouchViewInsideAScrollView_IsNotStolenByTheScrollView()
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Raw-touch injection is not available on {Platform}.");
            return;
        }

        var target = await FindRawTouchTargetAsync();

        // Vertical, inside a vertical ScrollView: the case where the enclosing scroller
        // takes the gesture away from the element the caller actually named.
        var result = await Client.PanAsync(target.Id, deltaX: 0, deltaY: 60);

        Assert.True(result.Success, result.Error);

        await SettleAsync();
        var status = await FindElementAsync("RawTouchStatusLabel");
        Assert.Contains("dy=60", status.Text);
    }

    private static int ReadRawTouchDx(string? statusText)
    {
        var match = System.Text.RegularExpressions.Regex.Match(statusText ?? "", @"dx=(-?\d+)");
        Assert.True(match.Success, $"No dx in '{statusText}'");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Swipe_OnRawTouchView_ArrivesAsADrag()
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Raw-touch injection is not available on {Platform}.");
            return;
        }

        var target = await FindRawTouchTargetAsync();

        var result = await Client.GestureDetailedAsync("swipe", target.Id, "right");

        Assert.True(result.Success, result.Error);
        Assert.Equal("native", result.HandledBy);

        await SettleAsync();
        var status = await FindElementAsync("RawTouchStatusLabel");
        Assert.True(ReadRawTouchDx(status.Text) > 0, status.Text);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Swipe_OnRawTouchView_WithNativeAncestorSwipe_PrefersTheChild()
    {
        if (!SupportsSyntheticTouch)
        {
            Output.WriteLine($"Native UIKit swipe recognizers are not available on {Platform}.");
            return;
        }

        var target = await FindRawTouchTargetAsync();
        var before = await FindElementAsync("NativeSwipeStatusLabel");
        Assert.StartsWith("native swipe: count=", before.Text);

        var result = await Client.GestureDetailedAsync("swipe", target.Id, "right");

        Assert.True(result.Success, result.Error);
        Assert.Equal("native", result.HandledBy);
        Assert.Contains("Synthetic UITouch", result.Detail);

        await SettleAsync();
        var status = await FindElementAsync("RawTouchStatusLabel");
        Assert.True(ReadRawTouchDx(status.Text) > 0, status.Text);
        var after = await FindElementAsync("NativeSwipeStatusLabel");
        Assert.Equal(before.Text, after.Text);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Swipe_OnViewWithoutLocalHandling_FallsBackToNativeAncestorSwipe()
    {
        if (!SupportsSyntheticTouch)
        {
            Output.WriteLine($"Native UIKit swipe recognizers are not available on {Platform}.");
            return;
        }

        await FindRawTouchTargetAsync();
        var target = await FindElementAsync("NativeSwipeProbeTarget");
        var before = await FindElementAsync("NativeSwipeStatusLabel");
        Assert.StartsWith("native swipe: count=", before.Text);

        var result = await Client.GestureDetailedAsync("swipe", target.Id, "right");

        Assert.True(result.Success, result.Error);
        Assert.Equal("native", result.HandledBy);
        Assert.Equal("UISwipeGestureRecognizer right", result.Detail);

        await SettleAsync();
        var after = await FindElementAsync("NativeSwipeStatusLabel");
        Assert.NotEqual(before.Text, after.Text);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Pan_OnRawTouchView_LongerThanTheView_IsShortenedToStayOnIt()
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Raw-touch injection is not available on {Platform}.");
            return;
        }

        var target = await FindRawTouchTargetAsync();

        // Started centred and offset by half the delta, a drag this long would begin far off
        // the canvas and land on whatever sits beside it.
        var result = await Client.PanAsync(target.Id, deltaX: 5000, deltaY: 0);

        Assert.True(result.Success, result.Error);
        Assert.Equal("native", result.HandledBy);

        await SettleAsync();
        var status = await FindElementAsync("RawTouchStatusLabel");
        Assert.InRange(ReadRawTouchDx(status.Text), 1, 4999);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    public async Task Pinch_OnRawTouchView_FromAnEdgeOrigin_KeepsBothFingersOnTheView(double originX, double originY)
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Raw-touch injection is not available on {Platform}.");
            return;
        }

        var target = await FindRawTouchTargetAsync();

        var result = await Client.PinchAsync(target.Id, scale: 0.5, originX: originX, originY: originY);

        Assert.True(result.Success, result.Error);
        Assert.Equal("native", result.HandledBy);

        await SettleAsync();
        var status = await FindElementAsync("RawTouchStatusLabel");
        Assert.Contains("maxtouches=2", status.Text);
        Assert.Contains("scale=0.50", status.Text);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Pinch_OnRawTouchView_WithClippedBottomEdge_IsRefusedBeforeTouchCallbacks()
    {
        if (!SupportsSyntheticTouch)
            return;

        var target = await FindRawTouchTargetAsync();
        var viewport = await FindElementAsync("GestureScrollView");
        Assert.True(await Client.ScrollAsync(target.Id, animated: false, scrollToPosition: "end"));
        Assert.True(await Client.ScrollAsync(viewport.Id, deltaY: -16, animated: false));
        await WaitForAsync(async () =>
        {
            target = await FindElementAsync("RawTouchTarget");
            viewport = await FindElementAsync("GestureScrollView");
            var bounds = Assert.IsType<BoundsInfo>(target.WindowBounds);
            var visible = Assert.IsType<BoundsInfo>(viewport.WindowBounds);
            return bounds.Y + bounds.Height > visible.Y + visible.Height + 8
                && bounds.Y + bounds.Height / 2 < visible.Y + visible.Height;
        });

        const string awaitingTouch = "rawtouch: awaiting edge pinch";
        Assert.True(await Client.SetPropertyAsync("RawTouchStatusLabel", "Text", awaitingTouch));
        var result = await Client.PinchAsync(target.Id, scale: 0.5, originX: 1, originY: 1);
        Assert.False(result.Success);
        Assert.Equal("none", result.HandledBy);
        Assert.Contains("No pinch handler", result.Error);
        Assert.Equal(awaitingTouch, (await FindElementAsync("RawTouchStatusLabel")).Text);

        Assert.True(await Client.ScrollAsync(target.Id, animated: false, scrollToPosition: "center"));
        target = await WaitForRawTouchViewportAsync();
        result = await Client.PinchAsync(target.Id, scale: 0.5, originX: 1, originY: 1);
        Assert.True(result.Success, result.Error);
        Assert.Equal("native", result.HandledBy);
        var status = await FindElementAsync("RawTouchStatusLabel");
        Assert.Contains("ended maxtouches=2", status.Text);
        Assert.Contains("scale=0.50", status.Text);
    }

    // UIKit-only: Android delivers real touches to the named view, which behave exactly as a
    // finger would — including pressing a button that is dragged across.
    private bool SupportsSyntheticTouch =>
        Platform.Equals("ios", StringComparison.OrdinalIgnoreCase)
        || Platform.Equals("maccatalyst", StringComparison.OrdinalIgnoreCase);

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Pan_OnAnOrdinaryButton_NeverDeliversSyntheticTouches()
    {
        if (!SupportsSyntheticTouch)
        {
            Output.WriteLine($"Synthetic touch is UIKit-only; skipping on {Platform}.");
            return;
        }

        await NavigateToGesturePageAsync();
        await Client.ScrollAsync("SyntheticTouchGuardButton");
        await SettleAsync();
        var button = await FindElementAsync("SyntheticTouchGuardButton");

        // UIButton overrides touchesBegan:, which is exactly what used to qualify a view for
        // synthesised touches. Vertical, so no enclosing horizontal pan (Shell's flyout) is involved.
        await Client.PanAsync(button.Id, deltaX: 0, deltaY: 10);

        // Whether another tier claims the pan is beside the point: the button must not be pressed.
        await SettleAsync();
        var status = await FindElementAsync("GuardStatusLabel");
        Assert.Equal("guard: pressed=0 clicked=0", status.Text);
    }

    [Fact]
    [Trait(TestFramework.Trait, TestFramework.Maui)]
    public async Task DoubleTap_FiresTwoTapRecognizerNotTheSingleTapOne()
    {
        await NavigateToGesturePageAsync();
        var target = await FindElementAsync("DoubleTapTarget");

        var result = await Client.GestureDetailedAsync("doubletap", target.Id);

        Assert.True(result.Success, result.Error);
        Assert.Equal("recognizer", result.HandledBy);

        await SettleAsync();
        var status = await FindElementAsync("TapStatusLabel");
        Assert.Equal("tap: double", status.Text);
    }

    [Fact]
    public async Task Tap_Gesture_UsesGestureEnvelopeAndReachesTheApp()
    {
        await NavigateToPageAsync("//interactions", "TestButton");
        var target = await FindElementAsync("TestButton");

        if (TestFramework.IsNative)
        {
            var result = await PostJsonAsync("/api/v1/ui/actions/gesture", new { type = "tap", elementId = target.Id });
            Assert.True(result.GetProperty("success").GetBoolean());
            Assert.Equal("Gesture performed", result.GetProperty("message").GetString());
        }
        else
        {
            var result = await Client.GestureDetailedAsync(
                "tap",
                target.Id,
                captureEpoch: target.CaptureEpoch,
                registryGeneration: target.RegistryGeneration);

            Assert.True(result.Success, result.Error);
            Assert.Equal("tap", result.Type);
            Assert.Equal("action", result.HandledBy);
        }

        await SettleAsync();
        var status = await FindElementAsync("StatusLabel");
        Assert.Contains("button", status.Text!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tap_Gesture_WithoutTarget_IsRejectedConsistently()
    {
        using var response = await PostRawAsync("/api/v1/ui/actions/gesture", new { type = "tap" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(TestFramework.IsNative ? "elementId is required to tap" : "tap requires elementId", body);
    }

    // ========== tap at a point: content drawn on a canvas ==========
    // SkiaSharp and Syncfusion hit-test what they drew from the touch location, so the drawn
    // items have no element id and only a tap at a point reaches them. Android delivers real
    // MotionEvents; iOS and Mac Catalyst feed a synthesised UITouch to the canvas's recognizers.
    // Each status label names the drawn item the app resolved, so these assert the tap landed
    // where it was aimed — not only that it was accepted.

    private async Task<ElementInfo> FindCanvasTargetAsync(string automationId)
    {
        await NavigateToPageAsync("//canvastaps", "SkiaCanvas");
        await Client.ScrollAsync(automationId);
        await SettleAsync();
        return await FindElementAsync(automationId);
    }

    private async Task<ActionResult> TapAtFractionAsync(ElementInfo target, double fx, double fy)
    {
        var bounds = target.Bounds ?? throw new InvalidOperationException($"{target.Id} has no bounds");
        return await Client.TapResultAsync(
            target.Id,
            bounds.Width * fx,
            bounds.Height * fy,
            target.CaptureEpoch,
            target.RegistryGeneration);
    }

    [Theory]
    [InlineData(0.8, "right")]
    [InlineData(0.2, "left")]
    public async Task TapAtPoint_OnSkiaCanvas_ReachesTheDrawnHalfAtThatPoint(double fx, string expected)
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Touch injection is not available on {Platform}.");
            return;
        }

        var canvas = await FindCanvasTargetAsync("SkiaCanvas");

        var result = await TapAtFractionAsync(canvas, fx, 0.5);

        Assert.True(result.Success, result.Error);
        await SettleAsync();
        var status = await FindElementAsync("SkiaStatusLabel");
        // The canvas reports the location it was handed; it must be the one asked for.
        var expectedX = canvas.Bounds!.Width * fx;
        var match = System.Text.RegularExpressions.Regex.Match(status.Text ?? "", @"^skia: (\w+) at \((\d+), (\d+)\)");
        Assert.True(match.Success, status.Text);
        Assert.Equal(expected, match.Groups[1].Value);
        Assert.InRange(double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), expectedX - 1, expectedX + 1);
        Assert.InRange(double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture), canvas.Bounds.Height / 2 - 1, canvas.Bounds.Height / 2 + 1);
    }

    [Fact]
    public async Task TapAtPoint_OnSyncfusionChart_SelectsEachColumnItIsAimedAt()
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Touch injection is not available on {Platform}.");
            return;
        }

        // Alternating columns is the point: a double-tap recognizer left holding the previous
        // tap would complete on the next one and select the previous column instead.
        (double X, double Y, string Column)[] columns = [(0.54, 0.6, "B"), (0.85, 0.65, "C"), (0.24, 0.78, "A"), (0.85, 0.65, "C")];
        foreach (var (fx, fy, column) in columns)
        {
            var chart = await FindCanvasTargetAsync("SalesChart");

            var result = await TapAtFractionAsync(chart, fx, fy);

            Assert.True(result.Success, result.Error);
            await SettleAsync();
            var status = await FindElementAsync("ChartStatusLabel");
            Assert.StartsWith($"chart: selected {column} ", status.Text);
        }
    }

    [Fact]
    public async Task TapAtPoint_OutsideTheElement_IsRejectedWithoutTapping()
    {
        var canvas = await FindCanvasTargetAsync("SkiaCanvas");
        var before = (await FindElementAsync("SkiaStatusLabel")).Text;

        var result = await Client.TapResultAsync(
            canvas.Id, canvas.Bounds!.Width + 10, 5, canvas.CaptureEpoch, canvas.RegistryGeneration);

        Assert.False(result.Success);
        Assert.Contains("outside the element", result.Error);
        await SettleAsync();
        var status = await FindElementAsync("SkiaStatusLabel");
        Assert.Equal(before, status.Text);
    }

    [Fact]
    public async Task TapAtPoint_WithOnlyOneCoordinate_IsRejected()
    {
        var canvas = await FindCanvasTargetAsync("SkiaCanvas");

        using var response = await PostRawAsync("/api/v1/ui/actions/tap", new { elementId = canvas.Id, x = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("x and y must be given together", await response.Content.ReadAsStringAsync());
    }

    // ========== drag from a point: real touches along a path ==========
    // The Drag and Drop page's slider and card move only for what MAUI's pan and tap
    // recognizers receive from the platform, and each status label counts it. A drag driven
    // through the recognizer directly would also have passed — these check the touch pipeline:
    // the thumb lands where the finger stopped, and the tap on the same layout stays silent.

    private async Task<ElementInfo> OpenDragPageAsync(string automationId)
    {
        await NavigateToPageAsync("//dragdrop", "DragSliderTrack");
        // The counters are what every drag test asserts, so they start from zero. The page can
        // still be settling from the navigation, so a reset tap that misses is retried.
        await WaitForAsync(async () =>
        {
            var reset = await FindElementAsync("DragResetButton");
            return await Client.TapAsync(reset.Id, reset.CaptureEpoch, reset.RegistryGeneration)
                && (await FindElementAsync("DropStatus")).Text == "drop: none drops=0";
        });
        await Client.ScrollAsync(automationId);
        await SettleAsync();
        return await FindElementAsync(automationId);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task DragFromPoint_OnACustomSlider_MovesTheThumbWithoutFiringItsTap()
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Touch injection is not available on {Platform}.");
            return;
        }

        var track = await OpenDragPageAsync("DragSliderTrack");
        var bounds = track.Bounds!;
        const double thumb = 24;
        var travel = bounds.Width - thumb;

        // From the thumb at 0 to the middle of the track.
        var result = await Client.DragAsync(
            track.Id, thumb / 2, bounds.Height / 2, toX: thumb / 2 + travel / 2, toY: bounds.Height / 2,
            durationMs: 400, steps: 20, captureEpoch: track.CaptureEpoch, registryGeneration: track.RegistryGeneration);

        Assert.True(result.Success, result.Error);
        Assert.Equal("drag", result.Type);
        Assert.Equal("native", result.HandledBy);
        await SettleAsync();
        var status = (await FindElementAsync("DragSliderStatus")).Text ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(
            status, @"^slider: value=(\d+) pans=(\d+) taps=(\d+) commits=(\d+)$");
        Assert.True(match.Success, status);
        // UIKit starts a pan's translation after its 10pt slop, so iOS lands a little short.
        Assert.InRange(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), 44, 52);
        Assert.Equal("1", match.Groups[2].Value);
        Assert.Equal("0", match.Groups[3].Value);
        Assert.Equal("1", match.Groups[4].Value);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task DragFromPoint_OntoADropTarget_IsReleasedOverThatTarget()
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Touch injection is not available on {Platform}.");
            return;
        }

        await OpenDragPageAsync("DragCard");
        // Both ids from one capture: a drop target is validated against the dragged element's.
        var both = await Client.QueryCssAsync("#DragCard, #DropZone");
        var card = Assert.Single(both, e => e.AutomationId == "DragCard");
        var zone = Assert.Single(both, e => e.AutomationId == "DropZone");

        var result = await Client.DragAsync(
            card.Id, card.Bounds!.Width / 2, card.Bounds.Height / 2, toElementId: zone.Id,
            durationMs: 500, steps: 20, captureEpoch: card.CaptureEpoch, registryGeneration: card.RegistryGeneration);

        Assert.True(result.Success, result.Error);
        await SettleAsync();
        Assert.Equal("drop: DropZone drops=1", (await FindElementAsync("DropStatus")).Text);

        // And one released short of the zone is reported outside it.
        card = await FindElementAsync("DragCard");
        result = await Client.DragAsync(
            card.Id, card.Bounds!.Width / 2, card.Bounds.Height / 2,
            toX: card.Bounds.Width / 2 + 40, toY: card.Bounds.Height / 2,
            captureEpoch: card.CaptureEpoch, registryGeneration: card.RegistryGeneration);

        Assert.True(result.Success, result.Error);
        await SettleAsync();
        Assert.Equal("drop: outside drops=2", (await FindElementAsync("DropStatus")).Text);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task DragFromPoint_InACaptureBoundBatch_DropsOntoTheTarget()
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Touch injection is not available on {Platform}.");
            return;
        }

        await OpenDragPageAsync("DragCard");
        var both = await Client.QueryCssAsync("#DragCard, #DropZone");
        var card = Assert.Single(both, e => e.AutomationId == "DragCard");
        var zone = Assert.Single(both, e => e.AutomationId == "DropZone");

        using var response = await PostRawAsync("/api/v1/ui/actions/batch", new
        {
            actions = new object[]
            {
                new { action = "gesture", type = "drag", elementId = card.Id, x = 30, y = 30, toElementId = zone.Id, durationMs = 500, steps = 20 },
            },
            captureEpoch = card.CaptureEpoch,
            registryGeneration = card.RegistryGeneration,
        });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await SettleAsync();
        Assert.Equal("drop: DropZone drops=1", (await FindElementAsync("DropStatus")).Text);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task DragFromPoint_OntoATargetFromAnotherCapture_IsRejectedAsStale()
    {
        var card = await OpenDragPageAsync("DragCard");
        var zone = await FindElementAsync("DropZone");
        Assert.NotEqual(card.CaptureEpoch, zone.CaptureEpoch);

        var result = await Client.DragAsync(
            card.Id, 10, 10, toElementId: zone.Id,
            captureEpoch: card.CaptureEpoch, registryGeneration: card.RegistryGeneration);

        Assert.False(result.Success);
        Assert.Equal("drop: none drops=0", (await FindElementAsync("DropStatus")).Text);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task DragFromPoint_OnRawTouchView_DeliversTheWholeDrag()
    {
        if (!SupportsRawTouchInjection)
        {
            Output.WriteLine($"Raw-touch injection is not available on {Platform}.");
            return;
        }

        var target = await FindRawTouchTargetAsync();
        var bounds = target.Bounds!;

        var result = await Client.DragAsync(
            target.Id, bounds.Width / 4, bounds.Height / 2, toX: bounds.Width / 4 + 80, toY: bounds.Height / 2,
            captureEpoch: target.CaptureEpoch, registryGeneration: target.RegistryGeneration);

        Assert.True(result.Success, result.Error);
        await SettleAsync();
        Assert.Contains("dx=80", (await FindElementAsync("RawTouchStatusLabel")).Text);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task DragFromPoint_StartingOutsideTheElement_IsRejectedWithoutTouching()
    {
        var track = await OpenDragPageAsync("DragSliderTrack");
        var before = (await FindElementAsync("DragSliderStatus")).Text;

        var result = await Client.DragAsync(
            track.Id, track.Bounds!.Width + 10, 5, toX: 10, toY: 5,
            captureEpoch: track.CaptureEpoch, registryGeneration: track.RegistryGeneration);

        Assert.False(result.Success);
        Assert.Contains("outside the element", result.Error);
        await SettleAsync();
        Assert.Equal(before, (await FindElementAsync("DragSliderStatus")).Text);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task DragFromPoint_WithNoEnd_IsRejected()
    {
        var track = await OpenDragPageAsync("DragSliderTrack");

        using var response = await PostRawAsync(
            "/api/v1/ui/actions/gesture", new { type = "drag", elementId = track.Id, x = 10, y = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("needs an end", await response.Content.ReadAsStringAsync());
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task LongPress_OnAndroid_HoldsTheNativeTouchForAtLeastThePlatformThreshold()
    {
        if (!Platform.Equals("android", StringComparison.OrdinalIgnoreCase))
        {
            Output.WriteLine("Native long-press timing is Android-specific.");
            return;
        }

        await NavigateToGesturePageAsync();
        var target = await FindElementAsync("LongPressTarget");

        var result = await Client.GestureDetailedAsync("longpress", target.Id);

        Assert.True(result.Success, result.Error);
        Assert.Equal("native", result.HandledBy);

        await SettleAsync();
        var status = await FindElementAsync("LongPressStatusLabel");
        var elapsedText = status.Text?["longpress: ".Length..].TrimEnd('m', 's');
        Assert.True(
            double.TryParse(elapsedText, NumberStyles.Number, CultureInfo.InvariantCulture, out var elapsedMs)
                && elapsedMs >= 500,
            $"Expected a native long press of at least 500ms, got '{status.Text}'.");
    }

    [Fact]
    [Trait(TestFramework.Trait, TestFramework.Maui)]
    public async Task Pinch_OnElementWithoutRecognizer_ReportsWhichTierRan()
    {
        await NavigateToGesturePageAsync();
        var target = await FindElementAsync("NativeScrollTarget");

        var result = await Client.PinchAsync(target.Id, scale: 2.0);
        Output.WriteLine($"pinch on NativeScrollTarget: handledBy={result.HandledBy} detail={result.Detail} error={result.Error}");

        // A plain ScrollView has no PinchGestureRecognizer, so this must never claim
        // the app's own recognizer handled it — either native injection ran, or nothing did.
        Assert.NotEqual("recognizer", result.HandledBy);

        if (result.Success)
        {
            Assert.Equal("native", result.HandledBy);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Error), "A failed gesture must explain why.");
        }
    }

    [Fact]
    [Trait(TestFramework.Trait, TestFramework.Maui)]
    public async Task Pan_OnElementWithoutRecognizer_FallsThroughToNative()
    {
        await NavigateToGesturePageAsync();
        var target = await FindElementAsync("NativeScrollTarget");
        var beforeText = await Client.GetPropertyAsync(target.Id, "ScrollY");
        Assert.True(
            double.TryParse(beforeText, NumberStyles.Number, CultureInfo.InvariantCulture, out var before),
            $"Could not read the initial ScrollY value: '{beforeText}'.");

        var result = await Client.PanAsync(
            target.Id,
            deltaX: 0,
            deltaY: -120,
            captureEpoch: target.CaptureEpoch,
            registryGeneration: target.RegistryGeneration);
        Output.WriteLine($"pan on NativeScrollTarget: handledBy={result.HandledBy} detail={result.Detail} error={result.Error}");

        Assert.NotEqual("recognizer", result.HandledBy);

        if (result.Success)
        {
            Assert.Equal("native", result.HandledBy);

            await SettleAsync();
            var afterText = await Client.GetPropertyAsync(target.Id, "ScrollY");
            Assert.True(
                double.TryParse(afterText, NumberStyles.Number, CultureInfo.InvariantCulture, out var after),
                $"Could not read ScrollY after the native pan: '{afterText}'.");
            Assert.True(after > before, $"Expected native pan to increase ScrollY; before={before}, after={after}.");

            if (Platform == "android")
                Assert.Contains("MotionEvent", result.Detail);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Error), "A failed gesture must explain why.");
        }
    }

    [Fact]
    public async Task Fill_MultipleEntries_SetsAllText()
    {
        await NavigateToMainPageAsync();
        var titleEntry = await FindElementAsync("NewTodoEntry");
        var descEntry = await FindElementAsync("NewDescriptionEntry");

        await Client.FillAsync(titleEntry.Id, "Title text");
        await Client.FillAsync(descEntry.Id, "Description text");
        await SettleAsync();

        var updatedTitle = await FindElementAsync("NewTodoEntry");
        var updatedDesc = await FindElementAsync("NewDescriptionEntry");

        Assert.Equal("Title text", updatedTitle.Text);
        Assert.Equal("Description text", updatedDesc.Text);

        await Client.ClearAsync(titleEntry.Id);
        await Client.ClearAsync(descEntry.Id);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Tap_WindowsNativeAlertButton_DismissesDialog()
    {
        if (!Platform.Equals("windows", StringComparison.OrdinalIgnoreCase))
        {
            Output.WriteLine("Windows native dialog action test skipped on non-Windows platform.");
            return;
        }

        await NavigateToPageAsync("//dialogs", "AlertOkOnlyBtn");

        var trigger = await FindElementAsync("AlertOkOnlyBtn");
        var triggered = await Client.TapAsync(trigger.Id).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(triggered);

        var okButton = await WaitForNativeButtonAsync("OK");
        Assert.True(await Client.TapAsync(okButton.Id));

        await WaitForAsync(async () =>
        {
            var status = await FindElementAsync("DialogStatusLabel");
            return status.Text?.Contains("OK dismissed", StringComparison.OrdinalIgnoreCase) == true;
        }, timeoutMs: 5000);
    }

    async Task CleanupAddedTodoAsync(string todoTitle)
    {
        try
        {
            await SettleAsync();
            var deleteButtons = await Client.QueryAsync(automationId: "DeleteButton");
            if (deleteButtons.Count > 0)
            {
                await Client.TapAsync(deleteButtons[^1].Id);
                await SettleAsync();
            }
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Cleanup warning: Could not delete todo '{todoTitle}': {ex.Message}");
        }
    }

    async Task<ElementInfo> WaitForNativeButtonAsync(string text)
    {
        ElementInfo? match = null;
        await WaitForAsync(async () =>
        {
            var buttons = await Client.QueryAsync(type: ButtonTypeName, text: text);
            match = buttons.FirstOrDefault(e =>
                e.Id.StartsWith("native:", StringComparison.Ordinal) &&
                string.Equals(e.Text, text, StringComparison.OrdinalIgnoreCase));
            return match is not null;
        }, timeoutMs: 5000);

        return match!;
    }
}
