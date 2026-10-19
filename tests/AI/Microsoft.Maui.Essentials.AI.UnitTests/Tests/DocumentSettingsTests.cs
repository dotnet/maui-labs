using Xunit;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class DocumentSettingsTests
{
    [Fact]
    public void Validate_DocumentDeploymentRequiresSharedEndpointAndKey()
    {
        var settings = new AISettings { DocumentDeploymentName = "document-model" };
        Assert.Throws<InvalidOperationException>(settings.Validate);
        settings.Endpoint = new Uri("https://example.openai.azure.com/openai/v1/");
        Assert.Throws<InvalidOperationException>(settings.Validate);
        settings.ApiKey = "test-key";
        settings.Validate();
    }

    [Theory]
    [InlineData("https://example.openai.azure.com/openai/v1/", "https://example.services.ai.azure.com/")]
    [InlineData("https://example.services.ai.azure.com/api/projects/test", "https://example.services.ai.azure.com/")]
    public void GetFoundryDocumentEndpoint_UsesResourceRoot(string endpoint, string expected)
    {
        var settings = new AISettings { Endpoint = new Uri(endpoint) };
        Assert.Equal(new Uri(expected), settings.GetFoundryDocumentEndpoint());
    }

    [Theory]
    [InlineData("http://example.services.ai.azure.com/")]
    [InlineData("https://user:password@example.services.ai.azure.com/")]
    [InlineData("https://example.services.ai.azure.com/?secret=value")]
    [InlineData("https://example.services.ai.azure.com/#fragment")]
    [InlineData("/relative")]
    public void GetFoundryDocumentEndpoint_InvalidEndpointRejectsWithoutEchoingValues(string endpoint)
    {
        var settings = new AISettings { Endpoint = new Uri(endpoint, UriKind.RelativeOrAbsolute) };
        var error = Assert.Throws<InvalidOperationException>(() => settings.GetFoundryDocumentEndpoint());
        Assert.DoesNotContain(endpoint, error.Message);
    }

    [Fact]
    public void Validate_IndependentDocumentIntelligenceSettingsRequireBothValues()
    {
        var settings = new AISettings { DocumentIntelligenceKey = "test-key" };
        Assert.Throws<InvalidOperationException>(settings.Validate);
        settings.DocumentIntelligenceEndpoint = new Uri("https://example.services.ai.azure.com/");
        settings.Validate();
        settings.DocumentIntelligenceKey = null;
        Assert.Throws<InvalidOperationException>(settings.Validate);
    }
}
