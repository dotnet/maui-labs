using System.Runtime.CompilerServices;
using AIExtensions.Sample.ChatPlayground.Services;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

#pragma warning disable MEAI001 // Image generation tools and middleware are experimental in the installed SDK.
public sealed class ImageGenerationPipelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImageTool_GeneratesInlineImageWithoutHidingUserImages(bool streaming)
    {
        var image = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "playground_sample.png"));
        var provider = new TestChatClient();
        var generator = new TestImageGenerator(image);
        using var client = provider.AsBuilder()
            .UseImageGenerationPreservingInputs(generator)
            .UseFunctionInvocation()
            .Build();

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
        Assert.Equal(1, generator.CallCount);
        var output = Assert.Single(contents.OfType<ImageGenerationToolResultContent>());
        Assert.Equal(image, Assert.Single(output.Outputs!.OfType<DataContent>()).Data.ToArray());

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
#pragma warning restore MEAI001
