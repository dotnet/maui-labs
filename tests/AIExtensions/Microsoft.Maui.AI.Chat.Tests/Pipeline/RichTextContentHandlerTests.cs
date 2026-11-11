// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class RichTextContentHandlerTests
{
    [Fact]
    public void RichTextContent_CopiesNodeList()
    {
        var paragraph = CreateParagraph("hello");
        var nodes = new List<RichTextNode> { paragraph };

        var content = new RichTextContent("hello", nodes);
        nodes.Clear();

        Assert.Equal("hello", content.Text);
        Assert.Same(paragraph, Assert.Single(content.Nodes));
    }

    [Fact]
    public void RichTextContent_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RichTextContent(null!, Array.Empty<RichTextNode>()));
        Assert.Throws<ArgumentNullException>(() =>
            new RichTextContent("", null!));
    }

    [Fact]
    public async Task ProviderSnapshots_UpdateOneStableRichBlock()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((_, _, cancellationToken) =>
            EmitRichSnapshots(cancellationToken));
        var agent = new UIAgent(client);
        RichContentBlock? richBlock = null;
        List<string>? changes = null;

        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "format this")))
        {
            if (block is RichContentBlock rich
                && block.Role == ChatRole.Assistant)
            {
                richBlock = rich;
                changes = rich.RecordPropertyChanges();
            }
        }

        Assert.NotNull(richBlock);
        Assert.Equal("second", richBlock.RawText);
        Assert.Equal("second", GetParagraphText(Assert.Single(richBlock.Content)));
        Assert.Contains(nameof(RichContentBlock.RawText), changes!);
    }

    [Fact]
    public async Task RichSnapshotWithTextFallback_EmitsSingleRichBlock()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((_, _, _) => EmitRichAndPlain());
        var agent = new UIAgent(client);
        var blocks = new List<ContentBlock>();

        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "both")))
        {
            blocks.Add(block);
        }

        var richBlock = Assert.Single(blocks, block => block.Role == ChatRole.Assistant);
        Assert.IsNotType<TextContentBlock>(richBlock);
        Assert.Equal("rich", Assert.IsAssignableFrom<RichContentBlock>(richBlock).RawText);
    }

    [Fact]
    public async Task Process_SeveralSnapshotsInOneUpdate_LastSnapshotWins()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents =
            [
                new RichTextContent("partial", [CreateParagraph("partial")]),
                new RichTextContent("complete", [CreateParagraph("complete")]),
            ],
        };

        var blocks = await ProcessAsync(pipeline, update);

        var block = Assert.IsAssignableFrom<RichContentBlock>(Assert.Single(blocks));
        Assert.Equal("complete", block.RawText);
        Assert.Equal("complete", GetParagraphText(Assert.Single(block.Content)));
    }

    [Fact]
    public async Task Process_SnapshotsWithDifferentMessageIds_EmitSeparateBlocks()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());

        var firstBlocks = await ProcessAsync(
            pipeline,
            CreateRichTextUpdate("msg-1", "First"));
        var secondBlocks = await ProcessAsync(
            pipeline,
            CreateRichTextUpdate("msg-2", "Second"));

        var first = Assert.IsAssignableFrom<RichContentBlock>(Assert.Single(firstBlocks));
        var second = Assert.IsAssignableFrom<RichContentBlock>(Assert.Single(secondBlocks));
        Assert.Equal("msg-1", first.Id);
        Assert.Equal("First", first.RawText);
        Assert.Equal(BlockLifecycleState.Inactive, first.LifecycleState);
        Assert.Equal("msg-2", second.Id);
        Assert.Equal("Second", second.RawText);
    }

    [Fact]
    public async Task Process_SnapshotsForSameMessage_UpdateBlockAndNotify()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());
        var block = Assert.IsAssignableFrom<RichContentBlock>(Assert.Single(
            await ProcessAsync(pipeline, CreateRichTextUpdate("msg-1", "Partial"))));
        var changes = block.RecordPropertyChanges();

        var emitted = await ProcessAsync(
            pipeline,
            CreateRichTextUpdate("msg-1", "Complete"));

        Assert.Empty(emitted);
        Assert.Equal("Complete", block.RawText);
        Assert.Equal([nameof(RichContentBlock.RawText), nameof(RichContentBlock.Content)], changes);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitRichSnapshots(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "rich-1",
            Contents =
            [
                new RichTextContent("first", [CreateParagraph("first")]),
            ],
        };
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "rich-1",
            Contents =
            [
                new RichTextContent("second", [CreateParagraph("second")]),
            ],
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitRichAndPlain()
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "mixed",
            Contents =
            [
                new RichTextContent("rich", [CreateParagraph("rich")]),
                new TextContent("plain"),
            ],
        };
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Process_UpdateWithoutContents_KeepsTheSnapshotBlock()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());
        var block = Assert.IsAssignableFrom<RichContentBlock>(Assert.Single(
            await ProcessAsync(pipeline, CreateRichTextUpdate("msg-1", "Partial"))));

        Assert.Empty(await ProcessAsync(pipeline, new ChatResponseUpdate { MessageId = "msg-1" }));
        Assert.Empty(await ProcessAsync(pipeline, CreateRichTextUpdate("msg-1", "Complete")));

        Assert.Equal("Complete", block.RawText);
        Assert.Equal(BlockLifecycleState.Active, block.LifecycleState);
    }

    private static ChatResponseUpdate CreateRichTextUpdate(string messageId, string text) => new()
    {
        Role = ChatRole.Assistant,
        MessageId = messageId,
        Contents = [new RichTextContent(text, [CreateParagraph(text)])],
    };

    private static async Task<List<ContentBlock>> ProcessAsync(
        BlockMappingPipeline pipeline,
        ChatResponseUpdate update)
    {
        var blocks = new List<ContentBlock>();
        await foreach (var block in pipeline.Process(update))
        {
            blocks.Add(block);
        }

        return blocks;
    }

    private static ParagraphNode CreateParagraph(string text)
    {
        var paragraph = new ParagraphNode();
        paragraph.AddChild(new TextNode(text));
        return paragraph;
    }

    private static string GetParagraphText(RichTextNode node)
    {
        var paragraph = Assert.IsType<ParagraphNode>(node);
        return Assert.IsType<TextNode>(Assert.Single(paragraph.Children)).Text;
    }
}
