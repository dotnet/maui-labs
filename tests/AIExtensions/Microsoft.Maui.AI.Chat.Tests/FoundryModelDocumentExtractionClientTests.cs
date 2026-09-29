using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class FoundryModelDocumentExtractionClientTests
{
    [Fact]
    public async Task ExtractAsync_SendsDocumentAndMapsStructuredResponse()
    {
        var chat = new RecordingChatClient("""
            {
              "pages": [
                {
                  "pageNumber": 1,
                  "text": "EVENT REGISTRATION\nWorkshop 9:00 AM",
                  "blocks": [
                    { "kind": "title", "text": "EVENT REGISTRATION" },
                    { "kind": "paragraph", "text": "Workshop 9:00 AM" }
                  ],
                  "tables": [
                    {
                      "rowCount": 2,
                      "columnCount": 2,
                      "cells": [
                        { "rowIndex": 0, "columnIndex": 0, "rowSpan": 1, "columnSpan": 1, "kind": "columnHeader", "content": "Session" },
                        { "rowIndex": 0, "columnIndex": 1, "rowSpan": 1, "columnSpan": 1, "kind": "columnHeader", "content": "Time" },
                        { "rowIndex": 1, "columnIndex": 0, "rowSpan": 1, "columnSpan": 1, "kind": "content", "content": "Workshop" },
                        { "rowIndex": 1, "columnIndex": 1, "rowSpan": 1, "columnSpan": 1, "kind": "content", "content": "9:00 AM" }
                      ]
                    }
                  ]
                }
              ]
            }
            """);
        using var client = new FoundryModelDocumentExtractionClient(
            chat,
            "vision-deployment");

        var result = await client.ExtractAsync(
            new MemoryStream([1, 2, 3]),
            "application/pdf");

        var page = Assert.Single(result.Pages);
        Assert.Equal("EVENT REGISTRATION\nWorkshop 9:00 AM", page.Text);
        Assert.Null(page.Dimensions);
        Assert.All(page.Elements, static element => Assert.Null(element.BoundingRegion));
        var title = Assert.Single(page.Elements.OfType<DocumentBlock>(), static block =>
            block.Kind == DocumentBlockKind.Title);
        Assert.Equal("EVENT REGISTRATION", title.Text);
        var table = Assert.Single(page.Elements.OfType<DocumentTable>());
        Assert.Equal(4, table.Cells?.Count);
        var raw = Assert.IsType<FoundryModelDocumentRawReference>(page.RawRepresentation);
        Assert.Contains("\"pages\"", raw.Json);
        Assert.Equal(3, result.Usage?.InputTokenCount);
        Assert.Equal(5, result.Usage?.OutputTokenCount);
        Assert.Equal(8, result.Usage?.TotalTokenCount);

        var message = Assert.Single(chat.Messages!);
        Assert.Contains(message.Contents, static content =>
            content is TextContent text && text.Text.Contains("no geometry", StringComparison.OrdinalIgnoreCase));
        var document = Assert.Single(message.Contents.OfType<DataContent>());
        Assert.Equal("application/pdf", document.MediaType);
        Assert.Equal([1, 2, 3], document.Data.ToArray());
        Assert.Equal("vision-deployment", chat.Options!.ModelId);
        Assert.IsType<ChatResponseFormatJson>(chat.Options.ResponseFormat);
    }

    [Fact]
    public async Task ExtractAsync_UnsupportedMediaType_Throws()
    {
        using var client = new FoundryModelDocumentExtractionClient(
            new RecordingChatClient("""{"pages":[]}"""),
            "vision-deployment");

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            client.ExtractAsync(new MemoryStream([1]), "image/heic"));
    }

    private sealed class RecordingChatClient(string responseText) : IChatClient
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
            return Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                responseText))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 3,
                    OutputTokenCount = 5,
                    TotalTokenCount = 8,
                }
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
