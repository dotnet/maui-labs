using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

/// <summary>
/// The device evidence kind is the manifest's own account of what produced the run. The producer
/// used to invent "unknown" when nothing identified the device, and its own validator then refused
/// that value, so a manifest could be published carrying a kind no consumer credits while the
/// producer reported it as a value it had chosen.
/// </summary>
public sealed class FlowPilotArtifactManifestDeviceEvidenceTests : IDisposable
{
    readonly string _root = Path.Combine(
        Path.GetTempPath(), "devflow-device-evidence-tests", Guid.NewGuid().ToString("N"));

    public FlowPilotArtifactManifestDeviceEvidenceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("emulator")]
    [InlineData("physical-device")]
    [InlineData("real-device")]
    [InlineData("desktop-host")]
    public void AcceptedKind_IsUsedAsSupplied(string kind)
    {
        var manifest = Create(deviceEvidenceKind: kind);

        Assert.Equal(kind, manifest.Platform.DeviceEvidence.Kind);
        Assert.DoesNotContain(
            manifest.Omissions,
            omission => string.Equals(omission.Kind, "device-evidence", StringComparison.Ordinal));
        Assert.DoesNotContain(
            manifest.Validate().Errors,
            error => error.Contains("deviceEvidence.kind", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("simulator")]
    public void UnaccceptedKind_IsNeverNormalizedIntoUnknown(string? configured)
    {
        var manifest = Create(deviceEvidenceKind: configured);

        Assert.Null(manifest.Platform.DeviceEvidence.Kind);
        Assert.Contains(
            manifest.Omissions,
            omission => string.Equals(omission.Kind, "device-evidence", StringComparison.Ordinal));
        Assert.Contains(
            manifest.Validate().Errors,
            error => error.Contains("deviceEvidence.kind is required", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAvdName_IsTheOnlyEvidenceThatInfersAnEmulator()
    {
        var inferred = Create(deviceEvidenceKind: null, androidAvdName: "devflow-tests-api35");

        Assert.Equal("emulator", inferred.Platform.DeviceEvidence.Kind);
        Assert.DoesNotContain(
            inferred.Omissions,
            omission => string.Equals(omission.Kind, "device-evidence", StringComparison.Ordinal));
        Assert.Contains("emulator", inferred.Platform.DeviceProfile ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RealDevice_IsNeverClaimedWithoutAPhysicalKind()
    {
        var manifest = Create(deviceEvidenceKind: "emulator", realDevice: true);

        Assert.False(manifest.Platform.DeviceEvidence.RealDevice);
        Assert.DoesNotContain(
            manifest.Validate().Errors,
            error => error.Contains("realDevice requires", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    FlowPilotArtifactManifest Create(
        string? deviceEvidenceKind,
        string? androidAvdName = null,
        bool? realDevice = null)
        => FlowPilotArtifactManifest.Create(new FlowPilotManifestOptions
        {
            ArtifactRoot = Path.Combine(_root, "artifacts"),
            RepositoryRoot = _root,
            Platform = "android",
            PackageId = "com.companyname.mauitodo",
            RepositoryCommit = "0123456789abcdef0123456789abcdef01234567",
            WorkflowRunId = "device-evidence-tests",
            DeviceEvidenceKind = deviceEvidenceKind,
            AndroidAvdName = androidAvdName,
            RealDevice = realDevice,
        });
}
