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
    public void CreateOptions_AppleVision_MapsAppleSettings()
    {
        using var client = CreateClient("apple-vision", "Apple Vision");
        var settings = new DocumentSettingsViewModel([client])
        {
            DetectBarcodes = false,
            AutomaticallyDetectLanguage = true,
        };

        var options = settings.CreateOptions();

        Assert.Equal(
            ["apple.vision.barcodeDetectionEnabled", "apple.vision.automaticallyDetectLanguage"],
            settings.RequestOptions.Select(static option => option.Key));
        Assert.False(Assert.IsType<bool>(options.AdditionalProperties!["apple.vision.barcodeDetectionEnabled"]));
        Assert.True(Assert.IsType<bool>(options.AdditionalProperties["apple.vision.automaticallyDetectLanguage"]));
        Assert.False(options.AdditionalProperties.ContainsKey("mistral.includeImages"));
    }

    [Fact]
    public void CreateOptions_MistralDocument_MapsImageSetting()
    {
        using var client = CreateClient("foundry-mistral-document", "Mistral document");
        var settings = new DocumentSettingsViewModel([client])
        {
            IncludeImages = true,
        };

        var options = settings.CreateOptions();

        Assert.Equal(["mistral.includeImages"], settings.RequestOptions.Select(static option => option.Key));
        Assert.True(Assert.IsType<bool>(options.AdditionalProperties!["mistral.includeImages"]));
        Assert.False(options.AdditionalProperties.ContainsKey("apple.vision.barcodeDetectionEnabled"));
    }

    [Fact]
    public void CreateOptions_ClientWithoutSettings_LeavesAdditionalPropertiesUnset()
    {
        using var client = CreateClient("foundry-vision-chat", "Vision chat");
        var settings = new DocumentSettingsViewModel([client]);

        var options = settings.CreateOptions();

        Assert.False(settings.HasRequestOptions);
        Assert.Null(options.AdditionalProperties);
    }

    [Fact]
    public void SelectedOption_ChangesVisibleOptionsAndPreservesValues()
    {
        using var apple = CreateClient("apple-vision", "Apple Vision");
        using var mistral = CreateClient("foundry-mistral-document", "Mistral document");
        using var vision = CreateClient("foundry-vision-chat", "Vision chat");
        var settings = new DocumentSettingsViewModel([apple, mistral, vision])
        {
            DetectBarcodes = false,
            IncludeImages = true,
        };

        settings.SelectedOption = settings.Clients[1];
        Assert.Equal(["mistral.includeImages"], settings.RequestOptions.Select(static option => option.Key));
        Assert.True(settings.IncludeImages);

        settings.SelectedOption = settings.Clients[2];
        Assert.Empty(settings.RequestOptions);

        settings.SelectedOption = settings.Clients[0];
        Assert.Equal(
            ["apple.vision.barcodeDetectionEnabled", "apple.vision.automaticallyDetectLanguage"],
            settings.RequestOptions.Select(static option => option.Key));
        Assert.False(settings.DetectBarcodes);
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
