using Microsoft.Maui.Cli.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The composite producer is the only thing that may fill <c>layout.json.systemEvidence</c>.
/// <para>
/// Its whole value is that a reader can trust it. "The keyboard was up" is a cause for a
/// not-hittable finding only if the keyboard was up <em>at the moment those findings were
/// measured</em>, on <em>that</em> device, from <em>that</em> app instance. Every assertion here
/// pins one of the ways that can silently stop being true, because the failure mode is not a
/// crash — it is a confident, wrong explanation.
/// </para>
/// </summary>
public sealed class LayoutSystemEvidenceComposerTests
{
    private const string DeviceId = "emulator-5554";
    private const string AppPackageId = "com.example.app";

    [Fact]
    public void Unavailable_StatesTheReasonAndCarriesNoEvidence()
    {
        var evidence = LayoutSystemEvidenceComposer.Unavailable("No device host is available.");

        Assert.Equal("unavailable", evidence.Status);
        Assert.False(evidence.GeometryStable);
        Assert.Empty(evidence.Elements);
        Assert.Contains("No device host is available.", evidence.Limitations);
        Assert.Null(evidence.DeviceId);
        Assert.Null(evidence.CapturedAt);
    }

    [Fact]
    public void Compose_WithACorrelatedCapture_ReportsCompleteAndKeepsExternalElements()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var report = Report(capturedAt, "tree-1");
        var evidence = LayoutSystemEvidenceComposer.Compose(
            report,
            Report(capturedAt, "tree-1"),
            DeviceId,
            Display(),
            Snapshot(capturedAt, keyboardVisible: true, elements:
            [
                SystemElement("keyboard", role: "keyboard"),
                AppElement("app-button"),
            ]),
            screenshot: [1, 2, 3],
            AppPackageId,
            identityStable: true);

        Assert.Equal("complete", evidence.Status);
        Assert.Equal(DeviceId, evidence.DeviceId);
        Assert.True(evidence.GeometryStable);
        Assert.True(evidence.KeyboardVisible);
        Assert.True(evidence.ScreenshotCaptured);
        Assert.NotNull(evidence.ScreenshotDigest);
        Assert.Equal(0, evidence.CaptureSkewMs);
        var element = Assert.Single(evidence.Elements);
        Assert.Equal("keyboard", element.Id);
        Assert.Equal(200, element.Bounds!.Width);
    }

    /// <summary>
    /// A screenshot is metadata, not proof. The caveat has to travel with every accepted report so
    /// nobody reads a digest as evidence that a pixel was covered.
    /// </summary>
    [Fact]
    public void Compose_AlwaysCarriesTheScreenshotCaveat()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var evidence = LayoutSystemEvidenceComposer.Compose(
            Report(capturedAt, "tree-1"),
            Report(capturedAt, "tree-1"),
            DeviceId,
            Display(),
            Snapshot(capturedAt),
            screenshot: [1],
            AppPackageId,
            identityStable: true);

        Assert.Contains(
            evidence.Limitations,
            item => item.Contains("do not independently prove visual occlusion", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_WithNoHierarchy_IsUnavailableRatherThanEmpty()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var evidence = LayoutSystemEvidenceComposer.Compose(
            Report(capturedAt, "tree-1"),
            Report(capturedAt, "tree-1"),
            DeviceId,
            Display(),
            snapshot: null,
            screenshot: null,
            AppPackageId,
            identityStable: true);

        Assert.Equal("unavailable", evidence.Status);
        Assert.Empty(evidence.Elements);
        Assert.False(evidence.ScreenshotCaptured);
        Assert.Contains(
            evidence.Limitations,
            item => item.Contains("did not provide a UI hierarchy", StringComparison.Ordinal));
    }

    /// <summary>
    /// A tree that moved between the two scans means the findings and the device hierarchy describe
    /// different screens. Reporting the elements anyway would invite a reader to align them.
    /// </summary>
    [Fact]
    public void Compose_WhenTheTreeMovedDuringCapture_IsIncompleteAndDropsElements()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var evidence = LayoutSystemEvidenceComposer.Compose(
            Report(capturedAt, "tree-1"),
            Report(capturedAt, "tree-2"),
            DeviceId,
            Display(),
            Snapshot(capturedAt, elements: [SystemElement("dialog", role: "dialog")]),
            screenshot: [1],
            AppPackageId,
            identityStable: true);

        Assert.Equal("incomplete", evidence.Status);
        Assert.False(evidence.GeometryStable);
        Assert.Empty(evidence.Elements);
        Assert.Contains(
            evidence.Limitations,
            item => item.Contains("app geometry changed", StringComparison.Ordinal));
    }

    /// <summary>
    /// Without a second scan there is no evidence the app held still, so "stable" is unprovable
    /// rather than assumed.
    /// </summary>
    [Fact]
    public void Compose_WithoutARecheck_RefusesToClaimStability()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var evidence = LayoutSystemEvidenceComposer.Compose(
            Report(capturedAt, "tree-1"),
            recheck: null,
            DeviceId,
            Display(),
            Snapshot(capturedAt),
            screenshot: null,
            AppPackageId,
            identityStable: true);

        Assert.Equal("incomplete", evidence.Status);
        Assert.False(evidence.GeometryStable);
    }

    [Fact]
    public void Compose_WhenTheAgentInstanceChanged_IsIncomplete()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var evidence = LayoutSystemEvidenceComposer.Compose(
            Report(capturedAt, "tree-1"),
            Report(capturedAt, "tree-1"),
            DeviceId,
            Display(),
            Snapshot(capturedAt),
            screenshot: null,
            AppPackageId,
            identityStable: false);

        Assert.Equal("incomplete", evidence.Status);
        Assert.Contains(
            evidence.Limitations,
            item => item.Contains("agent instance changed", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_WhenTheCapturesAreTooFarApart_IsIncompleteAndReportsTheSkew()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var evidence = LayoutSystemEvidenceComposer.Compose(
            Report(capturedAt, "tree-1"),
            Report(capturedAt, "tree-1"),
            DeviceId,
            Display(),
            Snapshot(capturedAt.AddSeconds(5)),
            screenshot: null,
            AppPackageId,
            identityStable: true);

        Assert.Equal("incomplete", evidence.Status);
        Assert.Equal(5000, evidence.CaptureSkewMs);
        Assert.Contains(
            evidence.Limitations,
            item => item.Contains("ms from the app snapshot", StringComparison.Ordinal));
    }

    /// <summary>
    /// A hierarchy from another device is the most dangerous input here: it looks complete and
    /// describes a screen that was never measured.
    /// </summary>
    [Fact]
    public void Compose_WhenTheHierarchyCameFromAnotherDevice_IsIncomplete()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var evidence = LayoutSystemEvidenceComposer.Compose(
            Report(capturedAt, "tree-1"),
            Report(capturedAt, "tree-1"),
            DeviceId,
            Display(),
            Snapshot(capturedAt) with { DeviceId = "emulator-5556" },
            screenshot: null,
            AppPackageId,
            identityStable: true);

        Assert.Equal("incomplete", evidence.Status);
        Assert.Empty(evidence.Elements);
        Assert.Contains(
            evidence.Limitations,
            item => item.Contains("orientation, or display scale", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_WhenTheDeviceRotatedDuringCapture_IsIncomplete()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var evidence = LayoutSystemEvidenceComposer.Compose(
            Report(capturedAt, "tree-1"),
            Report(capturedAt, "tree-1"),
            DeviceId,
            Display(),
            Snapshot(capturedAt) with { Orientation = "landscape" },
            screenshot: null,
            AppPackageId,
            identityStable: true);

        Assert.Equal("incomplete", evidence.Status);
        Assert.Empty(evidence.Elements);
    }

    /// <summary>
    /// Without a package identity the producer cannot tell an app element from an intruder, so an
    /// unattributed element is never promoted to external and the gap is stated.
    /// </summary>
    [Fact]
    public void Compose_WithoutAPackageIdentity_ReportsOnlyHostMarkedSystemElements()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var evidence = LayoutSystemEvidenceComposer.Compose(
            Report(capturedAt, "tree-1"),
            Report(capturedAt, "tree-1"),
            DeviceId,
            Display(),
            Snapshot(capturedAt, elements:
            [
                SystemElement("sheet", role: "sheet"),
                AppElement("other-app") with { PackageId = "com.other.app" },
            ]),
            screenshot: null,
            appPackageId: null,
            identityStable: true);

        Assert.Equal("complete", evidence.Status);
        Assert.Equal("sheet", Assert.Single(evidence.Elements).Id);
        Assert.Contains(
            evidence.Limitations,
            item => item.Contains("package identity was unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_WhenAnAppSnapshotHasNoTimestamp_IsIncomplete()
    {
        var report = Report(DateTimeOffset.UtcNow, "tree-1");
        report.Snapshot.CapturedAt = "";

        var evidence = LayoutSystemEvidenceComposer.Compose(
            report,
            Report(DateTimeOffset.UtcNow, "tree-1"),
            DeviceId,
            Display(),
            Snapshot(DateTimeOffset.UtcNow),
            screenshot: null,
            AppPackageId,
            identityStable: true);

        Assert.Equal("incomplete", evidence.Status);
        Assert.Null(evidence.CaptureSkewMs);
        Assert.Contains(
            evidence.Limitations,
            item => item.Contains("usable capture timestamp", StringComparison.Ordinal));
    }

    /// <summary>
    /// A hostile or broken device host must not be able to grow the report without bound.
    /// </summary>
    [Fact]
    public void Compose_BoundsElementsAndLimitations()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var elements = Enumerable.Range(0, LayoutSystemEvidenceComposer.MaxElements + 40)
            .Select(index => SystemElement($"system-{index}"))
            .ToArray();
        var limitations = Enumerable.Range(0, LayoutSystemEvidenceComposer.MaxLimitations + 40)
            .Select(index => $"{index}:" + new string('x', 900))
            .ToArray();

        var evidence = LayoutSystemEvidenceComposer.Compose(
            Report(capturedAt, "tree-1"),
            Report(capturedAt, "tree-1"),
            DeviceId,
            Display(),
            Snapshot(capturedAt, elements: elements) with { Limitations = limitations },
            screenshot: null,
            AppPackageId,
            identityStable: true);

        Assert.Equal(LayoutSystemEvidenceComposer.MaxElements, evidence.Elements.Count);
        Assert.Equal(LayoutSystemEvidenceComposer.MaxLimitations, evidence.Limitations.Count);
        Assert.All(evidence.Limitations, item => Assert.True(item.Length <= 512));
    }

    /// <summary>
    /// The producer reports; it never rewrites. Findings, the summary, and the agent's diagnostics
    /// revision stay exactly as the agent computed them, because the agent is the only component
    /// holding the reviewed suppression policy for the scan.
    /// </summary>
    [Fact]
    public void Compose_DoesNotMutateTheReport()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var report = Report(capturedAt, "tree-1");
        report.Findings.Add(new LayoutFinding { Id = "existing", RuleId = "layout.rule" });
        report.Snapshot.DiagnosticsRevision = "agent-revision";

        LayoutSystemEvidenceComposer.Compose(
            report,
            Report(capturedAt, "tree-1"),
            DeviceId,
            Display(),
            Snapshot(capturedAt, keyboardVisible: true, elements: [SystemElement("keyboard", role: "keyboard")]),
            screenshot: [1],
            AppPackageId,
            identityStable: true);

        Assert.Equal("existing", Assert.Single(report.Findings).Id);
        Assert.Equal("agent-revision", report.Snapshot.DiagnosticsRevision);
        Assert.Null(report.SystemEvidence);
    }

    private static LayoutDiagnosticsReport Report(DateTimeOffset capturedAt, string treeRevision)
        => new()
        {
            Snapshot = new LayoutSnapshotInfo
            {
                CapturedAt = capturedAt.ToUniversalTime().ToString("O"),
                TreeRevision = treeRevision,
            },
        };

    private static DisplayGeometry Display()
        => new() { Orientation = "portrait", Scale = 2.0 };

    private static DeviceUiSnapshot Snapshot(
        DateTimeOffset capturedAt,
        bool? keyboardVisible = null,
        DeviceUiElement[]? elements = null)
        => new()
        {
            DeviceId = DeviceId,
            CapturedAt = capturedAt,
            Orientation = "portrait",
            Scale = 2.0,
            ForegroundOwner = AppPackageId,
            KeyboardVisible = keyboardVisible,
            Elements = elements ?? [],
        };

    private static DeviceUiElement SystemElement(string id, string? role = null)
        => new()
        {
            Id = id,
            Role = role,
            Type = "System",
            IsSystem = true,
            Interactive = true,
            Bounds = new DeviceRect(0, 400, 200, 100),
        };

    private static DeviceUiElement AppElement(string id)
        => new()
        {
            Id = id,
            Type = "Button",
            PackageId = AppPackageId,
            Interactive = true,
        };
}
