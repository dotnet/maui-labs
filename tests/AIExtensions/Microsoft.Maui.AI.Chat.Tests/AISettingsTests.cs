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
                ["AI:EmbeddingDeploymentName"] = "embedding"
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
}
