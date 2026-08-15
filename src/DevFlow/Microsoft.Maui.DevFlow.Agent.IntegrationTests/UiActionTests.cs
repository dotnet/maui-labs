using System.Net;
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
        var response = await PostRawAsync("/api/v1/ui/actions/gesture", new { type = "wiggle" });
        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Capabilities_AdvertisesSupportedGestures()
    {
        var caps = await GetJsonAsync("/api/v1/agent/capabilities");
        var gestures = caps.GetProperty("ui").GetProperty("gestures")
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

    [Fact]
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
    public async Task Pinch_OnElementWithoutRecognizer_ReportsWhichTierRan()
    {
        await NavigateToGesturePageAsync();
        var target = await FindElementAsync("NativeScrollTarget");

        var result = await Client.PinchAsync(target.Id, scale: 2.0);
        Output.WriteLine($"pinch on NativeScrollTarget: handledBy={result.HandledBy} detail={result.Detail} error={result.Error}");

        // A plain ScrollView has no PinchGestureRecognizer, so this must never claim
        // the app's own recognizer handled it — either native injection ran, or nothing did.
        Assert.NotEqual("recognizer", result.HandledBy);

        // Android synthesises real multi-pointer MotionEvents, so it must always reach
        // the native tier. Other platforms only zoom surfaces that opt in, so they may decline.
        if (Platform == "android")
        {
            Assert.True(result.Success, result.Error);
            Assert.Equal("native", result.HandledBy);
            Assert.Contains("MotionEvent", result.Detail);
        }
        else if (result.Success)
        {
            Assert.Equal("native", result.HandledBy);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Error), "A failed gesture must explain why.");
        }
    }

    [Fact]
    public async Task Pan_OnElementWithoutRecognizer_FallsThroughToNative()
    {
        await NavigateToGesturePageAsync();
        var target = await FindElementAsync("NativeScrollTarget");

        var result = await Client.PanAsync(target.Id, deltaX: 0, deltaY: -120);
        Output.WriteLine($"pan on NativeScrollTarget: handledBy={result.HandledBy} detail={result.Detail} error={result.Error}");

        Assert.NotEqual("recognizer", result.HandledBy);

        if (Platform == "android")
        {
            Assert.True(result.Success, result.Error);
            Assert.Equal("native", result.HandledBy);
            Assert.Contains("MotionEvent", result.Detail);
        }
        else if (!result.Success)
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
