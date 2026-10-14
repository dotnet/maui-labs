using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.AI.Chat.Tests;

public class DocumentSettingsViewModelTests
{
    [Fact]
    public void Constructor_RegistersActualClientsAndReadersWithoutProviderFactories()
    {
        var reader = Reader("apple-reader");
        using var client = Client("apple-client");
        var settings = new DocumentSettingsViewModel([reader], [client]);
        Assert.Equal(DocumentOperationMode.Extraction, settings.Mode);
        Assert.Same(client, settings.SelectedClientOption!.Client);
        Assert.Same(reader, settings.SelectedReader);
        Assert.True(settings.HasSelection);
        Assert.False(settings.CanSelectModel);
        settings.Mode = DocumentOperationMode.Reader;
        Assert.True(settings.IsReaderMode);
        Assert.False(settings.IsClientMode);
        Assert.Equal("Reader", settings.CurrentName);
        Assert.False(settings.IsExtractionMode);
        Assert.Equal("Read", settings.OperationLabel);
    }

    [Fact]
    public void ModelOverridesAreOnlyForwardedWhereTheSelectedProviderSupportsThem()
    {
        using var local = Client("apple-client");
        using var cloud = Client("cloud-client", isCloud: true);
        var settings = new DocumentSettingsViewModel([], [local, cloud]) { ModelId = "deployment" };
        Assert.Null(settings.CreateOptions().ModelId);
        settings.SelectedClientOption = settings.Clients[1];
        Assert.True(settings.CanSelectModel);
        Assert.Equal("deployment", settings.CreateOptions()!.ModelId);
        settings.Mode = DocumentOperationMode.Reader;
        Assert.Null(settings.CreateOptions().ModelId);
    }

    [Fact]
    public void SelectionRejectsUnregisteredOrDuplicateProviders()
    {
        using var client = Client("same");
        using var duplicate = Client("same");
        Assert.Throws<ArgumentException>(() => new DocumentSettingsViewModel([], [client, duplicate]));
        var settings = new DocumentSettingsViewModel([], [client]);
        using var other = Client("other");
        Assert.Throws<ArgumentException>(() => settings.SelectedClientOption = new DocumentExtractionClientOption(other, 2));
    }

    [Fact]
    public void ReaderSelectionRemainsAvailableWhenNoExtractionClientIsConfigured()
    {
        var settings = new DocumentSettingsViewModel([Reader("reader")]);
        Assert.Equal(DocumentOperationMode.Reader, settings.Mode);
        Assert.True(settings.HasSelection);
        settings.Mode = DocumentOperationMode.Extraction;
        Assert.False(settings.HasSelection);
        settings.Mode = DocumentOperationMode.Reader;
        Assert.True(settings.HasSelection);
    }

    [Fact]
    public void CaptureAndNativeLanguageControlsArePerCallAndNeverForceAppleDefaults()
    {
        using var apple = Client("apple-vision-document");
        using var cloud = Client("cloud", isCloud: true);
        var settings = new DocumentSettingsViewModel([], [apple, cloud]);
        Assert.True(settings.CanSelectAppleOptions);
        Assert.True(settings.CreateOptions().CaptureRawOutput);
        Assert.Empty(settings.CreateOptions().AdditionalProperties);
        settings.CaptureRawOutput = false;
        settings.AppleRecognitionLanguages = " en, fr ";
        settings.ModelId = "cloud-deployment";
        var first = settings.CreateOptions();
        Assert.False(first.CaptureRawOutput);
        Assert.Null(first.ModelId);
        Assert.Equal(["en", "fr"], Assert.IsType<string[]>(first.AdditionalProperties["apple.vision.recognitionLanguages"]));
        settings.SelectedClientOption = settings.Clients[1];
        var second = settings.CreateOptions();
        Assert.False(settings.CanSelectAppleOptions);
        Assert.Equal("cloud-deployment", second.ModelId);
        Assert.Empty(second.AdditionalProperties);
        Assert.False(second.CaptureRawOutput);
        Assert.NotSame(first, second);
        Assert.Single(first.AdditionalProperties);
    }

    private static IngestionDocumentReader Reader(string id) =>
        new DescribedDocumentReader(new TestReader(), new DocumentReaderDescriptor(id, "Reader", "Description"));

    private static IDocumentExtractionClient Client(string id, bool isCloud = false) =>
        new DescribedDocumentExtractionClient(
            new TestClient(), new DocumentExtractionClientDescriptor(id, "Client", "Description", isCloud));

    private sealed class TestReader : IngestionDocumentReader
    {
        public override Task<IngestionDocument> ReadAsync(
            Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default) =>
            Task.FromResult(new IngestionDocument(identifier));
    }

    private sealed class TestClient : IDocumentExtractionClient
    {
        public IAsyncEnumerable<DocumentExtractionPage> ExtractPagesAsync(
            Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
