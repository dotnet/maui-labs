using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Attributes.Tests;

public class AIFunctionCancellationTests
{
    [Fact]
    public async Task Cancellation_token_is_bound_correctly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CancellableToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = CancellableToolContext.Default.Tools.First(t => t.Name == "cancellable_tool") as AIFunction;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["input"] = "hello" }) { Services = provider };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool!.InvokeAsync(args, cts.Token).AsTask());
    }

    [Fact]
    public async Task Non_cancelled_token_completes_successfully()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CancellableToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = CancellableToolContext.Default.Tools.First(t => t.Name == "cancellable_tool") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["input"] = "hello" }) { Services = provider };

        var result = await tool!.InvokeAsync(args);

        Assert.Equal("done: hello", result?.ToString());
    }
}
