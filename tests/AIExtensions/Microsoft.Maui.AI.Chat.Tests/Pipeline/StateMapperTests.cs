// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class StateMapperTests
{
    private sealed class RecipeState
    {
        public string Title { get; set; } = string.Empty;
        public string Cuisine { get; set; } = string.Empty;
    }

    [Fact]
    public async Task StateMapper_StateAndVisibleText_UpdatesStateAndFiltersStateContent()
    {
        var client = CreateClient(EmitStateAndText());
        var agent = CreateRecipeAgent(client);
        var changed = 0;
        agent.State.OnChanged(() => changed++);

        var blocks = await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Recipe?")));

        Assert.Equal("Pasta", agent.State.Value.Title);
        Assert.Equal("Italian", agent.State.Value.Cuisine);
        Assert.Equal(1, changed);

        var responseText = Assert.Single(
            blocks.OfType<TextContentBlock>(),
            block => block.Role == ChatRole.Assistant);
        Assert.Equal("Enjoy this recipe!", responseText.RawText);
        Assert.DoesNotContain(
            blocks.OfType<TextContentBlock>(),
            block => block.RawText.StartsWith('{'));
    }

    [Fact]
    public async Task StateMapper_StateOnlyUpdate_ProducesNoAssistantBlock()
    {
        var client = CreateClient(EmitStateOnly());
        var agent = CreateRecipeAgent(client);

        var blocks = await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Recipe?")));

        Assert.Equal("Pasta", agent.State.Value.Title);
        Assert.DoesNotContain(blocks, block => block.Role == ChatRole.Assistant);
    }

    [Fact]
    public async Task StateMapper_ReturnsFalse_DoesNotFilterOrUpdateState()
    {
        var client = CreateClient(EmitStateOnly());
        var agent = new UIAgent<RecipeState>(client, options =>
        {
            options.StateMapper = context =>
            {
                var content = GetFirstUnhandled(context);
                context.MarkHandled(content);
                context.SetState(new RecipeState { Title = "ignored" });
                return false;
            };
        });

        var blocks = await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Recipe?")));

        Assert.Equal(string.Empty, agent.State.Value.Title);
        var text = Assert.Single(
            blocks.OfType<TextContentBlock>(),
            block => block.Role == ChatRole.Assistant);
        Assert.StartsWith("{", text.RawText);
    }

    [Fact]
    public async Task StateMapper_WrongStateType_FiltersContentWithoutReplacingTypedState()
    {
        var client = CreateClient(EmitStateOnly());
        var initial = new RecipeState { Title = "initial" };
        var agent = new UIAgent<RecipeState>(client, options =>
        {
            options.StateMapper = context =>
            {
                var content = GetFirstUnhandled(context);
                context.MarkHandled(content);
                context.SetState("not a RecipeState");
                return true;
            };
        }, initial);

        var blocks = await EnumerateAsync(agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Recipe?")));

        Assert.Same(initial, agent.State.Value);
        Assert.DoesNotContain(blocks, block => block.Role == ChatRole.Assistant);
    }

    [Fact]
    public async Task RestoreAsync_ReappliesStateMapperAndKeepsStateContentHidden()
    {
        var thread = new InMemoryConversationThread("thread-1");
        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "Recipe?"));
        await foreach (var update in EmitStateAndText())
            thread.AppendUpdate(update);
        thread.CompleteTurn();

        var client = CreateClient(ResponseEmitters.EmitTextResponse("unused"));
        var agent = CreateRecipeAgent(client, thread);
        var context = new AgentContext(agent);

        await context.RestoreAsync();

        Assert.Equal("Pasta", agent.State.Value.Title);
        var turn = Assert.Single(context.Turns);
        var text = Assert.Single(turn.ResponseBlocks.OfType<TextContentBlock>());
        Assert.Equal("Enjoy this recipe!", text.RawText);
    }

    [Fact]
    public void GetFilteredUpdate_PreservesUpdateMetadata()
    {
        var stateContent = new TextContent("""{"title":"Pasta"}""");
        var visibleContent = new TextContent("visible");
        var raw = new object();
        var additionalProperties = new AdditionalPropertiesDictionary
        {
            ["test"] = "value",
        };
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            AuthorName = "Sage",
            MessageId = "message-1",
            ResponseId = "response-1",
            ConversationId = "conversation-1",
            RawRepresentation = raw,
            AdditionalProperties = additionalProperties,
            Contents = [stateContent, visibleContent],
        };
        var context = new StateMapperContext(update);
        context.MarkHandled(stateContent);

        var filtered = context.GetFilteredUpdate();

        Assert.NotSame(update, filtered);
        Assert.Equal(update.Role, filtered.Role);
        Assert.Equal(update.AuthorName, filtered.AuthorName);
        Assert.Equal(update.MessageId, filtered.MessageId);
        Assert.Equal(update.ResponseId, filtered.ResponseId);
        Assert.Equal(update.ConversationId, filtered.ConversationId);
        Assert.Same(raw, filtered.RawRepresentation);
        Assert.Equal("value", filtered.AdditionalProperties!["test"]);
        Assert.Equal([visibleContent], filtered.Contents);
    }

    [Fact]
    public void MarkHandled_UnknownContent_DoesNotFilterUpdate()
    {
        var content = new TextContent("visible");
        var update = new ChatResponseUpdate { Contents = [content] };
        var context = new StateMapperContext(update);

        context.MarkHandled(new TextContent("not present"));

        Assert.Same(update, context.GetFilteredUpdate());
    }

    private static UIAgent<RecipeState> CreateRecipeAgent(
        IChatClient client,
        IConversationThread? thread = null)
    {
        return new UIAgent<RecipeState>(client, options =>
        {
            options.Thread = thread;
            options.StateMapper = context =>
            {
                foreach (var content in context.UnhandledContents)
                {
                    if (content is not TextContent text
                        || text.Text is null
                        || !text.Text.StartsWith('{'))
                    {
                        continue;
                    }

                    var state = JsonSerializer.Deserialize<RecipeState>(text.Text);
                    if (state is null)
                        continue;

                    context.MarkHandled(content);
                    context.SetState(state);
                    return true;
                }
                return false;
            };
        });
    }

    private static AIContent GetFirstUnhandled(StateMapperContext context)
    {
        foreach (var content in context.UnhandledContents)
            return content;

        throw new InvalidOperationException("The update contains no unhandled content.");
    }

    private static DelegatingStreamingChatClient CreateClient(
        IAsyncEnumerable<ChatResponseUpdate> response)
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => response);
        return client;
    }

    private static async Task<List<ContentBlock>> EnumerateAsync(
        IAsyncEnumerable<ContentBlock> blocks)
    {
        var result = new List<ContentBlock>();
        await foreach (var block in blocks)
            result.Add(block);
        return result;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitStateAndText(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-state",
            Contents =
            [
                new TextContent("""{"Title":"Pasta","Cuisine":"Italian"}"""),
                new TextContent("Enjoy this recipe!"),
            ],
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitStateOnly(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-state",
            Contents =
            [
                new TextContent("""{"Title":"Pasta","Cuisine":"Italian"}"""),
            ],
        };
        await Task.CompletedTask;
    }
}
