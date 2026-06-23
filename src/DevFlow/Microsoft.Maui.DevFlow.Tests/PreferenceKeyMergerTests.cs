using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class PreferenceKeyMergerTests
{
    [Fact]
    public void Merge_NoNativeSupport_ReturnsRegistryOnlyAndIncomplete()
    {
        var result = PreferenceKeyMerger.Merge(
            registryKeys: new[] { "b", "a" },
            nativeKeys: null);

        Assert.Equal(PreferenceKeyMerger.SourceRegistry, result.Source);
        Assert.False(result.Complete);
        Assert.Equal(new[] { "a", "b" }, result.Keys);
    }

    [Fact]
    public void Merge_NativeSupported_ReturnsNativeAndComplete()
    {
        var result = PreferenceKeyMerger.Merge(
            registryKeys: Array.Empty<string>(),
            nativeKeys: new[] { "session", "theme" });

        Assert.Equal(PreferenceKeyMerger.SourceNative, result.Source);
        Assert.True(result.Complete);
        Assert.Equal(new[] { "session", "theme" }, result.Keys);
    }

    [Fact]
    public void Merge_NativeSupported_UnionsRegistryKeysWithoutRegression()
    {
        // A key DevFlow tracked but that the native enumeration somehow missed
        // must still be listed (no regression vs. registry-only behavior).
        var result = PreferenceKeyMerger.Merge(
            registryKeys: new[] { "tracked_only" },
            nativeKeys: new[] { "app_written" });

        Assert.True(result.Complete);
        Assert.Equal(new[] { "app_written", "tracked_only" }, result.Keys);
    }

    [Fact]
    public void Merge_Deduplicates_OverlappingKeys()
    {
        var result = PreferenceKeyMerger.Merge(
            registryKeys: new[] { "shared", "reg" },
            nativeKeys: new[] { "shared", "nat" });

        Assert.Equal(new[] { "nat", "reg", "shared" }, result.Keys);
    }

    [Fact]
    public void Merge_ExcludesInternalKeys()
    {
        const string registryKey = "__devflow_known_keys";
        var result = PreferenceKeyMerger.Merge(
            registryKeys: new[] { "user_key" },
            nativeKeys: new[] { "user_key", registryKey },
            excludeKeys: new[] { registryKey });

        Assert.DoesNotContain(registryKey, result.Keys);
        Assert.Equal(new[] { "user_key" }, result.Keys);
    }

    [Fact]
    public void Merge_NativeEmpty_IsCompleteWithNoKeys()
    {
        var result = PreferenceKeyMerger.Merge(
            registryKeys: Array.Empty<string>(),
            nativeKeys: Array.Empty<string>());

        Assert.Equal(PreferenceKeyMerger.SourceNative, result.Source);
        Assert.True(result.Complete);
        Assert.Empty(result.Keys);
    }

    [Fact]
    public void Merge_IgnoresNullAndEmptyKeys()
    {
        var result = PreferenceKeyMerger.Merge(
            registryKeys: new[] { "", "a" },
            nativeKeys: new[] { "b", "", null! });

        Assert.Equal(new[] { "a", "b" }, result.Keys);
    }

    [Fact]
    public void Merge_KeysAreOrdinallySorted()
    {
        var result = PreferenceKeyMerger.Merge(
            registryKeys: new[] { "Zebra", "apple", "Banana" },
            nativeKeys: null);

        // Ordinal: uppercase letters sort before lowercase.
        Assert.Equal(new[] { "Banana", "Zebra", "apple" }, result.Keys);
    }
}
