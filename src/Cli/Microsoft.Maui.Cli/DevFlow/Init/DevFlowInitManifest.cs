using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.DevFlow.Init;

internal sealed class DevFlowInitManifest
{
    public int SchemaVersion { get; set; }
    public string ManifestVersion { get; set; } = "";
    public DevFlowInitPackageSet Packages { get; set; } = new();
    public List<DevFlowAiHostManifest> Hosts { get; set; } = [];
}

internal sealed class DevFlowInitPackageSet
{
    public DevFlowNuGetPackageManifest Agent { get; set; } = new();
    public DevFlowNuGetPackageManifest Blazor { get; set; } = new();
}

internal sealed class DevFlowNuGetPackageManifest
{
    public string PackageId { get; set; } = "";
    public string Version { get; set; } = "";
}

internal sealed class DevFlowAiHostManifest
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DevFlowAiHostDetectionManifest Detect { get; set; } = new();
    public List<DevFlowMarketplaceInstallManifest> MarketplaceInstalls { get; set; } = [];
    public List<DevFlowRepoLocalFallbackManifest> RepoLocalFallbacks { get; set; } = [];
    public DevFlowAiHostVerifyManifest Verify { get; set; } = new();
}

internal sealed class DevFlowAiHostDetectionManifest
{
    public List<string> Executables { get; set; } = [];
    public List<string> RepoMarkers { get; set; } = [];
    public List<string> ConfigMarkers { get; set; } = [];
}

internal sealed class DevFlowMarketplaceInstallManifest
{
    public string MarketplaceId { get; set; } = "";
    public string PluginId { get; set; } = "";
    public string DesiredVersion { get; set; } = "";
    public string InstallStrategy { get; set; } = "";
    public string UpdatePolicy { get; set; } = "";
    public List<string> ManualSteps { get; set; } = [];
}

internal sealed class DevFlowRepoLocalFallbackManifest
{
    public string SourceRepo { get; set; } = "";
    public string SourceRepoUrl { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string DesiredRef { get; set; } = "";
    public string TargetPathTemplate { get; set; } = "";
    public string SyncMetadataFileName { get; set; } = ".skill-version";
}

internal sealed class DevFlowAiHostVerifyManifest
{
    public List<string> ManualSteps { get; set; } = [];
}

internal static class DevFlowInitManifestLoader
{
    static readonly Lazy<DevFlowInitManifest> s_manifest = new(LoadCore);

    public static DevFlowInitManifest Load() => s_manifest.Value;

    static DevFlowInitManifest LoadCore()
    {
        var assembly = typeof(DevFlowInitManifestLoader).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("devflow-init-manifest.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new InvalidOperationException("Could not find embedded DevFlow init manifest.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded manifest resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var manifest = JsonSerializer.Deserialize(
            json,
            DevFlowInitManifestJsonContext.Default.DevFlowInitManifest);

        return manifest ?? throw new InvalidOperationException("Could not deserialize DevFlow init manifest.");
    }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(DevFlowInitManifest))]
internal sealed partial class DevFlowInitManifestJsonContext : JsonSerializerContext
{
}
