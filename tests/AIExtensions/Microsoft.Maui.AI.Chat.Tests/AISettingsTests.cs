using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class AISettingsTests
{
    [Fact]
    public void Bind_AISection_PopulatesTypedProviderSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Endpoint"] = "https://example.test/openai/v1/",
                ["AI:ApiKey"] = "test-key",
                ["AI:DeploymentName"] = "chat",
                ["AI:ImageDeploymentName"] = "image",
                ["AI:EmbeddingDeploymentName"] = "embedding",
                ["AI:DocumentDeploymentName"] = "mistral-ocr-4-0",
                ["AI:FoundryEndpoint"] = "https://foundry.example.test/",
                ["AI:FoundryApiKey"] = "foundry-key"
            })
            .Build();

        var settings = new AISettings();
        configuration.GetSection(AISettings.SectionName).Bind(settings);
        settings.Validate();

        Assert.Equal(new Uri("https://example.test/openai/v1/"), settings.Endpoint);
        Assert.Equal("test-key", settings.ApiKey);
        Assert.Equal("chat", settings.DeploymentName);
        Assert.Equal("image", settings.ImageDeploymentName);
        Assert.Equal("embedding", settings.EmbeddingDeploymentName);
        Assert.Equal("mistral-ocr-4-0", settings.DocumentDeploymentName);
        Assert.Equal(new Uri("https://foundry.example.test/"), settings.FoundryEndpoint);
        Assert.Equal("foundry-key", settings.FoundryApiKey);
    }

    [Fact]
    public void Validate_DocumentDeploymentWithFoundryCredentials_DoesNotRequireOpenAICredentials()
    {
        var settings = new AISettings
        {
            DocumentDeploymentName = "mistral-ocr-4-0",
            FoundryEndpoint = new Uri("https://foundry.example.test/"),
            FoundryApiKey = "foundry-key"
        };

        settings.Validate();
    }

    [Fact]
    public void Validate_WithoutDeployment_DoesNotRequireCredentials()
    {
        new AISettings().Validate();
        new AISettings { DeploymentName = " ", ImageDeploymentName = " " }.Validate();
    }

    [Theory]
    [InlineData("chat", null, null)]
    [InlineData(null, "image", null)]
    [InlineData(null, null, "embedding")]
    public void Validate_DeploymentWithoutEndpoint_Throws(
        string? chat, string? image, string? embedding)
    {
        var settings = new AISettings
        {
            DeploymentName = chat,
            ImageDeploymentName = image,
            EmbeddingDeploymentName = embedding,
            ApiKey = "test-key"
        };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);
        Assert.Contains("AI:Endpoint", exception.Message);
    }

    [Theory]
    [InlineData("chat", null, null)]
    [InlineData(null, "image", null)]
    [InlineData(null, null, "embedding")]
    public void Validate_DeploymentWithoutApiKey_Throws(
        string? chat, string? image, string? embedding)
    {
        var settings = new AISettings
        {
            DeploymentName = chat,
            ImageDeploymentName = image,
            EmbeddingDeploymentName = embedding,
            Endpoint = new Uri("https://example.test/openai/v1/")
        };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);
        Assert.Contains("AI:ApiKey", exception.Message);
    }

    [Fact]
    public void Validate_FoundryEndpointWithoutKey_Throws()
    {
        var settings = new AISettings
        {
            FoundryEndpoint = new Uri("https://foundry.example.test/")
        };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);
        Assert.Contains("AI:FoundryApiKey", exception.Message);
    }

    [Fact]
    public void Validate_FoundryKeyWithoutEndpoint_Throws()
    {
        var settings = new AISettings
        {
            FoundryApiKey = "foundry-key"
        };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);
        Assert.Contains("AI:FoundryEndpoint", exception.Message);
    }

    [Fact]
    public void Validate_DocumentDeploymentWithoutFoundryCredentials_Throws()
    {
        var settings = new AISettings
        {
            DocumentDeploymentName = "mistral-ocr-4-0"
        };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);
        Assert.Contains("AI:FoundryEndpoint", exception.Message);
    }

    [Fact]
    public void Validate_FoundryCredentialsWithoutDocumentDeployment_Throws()
    {
        var settings = new AISettings
        {
            FoundryEndpoint = new Uri("https://foundry.example.test/"),
            FoundryApiKey = "foundry-key"
        };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);
        Assert.Contains("AI:DocumentDeploymentName", exception.Message);
    }
}
