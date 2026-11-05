using System.Runtime.CompilerServices;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.AI.Chat.Tests;

[Collection("Chat diagnostics")]
public sealed class ImageGenerationPipelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImageTool_GeneratesInlineImageWithoutHidingUserImages(bool streaming)
    {
        var image = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "playground_sample.png"));
        using var diagnostics = new ChatDiagnostics();
        using var factory = LoggerFactory.Create(builder =>
            builder.AddProvider(diagnostics).AddFilter<ChatDiagnostics>("Microsoft.Extensions.AI", LogLevel.Debug));
        var provider = new TestChatClient();
        var imageProvider = new TestImageGenerator(image);
        using var generator = imageProvider.AsBuilder().UseLogging(factory).Build();
        using var client = new ImageGeneratingChatClient(
            provider.AsBuilder().UseFunctionInvocation(factory).Build(),
            generator, ImageGeneratingChatClient.DataContentHandling.GeneratedImages)
            .AsBuilder().UsePlaygroundTelemetry().UseLogging(factory).Build();

        var input = new DataContent(image, "image/png");
        var messages = new[] { new ChatMessage(ChatRole.User, [new TextContent("Generate a robot"), input]) };
        var options = new ChatOptions { Tools = [new HostedImageGenerationTool()] };
        var contents = new List<AIContent>();
        if (streaming)
        {
            await foreach (var update in client.GetStreamingResponseAsync(messages, options))
                contents.AddRange(update.Contents);
        }
        else
        {
            var response = await client.GetResponseAsync(messages, options);
            contents.AddRange(response.Messages.SelectMany(message => message.Contents));
        }

        Assert.Equal(2, provider.CallCount);
        Assert.Contains(provider.ReceivedMessages, message => message.Contents.Contains(input));
        Assert.All(provider.ReceivedTools, tools =>
        {
            Assert.DoesNotContain(tools, tool => tool is HostedImageGenerationTool);
            Assert.Contains(tools, tool => tool.Name == "GenerateImage");
        });
        Assert.Equal(1, imageProvider.CallCount);
        var output = Assert.Single(contents.OfType<ImageGenerationToolResultContent>());
        Assert.Equal(image, Assert.Single(output.Outputs!.OfType<DataContent>()).Data.ToArray());
        var entries = diagnostics.Snapshot().Entries;
        var span = Assert.Single(entries, entry => entry.Heading.Contains("| Telemetry |"));
        Assert.Contains(entries, entry => entry.Heading.Contains("LoggingImageGenerator")
            && entry.Message.Contains("invoked"));
        Assert.Contains(entries, entry => entry.Heading.Contains("LoggingImageGenerator")
            && entry.Message.Contains("completed"));
        Assert.Contains(entries, entry => entry.Heading.Contains("FunctionInvokingChatClient")
            && entry.Message.Contains("Invoking GenerateImage."));
        Assert.Contains(entries, entry => entry.Heading.Contains("FunctionInvokingChatClient")
            && entry.Message.Contains("GenerateImage invocation completed. Duration:"));
        Assert.DoesNotContain(entries, entry => (entry.Message + entry.Details).Contains("a robot"));
        Assert.All(entries, entry => Assert.Equal(span.TraceId, entry.TraceId));

        await client.GetResponseAsync(
            [messages[0], new ChatMessage(ChatRole.Tool, [output]), new ChatMessage(ChatRole.User, "Describe my original image")]);
        Assert.Contains(provider.ReceivedMessages[0].Contents, content => ReferenceEquals(content, input));
        Assert.DoesNotContain(provider.ReceivedMessages[1].Contents, content => content is ImageGenerationToolResultContent);
        Assert.Contains(provider.ReceivedMessages[1].Contents, content => content is TextContent { Text: { } text } &&
            text.Contains("available for edit", StringComparison.Ordinal));
    }

    private sealed class TestChatClient : IChatClient
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<ChatMessage> ReceivedMessages { get; private set; } = [];
        public List<IReadOnlyList<AITool>> ReceivedTools { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            ReceivedMessages = messages.ToList();
            ReceivedTools.Add(options?.Tools?.ToArray() ?? []);
            return Task.FromResult(++CallCount == 1
                ? new ChatResponse([new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("image-call", "GenerateImage", new Dictionary<string, object?> { ["prompt"] = "a robot" })])])
                : new ChatResponse([new ChatMessage(ChatRole.Assistant, "Generated an image.")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var message in response.Messages)
                yield return new ChatResponseUpdate(message.Role, message.Contents);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(IChatClient) ? this : null;

        public void Dispose() { }
    }

    private sealed class TestImageGenerator(byte[] image) : IImageGenerator
    {
        public int CallCount { get; private set; }

        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request, ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ImageGenerationResponse([new DataContent(image, "image/png")]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(IImageGenerator) ? this : null;

        public void Dispose() { }
    }
}
