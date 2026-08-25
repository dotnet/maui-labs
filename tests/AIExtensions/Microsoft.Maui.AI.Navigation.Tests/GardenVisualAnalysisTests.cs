using System.Runtime.CompilerServices;
using AIExtensions.Sample.Garden.Models;
using AIExtensions.Sample.Garden.ViewModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Indexer;
using Microsoft.Maui.AI.Wayfinding;

namespace Microsoft.Maui.AI.Navigation.Tests;

public class GardenOrderInsightsTests
{
    [Fact]
    public void Create_Orders_AggregatesSpendingAndProductQuantities()
    {
        var basil = new Product(
            "seed-basil",
            "Sweet Basil Seeds",
            "Seeds",
            2.50m,
            "");
        var trowel = new Product(
            "tool-trowel",
            "Hand Trowel",
            "Tools",
            10m,
            "");
        var orders = new[]
        {
            new Order(
                "A",
                DateTime.Today,
                [new ListItem(basil, 4), new ListItem(trowel, 1)]),
            new Order(
                "B",
                DateTime.Today.AddDays(-1),
                [new ListItem(basil, 2)]),
        };

        var result = OrderInsightsSnapshot.Create(orders);

        Assert.Equal(
            [new OrderInsightValue("Seeds", 15m), new OrderInsightValue("Tools", 10m)],
            result.SpendingByCategory);
        Assert.Equal(
            [new OrderInsightValue("Sweet Basil Seeds", 6m), new OrderInsightValue("Hand Trowel", 1m)],
            result.PopularProducts);
    }

    [Fact]
    public async Task DescribeAsync_CapturedPng_SendsImageAndQuestion()
    {
        byte[] png = [1, 2, 3, 4];
        var capture = new StubCaptureService(png);
        var chat = new RecordingChatClient("Two bar charts are visible.");
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>(
            MauiWayfindingOptions.VisionChatClientServiceKey,
            chat);
        using var provider = services.BuildServiceProvider();
        var service = new MauiVisualAnalysisService(
            provider,
            capture,
            new StubCurrentPageContext("OrderInsightsCharts"),
            new MauiWayfindingOptions());

        var result = await service.DescribeAsync(
            ["OrderInsightsCharts"],
            "Which item is largest?");

        Assert.Equal("Two bar charts are visible.", result);
        Assert.Equal(["OrderInsightsCharts"], capture.TargetAutomationIds);
        var userMessage = Assert.Single(
            chat.Messages!,
            message => message.Role == ChatRole.User);
        var image = Assert.Single(userMessage.Contents.OfType<DataContent>());
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal("visual-1.png", image.Name);
        Assert.Equal(png, image.Data.ToArray());
        Assert.Contains(
            "Which item is largest?",
            Assert.Single(userMessage.Contents.OfType<TextContent>()).Text);
    }

    [Fact]
    public async Task UseMauiVision_AddsIndependentVisualTool()
    {
        var services = new ServiceCollection();
        services.AddMauiWayfinding(
            new TestCatalog([]),
            options => options.EnableVision = true);
        using var provider = services.BuildServiceProvider();
        var inner = new RecordingChatClient("ok");
        var client = new ChatClientBuilder(inner)
            .UseMauiWayfinding()
            .Build(provider);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Describe this chart")]);

        Assert.Contains(
            inner.Options!.Tools!,
            tool => tool.Name == "describe_current_visual");
    }

    [Fact]
    public async Task DescribeAsync_MultipleTargets_SendsEverySelectedVisual()
    {
        var capture = new StubCaptureService([1, 2, 3]);
        var chat = new RecordingChatClient("Both charts are visible.");
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>(
            MauiWayfindingOptions.VisionChatClientServiceKey,
            chat);
        using var provider = services.BuildServiceProvider();
        var service = new MauiVisualAnalysisService(
            provider,
            capture,
            new StubCurrentPageContext("SpendingChart", "ProductsChart"),
            new MauiWayfindingOptions());

        var result = await service.DescribeAsync(
            ["SpendingChart", "ProductsChart"],
            "Compare these charts.");

        Assert.Equal("Both charts are visible.", result);
        Assert.Equal(
            ["SpendingChart", "ProductsChart"],
            capture.TargetAutomationIds);
        var userMessage = Assert.Single(
            chat.Messages!,
            message => message.Role == ChatRole.User);
        Assert.Equal(
            ["visual-1.png", "visual-2.png"],
            userMessage.Contents
                .OfType<DataContent>()
                .Select(content => content.Name!)
                .ToArray());
    }

    [Fact]
    public async Task DescribeAsync_NoTargets_RequiresIndexedAutomationId()
    {
        var capture = new StubCaptureService([1, 2, 3]);
        var chat = new RecordingChatClient("Current page description.");
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>(
            MauiWayfindingOptions.VisionChatClientServiceKey,
            chat);
        using var provider = services.BuildServiceProvider();
        var service = new MauiVisualAnalysisService(
            provider,
            capture,
            new StubCurrentPageContext(),
            new MauiWayfindingOptions());

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DescribeAsync(
                [],
                "What is visible?"));

        Assert.Contains("At least one AutomationId", error.Message);
        Assert.Empty(capture.TargetAutomationIds);
    }

    [Fact]
    public async Task DescribeAsync_UnindexedTarget_FailsBeforeCapture()
    {
        var capture = new StubCaptureService([1, 2, 3]);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>(
            MauiWayfindingOptions.VisionChatClientServiceKey,
            new RecordingChatClient("unused"));
        using var provider = services.BuildServiceProvider();
        var service = new MauiVisualAnalysisService(
            provider,
            capture,
            new StubCurrentPageContext("AdvertisedChart"),
            new MauiWayfindingOptions());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DescribeAsync(
                ["GuessedSensitiveControl"],
                "What is visible?"));

        Assert.Contains("current semantic UI", error.Message);
        Assert.Empty(capture.TargetAutomationIds);
    }

    [Fact]
    public async Task DescribeAsync_TooManyTargets_FailsBeforeCapture()
    {
        var capture = new StubCaptureService([1, 2, 3]);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>(
            MauiWayfindingOptions.VisionChatClientServiceKey,
            new RecordingChatClient("unused"));
        using var provider = services.BuildServiceProvider();
        var service = new MauiVisualAnalysisService(
            provider,
            capture,
            new StubCurrentPageContext("One", "Two"),
            new MauiWayfindingOptions { MaximumVisualTargets = 1 });

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.DescribeAsync(
                ["One", "Two"],
                "What is visible?"));

        Assert.Contains("At most 1", error.Message);
        Assert.Empty(capture.TargetAutomationIds);
    }

    [Fact]
    public async Task DescribeAsync_PayloadTooLarge_FailsBeforeSendingToModel()
    {
        var capture = new StubCaptureService([1, 2, 3]);
        var chat = new RecordingChatClient("unused");
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>(
            MauiWayfindingOptions.VisionChatClientServiceKey,
            chat);
        using var provider = services.BuildServiceProvider();
        var service = new MauiVisualAnalysisService(
            provider,
            capture,
            new StubCurrentPageContext("Chart"),
            new MauiWayfindingOptions { MaximumVisualPayloadBytes = 2 });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DescribeAsync(
                ["Chart"],
                "What is visible?"));

        Assert.Contains("2 byte payload limit", error.Message);
        Assert.Null(chat.Messages);
    }

    private sealed class TestCatalog(IReadOnlyList<IndexedPage> pages)
        : IndexedPageCatalog
    {
        public override IReadOnlyList<IndexedPage> Pages => pages;
    }

    private sealed class StubCaptureService(byte[] png)
        : ICurrentViewCaptureService
    {
        public List<string> TargetAutomationIds { get; } = [];

        public Task<CapturedViewImage> CaptureAsync(
            string? targetAutomationId = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetAutomationId);
            TargetAutomationIds.Add(targetAutomationId);
            return Task.FromResult(new CapturedViewImage(
                png,
                targetAutomationId,
                600,
                220));
        }
    }

    private sealed class StubCurrentPageContext(params string[] automationIds)
        : ICurrentPageContextProvider
    {
        public Task<CurrentPageSnapshot?> CaptureAsync(
            CurrentPageSnapshotOptions? options = null)
            => Task.FromResult<CurrentPageSnapshot?>(
                new CurrentPageSnapshot(
                    "OrdersPage",
                    "# Orders",
                    "Orders",
                    automationIds));
    }

    private sealed class RecordingChatClient(string response) : IChatClient
    {
        public IReadOnlyList<ChatMessage>? Messages { get; private set; }
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            Options = options;
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, response)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }
}
