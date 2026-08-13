// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class RichTextContentHandlerTests
{
    [Fact]
    public void RichTextContent_PreservesTextAndNodes()
    {
        IReadOnlyList<RichTextNode> nodes = [CreateParagraph("hello")];

        var content = new RichTextContent("hello", nodes);

        Assert.Equal("hello", content.Text);
        Assert.Same(nodes, content.Nodes);
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
        var changed = 0;

        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "format this")))
        {
            if (block is RichContentBlock rich
                && block.Role == ChatRole.Assistant)
            {
                richBlock = rich;
                rich.OnChanged(() => changed++);
            }
        }

        Assert.NotNull(richBlock);
        Assert.Equal("second", richBlock.RawText);
        Assert.Equal("second", GetParagraphText(Assert.Single(richBlock.Content)));
        Assert.True(changed >= 1);
    }

    [Fact]
    public async Task RichAndPlainContent_UseSeparateHandlers()
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

        var assistantBlocks = blocks
            .Where(block => block.Role == ChatRole.Assistant)
            .ToArray();
        Assert.Contains(
            assistantBlocks,
            block => block is RichContentBlock and not TextContentBlock);
        Assert.Contains(
            assistantBlocks,
            block => block is TextContentBlock text && text.RawText == "plain");
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
