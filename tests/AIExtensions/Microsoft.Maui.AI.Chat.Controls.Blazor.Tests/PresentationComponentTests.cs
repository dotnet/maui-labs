using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Presentation;

namespace Microsoft.Maui.AI.Chat.Controls.Blazor.Tests;

public class PresentationComponentTests
{
    [Fact]
    public void CopilotChatView_IsAComponent()
    {
        Assert.IsAssignableFrom<Microsoft.AspNetCore.Components.ComponentBase>(
            new CopilotChatView());
    }

    [Fact]
    public async Task MixedContent_ProducesUniqueProjectedRowKeys()
    {
        var paragraph = new ParagraphNode();
        paragraph.AddChild(new TextNode("rich"));
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "mixed",
            Contents =
            [
                new RichTextContent("rich", [paragraph]),
                new TextContent("plain"),
            ],
        };
        var session = CreateSession(update);
        using var presentation = new AgentChatPresentation(session);

        await session.SendMessageAsync("hello");

        var keys = presentation.Messages
            .SelectMany(message => message.Contents.Select(
                content => $"{message.Id}::{content.Id}"))
            .ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task OwnedPresentation_CanBePromotedWithoutBeingDisposed()
    {
        var session = CreateSession(TextUpdate("hello"));
        var component = new TestCopilotChatView();
        component.SetSession(session);
        component.ApplyParameters();
        var presentation = Assert.IsType<AgentChatPresentation>(
            component.EffectivePresentation);

        component.SetPresentation(presentation);
        component.ApplyParameters();
        await session.SendMessageAsync("question");

        Assert.Same(presentation, component.EffectivePresentation);
        Assert.NotEmpty(presentation.Messages);
        component.Dispose();
        presentation.Dispose();
    }

    [Fact]
    public async Task ReplacingOwnedPresentation_DisposesOldProjectionOnly()
    {
        var session = CreateSession(TextUpdate("hello"));
        var component = new TestCopilotChatView();
        component.SetSession(session);
        component.ApplyParameters();
        var oldPresentation = Assert.IsType<AgentChatPresentation>(
            component.EffectivePresentation);
        using var external = new AgentChatPresentation(session);

        component.SetPresentation(external);
        component.ApplyParameters();
        await session.SendMessageAsync("question");

        Assert.Empty(oldPresentation.Messages);
        Assert.NotEmpty(external.Messages);
        component.Dispose();
    }

    [Fact]
    public async Task DisposingComponent_DoesNotDisposeExternalPresentation()
    {
        var session = CreateSession(TextUpdate("hello"));
        using var external = new AgentChatPresentation(session);
        var component = new TestCopilotChatView();
        component.SetPresentation(external);
        component.ApplyParameters();

        component.Dispose();
        await session.SendMessageAsync("question");

        Assert.NotEmpty(external.Messages);
    }

    [Fact]
    public async Task ManualActionRunner_LeavesConversationErrorToAgentContext()
    {
        await AgentActionRunner.InvokeAsync(
            () => Task.FromException(new InvalidOperationException("failure")));
        await AgentActionRunner.InvokeAsync(
            () => Task.FromCanceled(new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task InteractiveButtons_RenderAsBlazorEvents()
    {
        using var content = new AgentBlockContent(
            new ErrorContentBlock("Try again."),
            turn: null,
            isRequest: false);
        var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<AgentContentView>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(AgentContentView.Content)] = content,
                }));
            return output.ToHtmlString();
        });

        Assert.Contains(">Retry</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("@onclick", html, StringComparison.Ordinal);
    }

    private static AgentContext CreateSession(params ChatResponseUpdate[] updates) =>
        new(new UIAgent(new StubChatClient(updates)));

    private static ChatResponseUpdate TextUpdate(string text) =>
        new(ChatRole.Assistant, text) { MessageId = "assistant" };

    private sealed class TestCopilotChatView : CopilotChatView
    {
        public void SetSession(AgentContext? session) => Session = session;

        public void SetPresentation(AgentChatPresentation? presentation) =>
            Presentation = presentation;

        public void ApplyParameters() => base.OnParametersSet();
    }

    private sealed class StubChatClient(
        IReadOnlyList<ChatResponseUpdate> updates) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            await GetStreamingResponseAsync(messages, options, cancellationToken)
                .ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            null;

        public void Dispose()
        {
        }
    }
}
