using System.Net;
using System.Text;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class MistralEmptyMarkdownTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExtractAsync_EmptyOptionalPageMarkdownDoesNotDiscardActualHeader(bool streamPages)
    {
        using var http = new HttpClient(new ResponseHandler())
        {
            BaseAddress = new Uri("https://example.test/"),
        };
        using var client = new FoundryMistralDocumentExtractionClient(http, "test-deployment");
        var result = await new DocumentExtractionService().ExtractAsync(
            client, new SelectedDocument("input.png", "image/png", [1]), streamPages);
        Assert.Contains("Actual header", result.Output);
        using var input = new MemoryStream([1]);
        using var extraction = await client.ExtractAsync(input, "header", "image/png");
        Assert.Null(Assert.Single(extraction.Pages).Markdown);
        var header = Assert.IsType<DocumentTextElement>(Assert.Single(Assert.Single(extraction.Document.Sections).Elements));
        Assert.Equal("Actual header", header.Text);
        Assert.Equal(DocumentTextKind.Header, header.Kind);
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"pages":[{"index":0,"markdown":"","header":"Actual header"}]}""",
                    Encoding.UTF8, "application/json"),
            });
    }
}
