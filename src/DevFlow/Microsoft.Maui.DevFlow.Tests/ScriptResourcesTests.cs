using Microsoft.Maui.DevFlow.Blazor;

namespace Microsoft.Maui.DevFlow.Tests;

public class ScriptResourcesTests
{
    [Fact]
    public async Task Load_ConcurrentColdLoads_PreserveAllScripts()
    {
        const string prefix = "Microsoft.Maui.DevFlow.Blazor.Resources.Scripts.";
        var assembly = typeof(ScriptResources).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(resources);

        using var start = new Barrier(resources.Length);
        var loads = resources.Select(resource => Task.Factory.StartNew(() =>
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var expected = reader.ReadToEnd();

            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(30)));
            for (var iteration = 0; iteration < 100; iteration++)
                Assert.Equal(expected, ScriptResources.Load(resource[prefix.Length..]));
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        await Task.WhenAll(loads);

        foreach (var resource in resources)
            Assert.False(string.IsNullOrWhiteSpace(ScriptResources.Load(resource[prefix.Length..])));
    }

    [Fact]
    public void Load_MissingResource_ThrowsActionableError()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ScriptResources.Load("missing-script.js"));

        Assert.Contains("Embedded resource not found", error.Message);
        Assert.Contains("missing-script.js", error.Message);
    }
}
