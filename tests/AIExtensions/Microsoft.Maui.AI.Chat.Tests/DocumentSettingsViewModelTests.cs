using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace Microsoft.Maui.AI.Chat.Tests;

public class DocumentSettingsViewModelTests
{
    [Fact]
    public void Constructor_SelectsTheFirstConfiguredClient()
    {
        using var first = Client("first");
        using var second = Client("second");
        var settings = new DocumentSettingsViewModel([first, second]);
        Assert.Equal(2, settings.Clients.Count);
        Assert.Same(first, settings.SelectedClientOption!.Client);
        Assert.Equal("first", settings.CurrentName);
        Assert.True(settings.HasSelection);
        settings.SelectedClientOption = settings.Clients[1];
        Assert.Same(second, settings.SelectedClientOption.Client);
        Assert.Equal("second", settings.CurrentName);
    }

    [Theory]
    [InlineData("apple-vision-document")]
    [InlineData("foundry-mistral-document")]
    [InlineData("any-other-client")]
    public void OptionsAreForwardedIdenticallyRegardlessOfClientIdentity(string id)
    {
        using var client = Client(id);
        var settings = new DocumentSettingsViewModel([client]) { ExtractorName = " deployment " };
        var options = settings.CreateOptions();
        Assert.Equal("deployment", options.ExtractorName);
        Assert.Empty(options.AdditionalProperties);
        Assert.NotSame(options, settings.CreateOptions());
    }

    [Fact]
    public void EmptyConfigurationHasNoSelection()
    {
        var settings = new DocumentSettingsViewModel([]);
        Assert.Empty(settings.Clients);
        Assert.False(settings.HasSelection);
        Assert.Null(settings.SelectedClientOption);
        Assert.Equal("No client", settings.CurrentName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankOptionsPreserveClientDefaults(string extractorName)
    {
        var settings = new DocumentSettingsViewModel([]) { ExtractorName = extractorName };
        var options = settings.CreateOptions();
        Assert.Null(options.ExtractorName);
        Assert.Empty(options.AdditionalProperties);
    }

    private static DocumentExtractionClient Client(string id) =>
        new DescribedDocumentExtractionClient(
            new TestClient(), new DocumentExtractionClientDescriptor(id, id, "Description"));

    private sealed class TestClient : DocumentExtractionClient
    {
        public override Task<ExtractionDocument> ExtractAsync(
            Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
