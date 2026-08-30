using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Cli.DevFlow.Evidence;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using Microsoft.Maui.Dispatching;
using CoreLayoutFormat = Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics.LayoutDiagnosticsFormat;
using CoreLayoutRules = Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics.LayoutDiagnosticRules;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// End-to-end coverage for the layout diagnostics endpoint, the typed driver API, and the
/// walker's runtime element map.
/// </summary>
public class LayoutDiagnosticsAgentTests
{
    [Fact]
    public async Task Endpoint_ReturnsAVersionedReportWithCoverageAndLimitations()
    {
        var label = new Label { AutomationId = "Title", Text = "secret label text" };
        using var harness = await LayoutHarness.CreateAsync(label);

        var report = await harness.Client.GetLayoutDiagnosticsAsync();

        Assert.NotNull(report);
        Assert.Equal(CoreLayoutFormat.SchemaVersion, report!.SchemaVersion);
        Assert.Equal(CoreLayoutFormat.RuleSetVersion, report.RuleSetVersion);
        Assert.True(report.Scope.ElementsExamined > 0);
        Assert.Equal(CoreLayoutRules.Managed.Count, report.Coverage.Rules.Count);
        Assert.NotEmpty(report.Coverage.Limitations);
        Assert.Contains("Element Text/Value content", report.Coverage.NeverCaptured);
    }

    [Fact]
    public async Task Endpoint_NeverReturnsElementTextOrValues()
    {
        var label = new Label { AutomationId = "Title", Text = "secret label text" };
        var entry = new Entry { AutomationId = "Email", Text = "alice@example.com" };
        using var harness = await LayoutHarness.CreateAsync(label, entry);

        var raw = await harness.GetRawAsync("/api/v1/ui/diagnostics/layout");

        Assert.DoesNotContain("secret label text", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@example.com", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Endpoint_ScopesToTheRequestedElementSubtree()
    {
        var label = new Label { AutomationId = "Title" };
        var entry = new Entry { AutomationId = "Email" };
        using var harness = await LayoutHarness.CreateAsync(label, entry);

        var full = await harness.Client.GetLayoutDiagnosticsAsync();
        var scoped = await harness.Client.GetLayoutDiagnosticsAsync(elementId: "Title");

        Assert.NotNull(full);
        Assert.NotNull(scoped);
        Assert.Equal("Title", scoped!.Scope.RootElementId);
        Assert.Equal(1, scoped.Scope.ElementsExamined);
        Assert.True(full!.Scope.ElementsExamined > scoped.Scope.ElementsExamined);
    }

    [Fact]
    public async Task Endpoint_ResolvesScopedRootBeforeApplyingElementBudget()
    {
        using var harness = await LayoutHarness.CreateAsync(
            new Label { AutomationId = "First" },
            new Label { AutomationId = "Second" },
            new Label { AutomationId = "Target" });

        var scoped = await harness.Client.GetLayoutDiagnosticsAsync(
            elementId: "Target",
            maxElements: 1);

        Assert.NotNull(scoped);
        Assert.Equal("Target", scoped!.Scope.RootElementId);
        Assert.Equal(1, scoped.Scope.ElementsExamined);
        Assert.False(scoped.Scope.Truncated);
    }

    [Fact]
    public async Task Endpoint_ReturnsNullForAnUnknownElement()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var report = await harness.Client.GetLayoutDiagnosticsAsync(elementId: "does-not-exist");

        Assert.Null(report);
    }

    [Fact]
    public async Task RichRequest_ReturnsTypedNotFoundForAnUnknownElement()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var error = await Assert.ThrowsAsync<LayoutDiagnosticsException>(() =>
            harness.Client.AnalyzeLayoutAsync(new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
            {
                Scope = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionScope
                {
                    RootElementId = "does-not-exist",
                },
            }));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(LayoutDiagnosticsErrorTypes.ElementNotFound, error.ErrorType);
    }

    [Fact]
    public async Task Endpoint_ClampsAndReportsTheElementBudget()
    {
        using var harness = await LayoutHarness.CreateAsync(
            new Label { AutomationId = "A" },
            new Label { AutomationId = "B" },
            new Label { AutomationId = "C" });

        var capped = await harness.Client.GetLayoutDiagnosticsAsync(maxElements: 1);
        var oversized = await harness.Client.GetLayoutDiagnosticsAsync(maxElements: 999_999);

        Assert.NotNull(capped);
        Assert.Equal(1, capped!.Scope.MaxElements);
        Assert.Equal(1, capped.Scope.ElementsExamined);
        Assert.True(capped.Scope.Truncated);
        Assert.Contains(capped.Coverage.Limitations, limitation =>
            limitation.Contains("element budget", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(oversized);
        Assert.Equal(CoreLayoutFormat.MaxElements, oversized!.Scope.MaxElements);
        Assert.False(oversized.Scope.Truncated);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5001, CoreLayoutFormat.MaxElements)]
    public async Task Endpoint_ClampsRawPostElementBudgets(int requested, int expected)
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var raw = await harness.PostRawAsync(
            "/api/v1/ui/diagnostics/layout",
            $$"""{"scope":{"mode":"allWindows"},"maxElements":{{requested}}}""");

        using var document = JsonDocument.Parse(raw);
        Assert.True(document.RootElement.TryGetProperty("scope", out var scope), raw);
        Assert.Equal(expected, scope.GetProperty("maxElements").GetInt32());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5001, CoreLayoutFormat.MaxElements)]
    public async Task Endpoint_ClampsRawGetElementBudgets(int requested, int expected)
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var raw = await harness.GetRawAsync($"/api/v1/ui/diagnostics/layout?maxElements={requested}");

        using var document = JsonDocument.Parse(raw);
        Assert.Equal(expected, document.RootElement.GetProperty("scope").GetProperty("maxElements").GetInt32());
    }

    [Fact]
    public async Task Endpoint_AcceptsAPostBodyForTheSameScan()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var raw = await harness.PostRawAsync(
            "/api/v1/ui/diagnostics/layout",
            """{"elementId":"Title","maxElements":10}""");

        using var document = JsonDocument.Parse(raw);
        var scope = document.RootElement.GetProperty("scope");
        Assert.Equal("Title", scope.GetProperty("rootElementId").GetString());
        Assert.Equal(10, scope.GetProperty("maxElements").GetInt32());
    }

    [Fact]
    public async Task Endpoint_AcceptsTheRichVersionedRequest()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var report = await harness.Client.AnalyzeLayoutAsync(
            new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
            {
                Scope = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionScope
                {
                    RootElementId = "Title",
                },
                Rules = [Microsoft.Maui.DevFlow.Driver.LayoutDiagnosticRules.VisibleZeroArea],
                IncludePasses = true,
            });

        Assert.NotNull(report);
        Assert.Equal(CoreLayoutFormat.SchemaVersion, report!.SchemaVersion);
        Assert.Equal("Title", report.Scope.RootElementId);
        Assert.Single(report.Coverage.Rules);
        Assert.NotEmpty(report.Snapshot.Id);
        Assert.NotEmpty(report.Snapshot.TreeRevision);
    }

    // ── privacy contract ─────────────────────────────────────────────────────────────────────
    //
    // This layer never reads element text or values, so `privacy.text` is not a capture knob: the
    // only honest value is "none". Anything else is refused rather than silently downgraded, and no
    // report carries a member that could hold text or a text length.

    [Theory]
    [InlineData("length")]
    [InlineData("full")]
    [InlineData("Full")]
    [InlineData("summary")]
    public async Task RichRequest_RejectsEveryTextCaptureMode(string mode)
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var error = await Assert.ThrowsAsync<LayoutDiagnosticsException>(() =>
            harness.Client.AnalyzeLayoutAsync(
                new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
                {
                    Privacy = new Microsoft.Maui.DevFlow.Driver.LayoutPrivacyOptions { Text = mode },
                }));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("layout-diagnostics-validation", error.ErrorType);
        Assert.Contains("privacy.text must be none", error.Message, StringComparison.Ordinal);
        Assert.Contains("never captures", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RawRequest_RejectsTextCaptureModesAndReturnsNoReport()
    {
        using var harness = await LayoutHarness.CreateAsync(
            new Label { AutomationId = "Title", Text = "secret label text" });

        var raw = await harness.PostRawAsync(
            "/api/v1/ui/diagnostics/layout",
            """{"privacy":{"text":"full"}}""");

        using var document = JsonDocument.Parse(raw);
        Assert.False(document.RootElement.TryGetProperty("findings", out _));
        Assert.DoesNotContain("secret label text", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RichRequest_AcceptsTheOnlyHonestPrivacyModeAndAlwaysPublishesNeverCaptured()
    {
        using var harness = await LayoutHarness.CreateAsync(
            new Label { AutomationId = "Title", Text = "secret label text" });

        var report = await harness.Client.AnalyzeLayoutAsync(
            new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
            {
                Scope = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionScope
                {
                    RootElementId = "Title",
                },
                Privacy = new Microsoft.Maui.DevFlow.Driver.LayoutPrivacyOptions { Text = "none" },
                IncludeEvidence = true,
                IncludePasses = true,
            });

        Assert.NotNull(report);
        Assert.Contains("Element Text/Value content", report!.Coverage.NeverCaptured);
    }

    [Fact]
    public void TextEvidence_HasNoMemberThatCouldCarryTextOrItsLength()
    {
        foreach (var type in new[]
                 {
                     typeof(Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics.LayoutTextEvidence),
                     typeof(Microsoft.Maui.DevFlow.Driver.LayoutTextEvidence),
                 })
        {
            var names = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .ToList();
            Assert.DoesNotContain("Text", names);
            Assert.DoesNotContain("TextLength", names);
            Assert.Contains("IsTruncated", names);
        }
    }

    /// <summary>
    /// Removing two response fields and redefining <c>suppressionKey</c> are exactly the changes
    /// the payload version exists to announce, so the version has to move with them.
    /// </summary>
    [Fact]
    public async Task RemovedTextFieldsAndTheRedefinedSuppressionKey_AdvancedThePayloadVersion()
    {
        Assert.Equal("2.1", CoreLayoutFormat.SchemaVersion);

        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });
        var report = await harness.Client.AnalyzeLayoutAsync(
            new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
            {
                Scope = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionScope
                {
                    RootElementId = "Title",
                },
            });

        Assert.Equal("2.1", report!.SchemaVersion);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("2.0")]
    [InlineData("2.1")]
    public async Task OlderRequestVersionsRemainAcceptedSoAnOlderDriverKeepsWorking(string schemaVersion)
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var report = await harness.Client.AnalyzeLayoutAsync(
            new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
            {
                SchemaVersion = schemaVersion,
                Scope = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionScope
                {
                    RootElementId = "Title",
                },
            });

        Assert.NotNull(report);
        Assert.Equal("2.1", report!.SchemaVersion);
    }

    /// <summary>
    /// The refusal an agent gives for an unknown request version is a cross-assembly contract, not
    /// an internal message. <c>maui_test_layout_diagnostics</c> classifies it by matching this
    /// exact shape — the restricted profile refuses to echo agent text, so the only way it can
    /// report "this app is too old for this payload" rather than an unclassifiable transport
    /// failure is by recognizing the message the agent actually emits.
    ///
    /// <para>Nothing links the two: the agent lives in <c>Agent.Core</c> and cannot reference the
    /// CLI, and the CLI cannot reference the agent, so no shared constant can span them without
    /// coupling an in-app assembly to a tool assembly. This test is the link. It drives the real
    /// endpoint, takes the real exception the real Driver builds from the real response, and hands
    /// that to the real classifier — so rewording the refusal in <c>Agent.Core</c>, changing its
    /// status code, or changing its reason fails here rather than silently degrading every
    /// restricted-profile scan against an older app to "target unavailable".</para>
    /// </summary>
    [Theory]
    [InlineData("3.0")]
    [InlineData("9.9")]
    [InlineData("2.2")]
    public async Task AnUnknownRequestVersionIsRefusedInTheShapeTheRestrictedToolClassifies(
        string unsupported)
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var refusal = await Assert.ThrowsAsync<LayoutDiagnosticsException>(() =>
            harness.Client.AnalyzeLayoutAsync(
                new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
                {
                    SchemaVersion = unsupported,
                }));

        // The wire shape, asserted exactly rather than by substring: this is the string the
        // classifier parses, and a reword is exactly the change that must not pass silently.
        Assert.Equal(400, refusal.StatusCode);
        Assert.Equal("layout-diagnostics-validation", refusal.ErrorType);
        Assert.Equal(
            $"schemaVersion must be '{CoreLayoutFormat.SchemaVersion}'.",
            refusal.Message);

        // The classification the restricted profile derives from it — from the real exception, not
        // a hand-written stand-in.
        var described = TestAgentDiscoveryTools.DescribeLayoutFailure(refusal);

        Assert.Equal(MauiTestAgentErrorCodes.UnsupportedOperation, described.Code);
        Assert.Equal(MauiTestAgentErrorCategories.Capability, described.Category);
        Assert.False(described.Retryable);
        Assert.Contains("payload version", described.Message!, StringComparison.Ordinal);
        Assert.Contains(
            $"schemaVersion '{CoreLayoutFormat.SchemaVersion}'",
            described.Message!,
            StringComparison.Ordinal);
        // The version the caller asked for is caller-supplied text and is never quoted back.
        Assert.DoesNotContain(unsupported, described.Message!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The neighbouring branch, from the same real endpoint: a scope element that is not in the
    /// tree is the caller's request being wrong about the live app, so it classifies as an invalid
    /// request rather than an unreachable target — and the agent's message, which carries the
    /// element id the caller supplied, is not echoed.
    /// </summary>
    [Fact]
    public async Task AVanishedScopeElementIsRefusedInTheShapeTheRestrictedToolClassifies()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var refusal = await Assert.ThrowsAsync<LayoutDiagnosticsException>(() =>
            harness.Client.AnalyzeLayoutAsync(
                new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
                {
                    Scope = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionScope
                    {
                        RootElementId = "el-that-left-the-tree",
                    },
                }));

        Assert.Equal(404, refusal.StatusCode);
        Assert.Equal(LayoutDiagnosticsErrorTypes.ElementNotFound, refusal.ErrorType);

        var described = TestAgentDiscoveryTools.DescribeLayoutFailure(refusal);

        Assert.Equal(MauiTestAgentErrorCodes.InvalidRequest, described.Code);
        Assert.Equal(MauiTestAgentErrorCategories.Validation, described.Category);
        Assert.False(described.Retryable);
        Assert.Contains("no longer exists", described.Message!, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "el-that-left-the-tree",
            described.Message!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrivacyOptions_AdvertiseExactlyOneAcceptedTextMode()    {
        Assert.Equal(new[] { "none" }, LayoutPrivacyTextModes.All);
        Assert.Equal(
            "none",
            new Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics.LayoutPrivacyOptions().Text);
        Assert.Equal("none", new Microsoft.Maui.DevFlow.Driver.LayoutPrivacyOptions().Text);
    }

    /// <summary>
    /// End-to-end proof that the pinned project root is what actually decides which suppressions a
    /// layout scan applies: the same live app, scanned three times through the shared coordinator
    /// that the MCP tool, the CLI, and evidence capture all use, differing only in which project
    /// root was pinned.
    /// </summary>
    [Fact]
    public async Task PinnedProjectRoot_DecidesWhichSuppressionsALayoutScanApplies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"devflow-evidence-policy-{Guid.NewGuid():N}");
        var app = Path.Combine(root, "app");
        var unrelated = Path.Combine(root, "unrelated");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(unrelated);
        try
        {
            using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

            var unsuppressed = await Microsoft.Maui.Cli.DevFlow.Diagnostics.LayoutDiagnosticsCoordinator.ScanAsync(
                harness.Client,
                elementId: "Title",
                policyStartPath: app);
            Assert.NotNull(unsuppressed);
            // The comparison is only meaningful if there is something a policy could suppress.
            var target = unsuppressed!.Findings.First(finding =>
                finding.Outcome is not ("pass" or "notApplicable") &&
                !string.IsNullOrEmpty(finding.SuppressionKey));
            Assert.Equal(0, unsuppressed.Summary.Suppressed);

            // The pinned project reviews that exact fingerprint; the other project reviews nothing.
            File.WriteAllText(
                Path.Combine(app, ".mauidevflow"),
                $$"""
                {
                  "layoutDiagnostics": {
                    "suppressions": [
                      { "fingerprint": "{{target.SuppressionKey}}", "reason": "Reviewed for this app" }
                    ]
                  }
                }
                """);

            var pinned = await Microsoft.Maui.Cli.DevFlow.Diagnostics.LayoutDiagnosticsCoordinator.ScanAsync(
                harness.Client,
                elementId: "Title",
                policyStartPath: app);
            var pinnedElsewhere = await Microsoft.Maui.Cli.DevFlow.Diagnostics.LayoutDiagnosticsCoordinator.ScanAsync(
                harness.Client,
                elementId: "Title",
                policyStartPath: unrelated);

            Assert.Contains(
                pinned!.Findings,
                finding => finding.SuppressionKey == target.SuppressionKey &&
                    finding.Suppressed &&
                    finding.SuppressionReason == "Reviewed for this app");
            Assert.DoesNotContain(pinnedElsewhere!.Findings, finding => finding.Suppressed);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RichRequest_WaitsForAStableSnapshotByDefault()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var report = await harness.Client.AnalyzeLayoutAsync(
            new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
            {
                Scope = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionScope
                {
                    RootElementId = "Title",
                },
            });

        Assert.NotNull(report);
        Assert.True(report!.Snapshot.Stable);
        Assert.Equal("consecutive-layout-snapshots-matched", report.Snapshot.StabilityReason);
    }

    [Fact]
    public async Task RichRequest_ImmediateModeReturnsOneUnstableSnapshot()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var report = await harness.Client.AnalyzeLayoutAsync(
            new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
            {
                Scope = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionScope
                {
                    RootElementId = "Title",
                },
                Stability = new Microsoft.Maui.DevFlow.Driver.LayoutStabilityOptions
                {
                    Mode = "immediate",
                },
            });

        Assert.NotNull(report);
        Assert.False(report!.Snapshot.Stable);
        Assert.Equal("immediate-snapshot-requested", report.Snapshot.StabilityReason);
    }

    [Fact]
    public async Task CompatibilityRequestType_SerializesThroughTheDriverContext()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var report = await harness.Client.AnalyzeLayoutAsync(
            new Microsoft.Maui.DevFlow.Driver.LayoutDiagnosticsRequest
            {
                ElementId = "Title",
                Rules = [Microsoft.Maui.DevFlow.Driver.LayoutDiagnosticRules.VisibleZeroArea],
            });

        Assert.NotNull(report);
        Assert.Equal("Title", report!.Scope.RootElementId);
    }

    [Fact]
    public async Task RuleCatalog_AdvertisesTheFullContract()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var catalog = await harness.Client.GetLayoutDiagnosticRulesAsync();

        Assert.NotNull(catalog);
        Assert.Equal(CoreLayoutRules.All.Count, catalog!.Rules.Count);
        Assert.Contains(catalog.Profiles, profile => profile == "agent");
    }

    [Fact]
    public async Task RichRequest_ReturnsTypedValidationErrors()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var error = await Assert.ThrowsAsync<LayoutDiagnosticsException>(() =>
            harness.Client.AnalyzeLayoutAsync(
                new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
                {
                    Rules = ["layout.not-a-rule"],
                }));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("layout-diagnostics-validation", error.ErrorType);
        Assert.Contains("Unknown", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RichRequest_RejectsNegativeWindowIndexes()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var error = await Assert.ThrowsAsync<LayoutDiagnosticsException>(() =>
            harness.Client.AnalyzeLayoutAsync(
                new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
                {
                    Scope = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionScope
                    {
                        Window = -1,
                    },
                }));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("layout-diagnostics-validation", error.ErrorType);
        Assert.Contains("window", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RichRequest_RejectsAnUnboundedSuppression()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var error = await Assert.ThrowsAsync<LayoutDiagnosticsException>(() =>
            harness.Client.AnalyzeLayoutAsync(
                new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
                {
                    Scope = new Microsoft.Maui.DevFlow.Driver.LayoutInspectionScope
                    {
                        RootElementId = "Title",
                    },
                    Suppressions = [new Microsoft.Maui.DevFlow.Driver.LayoutSuppression()],
                }));

        Assert.Equal(400, error.StatusCode);
        Assert.Contains("selector", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExhaustiveProfile_UsesAllWindowsAndTheMaximumBudget()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var report = await harness.Client.AnalyzeLayoutAsync(
            new Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest
            {
                Profile = "exhaustive",
                Stability = new Microsoft.Maui.DevFlow.Driver.LayoutStabilityOptions
                {
                    Mode = "immediate",
                },
            });

        Assert.NotNull(report);
        Assert.Null(report!.Scope.RootElementId);
        Assert.Equal(CoreLayoutFormat.MaxElements, report.Scope.MaxElements);
        Assert.Equal(CoreLayoutRules.All.Count, report.Coverage.Rules.Count);
    }

    [Fact]
    public async Task Capabilities_AdvertiseLayoutDiagnosticsWithItsLimitations()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        var capabilities = await harness.Client.GetCapabilitiesAsync();
        var layout = capabilities.GetProperty("capabilities").GetProperty("diagnostics.layout");

        Assert.True(layout.GetProperty("supported").GetBoolean());
        Assert.Equal(2, layout.GetProperty("version").GetInt32());
        Assert.Equal(CoreLayoutFormat.SchemaVersion, layout.GetProperty("schemaVersion").GetString());
        Assert.Equal(CoreLayoutFormat.MaxElements, layout.GetProperty("maxElements").GetInt32());
        Assert.NotEmpty(layout.GetProperty("rules").EnumerateArray());
        Assert.NotEmpty(layout.GetProperty("limitations").EnumerateArray());
        Assert.NotEmpty(layout.GetProperty("neverCaptured").EnumerateArray());
    }

    [Fact]
    public async Task Scan_DoesNotRetainRuntimeElementReferencesAfterwards()
    {
        using var harness = await LayoutHarness.CreateAsync(new Label { AutomationId = "Title" });

        await harness.Client.GetLayoutDiagnosticsAsync();

        Assert.Empty(harness.Walker.WalkElements);
        Assert.False(harness.Walker.CaptureWalkElements);
    }

    [Fact]
    public void Walker_OnlyRecordsRuntimeElementsWhenCaptureIsEnabled()
    {
        var walker = new VisualTreeWalker();
        var app = new TestApplication([new Label { AutomationId = "Title" }]);

        walker.WalkTree(app);
        Assert.Empty(walker.WalkElements);

        walker.CaptureWalkElements = true;
        walker.WalkTree(app);
        Assert.NotEmpty(walker.WalkElements);

        // A second walk rebuilds the map rather than accumulating across walks.
        var count = walker.WalkElements.Count;
        walker.WalkTree(app);
        Assert.Equal(count, walker.WalkElements.Count);

        walker.ClearWalkElements();
        Assert.Empty(walker.WalkElements);
    }

    [Fact]
    public void LayoutOnlyWalk_DoesNotCaptureTextValuesOrNativeProperties()
    {
        var root = new VerticalStackLayout
        {
            Children =
            {
                new Label { AutomationId = "SecretLabel", Text = "secret" },
                new Switch { AutomationId = "SecretSwitch", IsToggled = true },
            },
        };
        var walker = new VisualTreeWalker
        {
            CaptureWalkElements = true,
            CaptureLayoutOnly = true,
        };

        var nodes = VisualTreeWalker.FlattenElementInfos(walker.WalkRoot(root)).ToList();

        Assert.All(nodes, node =>
        {
            Assert.Null(node.Text);
            Assert.Null(node.Value);
            Assert.Null(node.NativeProperties);
        });
    }

    [Fact]
    public void ActivePageResolver_UnwrapsNavigationContainers()
    {
        var page = new ContentPage();
        var window = new Window(new NavigationPage(page));

        var active = DevFlowAgentService.ResolveActiveLayoutPage(window);

        Assert.Same(page, active);
    }

    [Fact]
    public void BlazorLayoutProbe_DoesNotReadRawText()
    {
        var method = typeof(DevFlowAgentService).GetMethod(
            "BuildBlazorLayoutExpression",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var script = Assert.IsType<string>(method.Invoke(null, [100]));

        Assert.DoesNotContain("textContent", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerText", script, StringComparison.Ordinal);
        Assert.DoesNotContain("nodeValue", script, StringComparison.Ordinal);
        Assert.Contains("elementFromPoint", script, StringComparison.Ordinal);
        Assert.Contains("getBoundingClientRect", script, StringComparison.Ordinal);
    }

    [Fact]
    public void StabilityRevision_IncludesNativeEvidence()
    {
        var method = typeof(DevFlowAgentService).GetMethod(
            "ComputeLayoutSnapshotRevision",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var first = new LayoutElementSnapshot
        {
            Id = "element",
            FullRegion = Region(0, 0, 100, 100),
            VisibleRegion = Region(0, 0, 100, 100),
        };
        var second = new LayoutElementSnapshot
        {
            Id = "element",
            FullRegion = Region(0, 0, 100, 100),
            VisibleRegion = Region(0, 0, 50, 100),
        };

        var firstRevision = Assert.IsType<string>(method.Invoke(null, [new[] { first }]));
        var secondRevision = Assert.IsType<string>(method.Invoke(null, [new[] { second }]));

        Assert.NotEqual(firstRevision, secondRevision);
    }

    [Fact]
    public void SnapshotCollector_UsesEffectiveAncestorVisibility()
    {
        Microsoft.Maui.DevFlow.Agent.Core.ElementInfo[] roots =
        {
            new Microsoft.Maui.DevFlow.Agent.Core.ElementInfo
            {
                Id = "parent",
                Type = "Grid",
                IsVisible = false,
                Opacity = 1,
                Children =
                [
                    new Microsoft.Maui.DevFlow.Agent.Core.ElementInfo
                    {
                        Id = "child",
                        ParentId = "parent",
                        Type = "Label",
                        IsVisible = true,
                        Opacity = 1,
                    }
                ]
            }
        };

        var result = LayoutSnapshotCollector.Collect(
            roots,
            new Dictionary<string, object>(),
            rootElementId: null,
            maxElements: 10);
        var scoped = LayoutSnapshotCollector.Collect(
            roots,
            new Dictionary<string, object>(),
            rootElementId: "child",
            maxElements: 10);

        Assert.False(Assert.Single(result.Snapshots, item => item.Id == "child").IsVisible);
        Assert.False(Assert.Single(scoped.Snapshots).IsVisible);
    }

    [Fact]
    public void Window_IsAVisibleStructuralRootForEffectiveVisibility()
    {
        var label = new Label { AutomationId = "Title", IsVisible = true };
        var window = new Window(new ContentPage { Content = label });
        var walker = new VisualTreeWalker { CaptureWalkElements = true };
        var roots = walker.WalkTree(new TestApplication([window]));

        var root = Assert.Single(roots);
        Assert.Equal("Window", root.Type);
        Assert.True(root.IsVisible);

        var collected = LayoutSnapshotCollector.Collect(
            roots,
            walker.WalkElements,
            rootElementId: null,
            maxElements: 20);
        Assert.True(Assert.Single(
            collected.Snapshots,
            snapshot => snapshot.Id == "Title").IsVisible);
    }

    [Fact]
    public void ShellItems_PreserveTheirStructuralVisibility()
    {
        var shellContent = new ShellContent
        {
            AutomationId = "NativeRoute",
            IsVisible = true,
            IsEnabled = true,
        };
        var roots = new VisualTreeWalker().WalkTree(new TestApplication([shellContent]));

        var info = Assert.Single(roots);
        Assert.Equal("ShellContent", info.Type);
        Assert.True(info.IsVisible);
        Assert.True(info.IsEnabled);
    }

    [Fact]
    public void Walker_ElementBudgetStopsTraversalAndRuntimeReferenceCapture()
    {
        var walker = new VisualTreeWalker { CaptureWalkElements = true };
        var app = new TestApplication(
            Enumerable.Range(0, 20).Select(index =>
                new Label { AutomationId = $"Label{index}" }));

        var tree = walker.WalkTree(app, maxDepth: 0, windowIndex: null, maxElements: 2);

        Assert.Equal(2, tree.Count);
        Assert.Equal(2, walker.WalkElements.Count);
        Assert.True(walker.WalkWasTruncated);
    }

    [Fact]
    public void Walker_ElementBudgetIncludesSyntheticToolbarItems()
    {
        var page = new ContentPage();
        for (var index = 0; index < 10; index++)
            page.ToolbarItems.Add(new ToolbarItem { Text = $"Item {index}" });
        var walker = new VisualTreeWalker();
        var app = new TestApplication([page]);

        var tree = walker.WalkTree(app, maxDepth: 0, windowIndex: null, maxElements: 2);
        var flattened = VisualTreeWalker.FlattenElementInfos(tree).ToList();

        Assert.Equal(2, flattened.Count);
        Assert.True(walker.WalkWasTruncated);
    }

    [Fact]
    public void Walker_ObjectReferenceCachePreservesIdAcrossLookupAndSubtreeWalk()
    {
        var walker = new VisualTreeWalker();
        var element = new Label { AutomationId = "StableId" };
        var generateId = typeof(VisualTreeWalker).GetMethod(
            "GenerateId",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var elementCache = (Dictionary<Guid, string>)typeof(VisualTreeWalker)
            .GetField("_elementIdToExternalId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(walker)!;

        var first = (string)generateId.Invoke(walker, [element])!;
        elementCache.Clear();
        var second = (string)generateId.Invoke(walker, [element])!;

        Assert.Equal(first, second);
        Assert.Equal("StableId", second);
    }

    [Fact]
    public void Walker_ObjectReferenceCacheDoesNotRetainRemovedTree()
    {
        var walker = new VisualTreeWalker();
        var reference = WalkTemporaryTree(walker);

        for (var attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(10);
        }

        Assert.False(reference.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WalkTemporaryTree(VisualTreeWalker walker)
    {
        var label = new Label { AutomationId = "Temporary" };
        typeof(VisualTreeWalker)
            .GetMethod("GenerateId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(walker, [label]);
        return new WeakReference(label);
    }

    private static Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics.LayoutRegionInfo Region(
        double x,
        double y,
        double width,
        double height)
        => LayoutPlatformEvidence.Region(
            new Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics.LayoutRect
            {
                X = x,
                Y = y,
                Width = width,
                Height = height,
            },
            "exact");

    private sealed class LayoutHarness : IDisposable
    {
        private readonly DevFlowAgentService _service;
        private readonly HttpClient _http;

        private LayoutHarness(DevFlowAgentService service, AgentClient client, VisualTreeWalker walker, int port)
        {
            _service = service;
            Client = client;
            Walker = walker;
            _http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        }

        public AgentClient Client { get; }

        public VisualTreeWalker Walker { get; }

        public Task<string> GetRawAsync(string path) => _http.GetStringAsync(path);

        public async Task<string> PostRawAsync(string path, string json)
        {
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(path, content);
            return await response.Content.ReadAsStringAsync();
        }

        public static async Task<LayoutHarness> CreateAsync(params View[] views)
        {
            var service = new TestAgentService(new AgentOptions { Port = GetFreePort() });
            var client = new AgentClient("localhost", service.Port);
            service.StartServerOnly(new ImmediateDispatcher());
            service.BindApp(new TestApplication(views));

            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (await client.GetStatusAsync() is not null)
                    return new LayoutHarness(service, client, service.Walker, service.Port);
                await Task.Delay(50);
            }

            client.Dispose();
            service.Dispose();
            throw new InvalidOperationException("DevFlow layout test agent did not start.");
        }

        public void Dispose()
        {
            _http.Dispose();
            Client.Dispose();
            _service.Dispose();
        }

        private static int GetFreePort() => TestPorts.Reserve();
    }

    /// <summary>Exposes the walker instance so a test can assert the per-scan map is released.</summary>
    private sealed class TestAgentService : DevFlowAgentService
    {
        public TestAgentService(AgentOptions options) : base(options) { }

        public VisualTreeWalker Walker { get; private set; } = null!;

        protected override VisualTreeWalker CreateTreeWalker()
        {
            Walker = new VisualTreeWalker();
            return Walker;
        }
    }

    private sealed class TestApplication : Application, IVisualTreeElement
    {
        private readonly IReadOnlyList<IVisualTreeElement> _children;

        public TestApplication(IEnumerable<IVisualTreeElement> views)
            => _children = views.ToArray();

        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => _children;

        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => null;
    }

    private sealed class ImmediateDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
    }

    private sealed class ImmediateDispatcherTimer : IDispatcherTimer
    {
        public bool IsRepeating { get; set; }
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }
}
