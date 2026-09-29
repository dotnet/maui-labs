using System.Runtime.CompilerServices;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class DocumentSettingsViewModelTests
{
    [Fact]
    public void Constructor_DescribedClients_SelectsFirstClient()
    {
        using var first = CreateClient("first", "First");
        using var second = CreateClient("second", "Second");

        var settings = new DocumentSettingsViewModel([first, second]);

        Assert.Equal(2, settings.Clients.Count);
        Assert.Same(first, settings.SelectedClient);
        Assert.Equal("First", settings.SelectedDescriptor?.Name);
        Assert.Equal("DocumentClient0Radio", settings.Clients[0].AutomationId);
    }

    [Fact]
    public void Constructor_DuplicateDescriptorIds_Throws()
    {
        using var first = CreateClient("duplicate", "First");
        using var second = CreateClient("duplicate", "Second");

        var exception = Assert.Throws<ArgumentException>(
            () => new DocumentSettingsViewModel([first, second]));

        Assert.Contains("distinct IDs", exception.Message);
    }

    [Fact]
    public void CreateOptions_MapsSharedPlaygroundSettings()
    {
        using var client = CreateClient("client", "Client");
        var settings = new DocumentSettingsViewModel([client])
        {
            DetectBarcodes = false,
            AutomaticallyDetectLanguage = true,
            IncludeImages = true,
        };

        var options = settings.CreateOptions();

        Assert.False(Assert.IsType<bool>(
            options.AdditionalProperties!["apple.vision.barcodeDetectionEnabled"]));
        Assert.True(Assert.IsType<bool>(
            options.AdditionalProperties["apple.vision.automaticallyDetectLanguage"]));
        Assert.True(Assert.IsType<bool>(
            options.AdditionalProperties["mistral.includeImages"]));
    }

    private static IDocumentExtractionClient CreateClient(string id, string displayName) =>
        new DescribedDocumentExtractionClient(
            new EmptyDocumentClient(),
            new DocumentExtractionClientDescriptor(
                id,
                displayName,
                $"{displayName} description"));

    private sealed class EmptyDocumentClient : IDocumentExtractionClient
    {
        public Task<DocumentExtractionResult> ExtractAsync(
            Stream document,
            string mediaType,
            DocumentExtractionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentExtractionResult([]));

        public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
            Stream document,
            string mediaType,
            DocumentExtractionOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
