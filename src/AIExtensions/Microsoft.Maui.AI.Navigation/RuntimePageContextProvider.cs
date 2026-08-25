using Microsoft.Maui.AI.Indexer;

namespace Microsoft.Maui.AI.Navigation;

/// <summary>Provides semantic context for the page currently visible to the user.</summary>
public interface ICurrentPageContextProvider
{
    Task<CurrentPageSnapshot?> CaptureAsync(
        CurrentPageSnapshotOptions? options = null);
}

/// <summary>
/// Uses <see cref="RuntimePageIndexer"/> to capture the currently presented MAUI page.
/// </summary>
public sealed class RuntimePageContextProvider : ICurrentPageContextProvider
{
    public Task<CurrentPageSnapshot?> CaptureAsync(
        CurrentPageSnapshotOptions? options = null)
        => RuntimePageIndexer.CaptureCurrentAsync(options);
}
