using System.Net;
using System.Text;
using System.Text.Json;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class FoundryMistralOcrClientTests
{
    [Fact]
    public async Task ExtractAsync_MapsOcr4PagesBlocksTablesAndImages()
    {
        var handler = new RecordingHandler(JsonResponse(SuccessResponse));
        using var client = new FoundryMistralOcrClient(
            new HttpClient(handler),
            new Uri("https://foundry.example.test"),
            "foundry-key",
            disposeHttpClient: true);
        var options = new DocumentExtractionOptions
        {
            AdditionalProperties = new()
            {
                ["mistral.includeImages"] = true,
            }
        };

        var result = await client.ExtractAsync(
            new MemoryStream([1, 2, 3]),
            "application/pdf",
            options);

        var page = Assert.Single(result.Pages);
        Assert.Equal("# EVENT REGISTRATION", page.Text);
        Assert.Equal(new DocumentPageDimensions(1600, 1200), page.Dimensions);
        Assert.Equal(DocumentCoordinateUnit.Pixel, page.CoordinateUnit);
        Assert.Equal(DocumentCoordinateOrigin.TopLeft, page.CoordinateOrigin);
        var title = Assert.Single(page.Elements.OfType<DocumentBlock>(), static block =>
            block.Kind == DocumentBlockKind.Title);
        Assert.Equal("EVENT REGISTRATION", title.Text);
        Assert.Equal(0.99, title.Confidence);
        Assert.NotNull(title.BoundingRegion);
        var table = Assert.Single(page.Elements.OfType<DocumentTable>());
        Assert.Contains("<table>", table.MarkdownRepresentation);
        Assert.NotNull(table.BoundingRegion);
        var image = Assert.Single(page.Elements.OfType<DocumentImage>());
        Assert.NotNull(image.Content);
        Assert.Equal([1, 2, 3], image.Content!.Data.ToArray());
        Assert.Contains(page.Elements.OfType<DocumentBlock>(), static block =>
            block.Kind?.Value == "signature");
        Assert.Equal(1, result.Usage?.PagesProcessed);
        Assert.Equal(3d, result.Usage?.AdditionalProperties!["doc_size_bytes"]);
        var raw = Assert.IsType<FoundryMistralOcrRawReference>(page.RawRepresentation);
        Assert.Contains("\"blocks\"", raw.Json);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            new Uri("https://foundry.example.test/providers/mistral/azure/ocr"),
            request.RequestUri);
        Assert.Equal("foundry-key", request.ApiKey);
        using var requestJson = JsonDocument.Parse(request.Body!);
        var root = requestJson.RootElement;
        Assert.Equal("mistral-ocr-4-0", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("include_blocks").GetBoolean());
        Assert.True(root.GetProperty("include_image_base64").GetBoolean());
        Assert.Equal("html", root.GetProperty("table_format").GetString());
        Assert.Equal(
            "data:application/pdf;base64,AQID",
            root.GetProperty("document").GetProperty("document_url").GetString());
    }

    [Fact]
    public async Task ExtractAsync_OverThirtyMegabytes_ThrowsBeforeRequest()
    {
        var handler = new RecordingHandler(JsonResponse(SuccessResponse));
        using var client = new FoundryMistralOcrClient(
            new HttpClient(handler),
            new Uri("https://foundry.example.test/"),
            "foundry-key",
            disposeHttpClient: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ExtractAsync(
                new MemoryStream(new byte[(30 * 1024 * 1024) + 1]),
                "application/pdf"));

        Assert.Contains("30 MB", exception.Message);
        Assert.Empty(handler.Requests);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private int _index;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.RequestUri,
                request.Headers.TryGetValues("api-key", out var values)
                    ? values.Single()
                    : null,
                body));
            return responses[_index++];
        }
    }

    private sealed record RecordedRequest(
        Uri? RequestUri,
        string? ApiKey,
        string? Body);

    private const string SuccessResponse = """
        {
          "pages": [
            {
              "index": 0,
              "markdown": "# EVENT REGISTRATION",
              "header": "Conference",
              "footer": "Page 1",
              "dimensions": { "width": 1600, "height": 1200 },
              "hyperlinks": [],
              "blocks": [
                {
                  "type": "title",
                  "top_left_x": 100,
                  "top_left_y": 80,
                  "bottom_right_x": 900,
                  "bottom_right_y": 150,
                  "content": "EVENT REGISTRATION",
                  "confidence_scores": { "page_confidence": 0.99 }
                },
                {
                  "type": "table",
                  "top_left_x": 100,
                  "top_left_y": 250,
                  "bottom_right_x": 1000,
                  "bottom_right_y": 650,
                  "content": "<table><tr><td>Workshop</td></tr></table>",
                  "table_id": "table-1"
                },
                {
                  "type": "image",
                  "top_left_x": 1100,
                  "top_left_y": 250,
                  "bottom_right_x": 1450,
                  "bottom_right_y": 600,
                  "content": "Conference logo",
                  "image_id": "image-1"
                },
                {
                  "type": "signature",
                  "top_left_x": 100,
                  "top_left_y": 800,
                  "bottom_right_x": 400,
                  "bottom_right_y": 950,
                  "content": "Signed"
                }
              ],
              "tables": [
                {
                  "id": "table-1",
                  "content": "<table><tr><td>Workshop</td></tr></table>"
                }
              ],
              "images": [
                {
                  "id": "image-1",
                  "image_base64": "data:image/png;base64,AQID",
                  "top_left_x": 1100,
                  "top_left_y": 250,
                  "bottom_right_x": 1450,
                  "bottom_right_y": 600
                }
              ]
            }
          ],
          "model": "mistral-ocr-4-0",
          "usage_info": {
            "pages_processed": 1,
            "doc_size_bytes": 3
          }
        }
        """;
}
