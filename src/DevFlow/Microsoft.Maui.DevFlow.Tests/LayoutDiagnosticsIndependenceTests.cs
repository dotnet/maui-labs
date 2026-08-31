using System.Reflection;
using Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The layout engine must stand alone. If any layout type or file reaches for a device companion,
/// a device UI snapshot, or a device route, an app-scoped structural scan silently acquires a
/// dependency on emulator/simulator capture, and every host that advertises a layout scan starts
/// promising evidence the engine cannot produce on a machine with no device host.
/// <para>
/// The device layer may still correlate a finished report with what the device saw — that is what
/// <c>systemEvidence</c> is for — but it does so from the device side, in
/// <c>Microsoft.Maui.Cli/DevFlow/Devices</c> and the broker's composite route. Keeping the
/// producer out of the engine directories is what makes the correlation additive and optional
/// rather than a hidden requirement, so these assertions fail on the dependency rather than at run
/// time in front of a user.
/// </para>
/// </summary>
public sealed class LayoutDiagnosticsIndependenceTests
{
    private static readonly string[] DeviceCompanionSymbols =
    [
        "DeviceUiSnapshot",
        "DeviceUiElement",
        "IDeviceCompanion",
        "DeviceCompanionManager",
        "DeviceCompanionHost",
        "MobileCanvas",
        "/api/layout-diagnostics/composite",
        "/api/devices",
        "/api/device/host",
    ];

    /// <summary>
    /// The analyzer, snapshot collector, and platform-evidence contracts are the whole layout
    /// engine. None of them may reference a device assembly: doing so would make an app-scoped
    /// structural scan depend on emulator/simulator capture that only the later layer provides.
    /// </summary>
    [Fact]
    public void LayoutAnalysisTypes_DoNotReferenceTheDeviceLayer()
    {
        Type[] layoutTypes =
        [
            typeof(LayoutDiagnosticsAnalyzer),
            typeof(LayoutSnapshotCollector),
            typeof(LayoutPlatformEvidence),
            typeof(LayoutInspectionRequest),
            typeof(LayoutInspectionResult),
        ];

        foreach (var type in layoutTypes)
        {
            foreach (var reference in type.Assembly.GetReferencedAssemblies())
            {
                Assert.False(
                    reference.Name?.Contains("DevFlow.Devices", StringComparison.OrdinalIgnoreCase) == true,
                    $"{type.Assembly.GetName().Name} references {reference.Name}, so layout diagnostics " +
                    "would depend on the Mobile Device Canvas device layer.");
            }
        }
    }

    /// <summary>
    /// Reflection cannot see a route string or a using directive that never resolves to a type, so
    /// the shipped layout sources are read directly. A composite broker route or a device capture
    /// call named here would move the correlation inside the engine, where it could no longer be
    /// skipped on a machine with no device host.
    /// </summary>
    [Fact]
    public void LayoutSourceFiles_NameNoDeviceCompanionOrCanvasRoute()
    {
        var offenders = new List<string>();
        foreach (var file in LayoutSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var symbol in DeviceCompanionSymbols)
            {
                if (text.Contains(symbol, StringComparison.Ordinal))
                    offenders.Add($"{file} names '{symbol}'");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The report type keeps a <c>SystemEvidence</c> slot, and the device layer's composite
    /// producer is the only thing allowed to fill it. The analyzer never may: a non-null value
    /// straight out of analysis would tell a reader that a keyboard, dialog, or share sheet was
    /// ruled in or out using evidence the analyzer never captured.
    /// </summary>
    [Fact]
    public void AnalyzedReport_LeavesSystemEvidenceUnpopulated()
    {
        var report = LayoutDiagnosticsAnalyzer.Analyze(
            [],
            new LayoutDiagnosticsScope(),
            "test",
            DateTime.UtcNow,
            new LayoutInspectionRequest());

        Assert.Null(report.SystemEvidence);
    }

    private static IEnumerable<string> LayoutSourceFiles()
    {
        var root = RepositoryRoot();
        string[] directories =
        [
            Path.Combine(root, "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent.Core", "LayoutDiagnostics"),
            Path.Combine(root, "src", "DevFlow", "Microsoft.Maui.DevFlow.Driver", "Diagnostics"),
            Path.Combine(root, "src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Diagnostics"),
        ];

        foreach (var directory in directories)
        {
            Assert.True(Directory.Exists(directory), $"{directory} is missing.");
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
                yield return file;
        }

        string[] files =
        [
            Path.Combine(
                root, "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent.Core",
                "DevFlowAgentService.LayoutDiagnostics.cs"),
            Path.Combine(root, "src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "LayoutDiagnosticsPolicy.cs"),
            Path.Combine(
                root, "src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Mcp", "Tools",
                "LayoutDiagnosticsTool.cs"),
        ];

        foreach (var file in files)
        {
            Assert.True(File.Exists(file), $"{file} is missing.");
            yield return file;
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
