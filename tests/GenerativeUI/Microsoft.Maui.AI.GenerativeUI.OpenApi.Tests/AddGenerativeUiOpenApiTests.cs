using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi.Tests;

public sealed class AddGenerativeUiOpenApiTests
{
    [Fact]
    public void Registers_the_resolvable_server_api_stack()
    {
        var services = new ServiceCollection();
        services.AddGenerativeUiOpenApi(options => options.BaseAddress = new Uri("https://api.garden.example"));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<GenerativeOpenApiOptions>());
        Assert.NotNull(provider.GetRequiredService<OpenApiCache>());
        Assert.NotNull(provider.GetRequiredService<ApiInvoker>());
        Assert.NotNull(provider.GetRequiredService<OpenApiExplorerTools>());
    }

    [Fact]
    public void Requires_a_base_address()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddGenerativeUiOpenApi(_ => { }));
    }

    [Fact]
    public async Task OpenApiCache_FromSpec_returns_the_spec_without_fetching()
    {
        var spec = OpenApiReducer.Reduce(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "garden.openapi.json")));
        var cache = OpenApiCache.FromSpec(spec);

        Assert.Same(spec, cache.Current);
        Assert.Same(spec, await cache.GetSpecAsync());
    }
}
