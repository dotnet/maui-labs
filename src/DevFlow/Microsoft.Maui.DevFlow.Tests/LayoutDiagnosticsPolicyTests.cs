using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics;
using DriverLayoutSuppression = Microsoft.Maui.DevFlow.Driver.LayoutSuppression;
using CoreLayoutRules = Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics.LayoutDiagnosticRules;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class LayoutDiagnosticsPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"devflow-layout-policy-{Guid.NewGuid():N}");

    public LayoutDiagnosticsPolicyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void UpdateProjectPolicy_PreservesExistingConfiguration()
    {
        var path = Path.Combine(_root, ".mauidevflow");
        File.WriteAllText(path, """{"port":9223,"other":{"enabled":true}}""");

        LayoutDiagnosticsPolicyLoader.UpdateProjectPolicy(
            _root,
            policy => policy.Suppressions.Add(new DriverLayoutSuppression
            {
                Fingerprint = "abc123",
                Reason = "Intentional overlap",
            }));

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(9223, document.RootElement.GetProperty("port").GetInt32());
        Assert.True(document.RootElement.GetProperty("other").GetProperty("enabled").GetBoolean());
        var suppression = Assert.Single(
            document.RootElement
                .GetProperty("layoutDiagnostics")
                .GetProperty("suppressions")
                .EnumerateArray());
        Assert.Equal("abc123", suppression.GetProperty("fingerprint").GetString());
    }

    [Fact]
    public void UpdateProjectPolicyCas_AppliesOnlyTheReviewedDigest()
    {
        var path = Path.Combine(_root, ".mauidevflow");
        File.WriteAllText(path, """{"port":9223}""");
        var digest = LayoutDiagnosticsPolicyLoader.GetProjectPolicyDigest(_root);

        LayoutDiagnosticsPolicyLoader.UpdateProjectPolicyCas(
            _root,
            digest,
            policy => policy.Suppressions.Add(new DriverLayoutSuppression
            {
                Fingerprint = "reviewed",
                Reason = "Reviewed reason",
            }));

        var policy = LayoutDiagnosticsPolicyLoader.LoadProjectPolicy(_root);
        Assert.Equal("reviewed", Assert.Single(policy.Suppressions).Fingerprint);
    }

    [Fact]
    public void UpdateProjectPolicyCas_RejectsAnExternalEdit()
    {
        var path = Path.Combine(_root, ".mauidevflow");
        File.WriteAllText(path, """{"port":9223}""");
        var digest = LayoutDiagnosticsPolicyLoader.GetProjectPolicyDigest(_root);
        File.WriteAllText(path, """{"port":9224}""");

        var error = Assert.Throws<LayoutPolicyConcurrencyException>(() =>
            LayoutDiagnosticsPolicyLoader.UpdateProjectPolicyCas(
                _root,
                digest,
                policy => policy.Suppressions.Add(new DriverLayoutSuppression { Fingerprint = "stale" })));

        Assert.Contains("changed after it was reviewed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stale", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void LoadProjectPolicy_FindsTheNearestAncestorConfig()
    {
        var child = Path.Combine(_root, "src", "Views");
        Directory.CreateDirectory(child);
        File.WriteAllText(
            Path.Combine(_root, ".mauidevflow"),
            """
            {
              "layoutDiagnostics": {
                "suppressions": [
                  { "ruleId": "layout.element-clipped", "automationId": "Hero" }
                ]
              }
            }
            """);

        var policy = LayoutDiagnosticsPolicyLoader.LoadProjectPolicy(child);

        var suppression = Assert.Single(policy.Suppressions);
        Assert.Equal("layout.element-clipped", suppression.RuleId);
        Assert.Equal("Hero", suppression.AutomationId);
    }

    [Fact]
    public void SuppressionMatching_HasExactlyOneAuthoritativeImplementation()
    {
        // The analyzer inside the agent is the only place a suppression is matched against a
        // finding. A second CLI-side matcher previously existed, was never called by production
        // code, and had already drifted (it accepted a finding id as a fingerprint, which the
        // analyzer never does). Its absence is part of the contract.
        var cliTypes = typeof(LayoutDiagnosticsPolicyLoader).Assembly
            .GetTypes()
            .Where(type => type.Name.Contains("SuppressionMatcher", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(cliTypes);
    }

    [Fact]
    public void PolicySuppressions_AreMatchedByTheAnalyzerUsingTheStableFingerprint()
    {
        File.WriteAllText(
            Path.Combine(_root, ".mauidevflow"),
            """
            {
              "layoutDiagnostics": {
                "suppressions": [
                  { "ruleId": "layout.visible-zero-area", "automationId": "Target", "reason": "Intentional collapsed host" }
                ]
              }
            }
            """);
        var policy = LayoutDiagnosticsPolicyLoader.LoadProjectPolicy(_root);

        var target = new LayoutElementSnapshot
        {
            Id = "runtime-id-0007",
            Type = "Label",
            AutomationId = "Target",
            SourceFile = "Views/MainPage.xaml",
            SourceLine = 42,
            IsVisible = true,
            IsEnabled = true,
            IsRealized = true,
            HasLayoutState = true,
            Frame = new LayoutRect { X = 0, Y = 0, Width = 0, Height = 20 },
        };
        var report = LayoutDiagnosticsAnalyzer.Analyze(
            [target],
            new LayoutDiagnosticsScope { MaxElements = 100, ElementsExamined = 1 },
            "Windows",
            new DateTime(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc),
            new LayoutInspectionRequest
            {
                Rules = [CoreLayoutRules.VisibleZeroArea],
                Suppressions = [.. policy.Suppressions.Select(suppression => new LayoutSuppression
                {
                    RuleId = suppression.RuleId,
                    AutomationId = suppression.AutomationId,
                    Fingerprint = suppression.Fingerprint,
                    Reason = suppression.Reason,
                })],
            });

        var finding = Assert.Single(report.Findings);
        Assert.True(finding.Suppressed);
        Assert.Equal("Intentional collapsed host", finding.SuppressionReason);
        Assert.NotEmpty(finding.SuppressionKey);
        Assert.DoesNotContain("runtime-id-0007", finding.SuppressionKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidProjectPolicy_IsSurfacedInsteadOfSilentlyIgnored()
    {
        File.WriteAllText(Path.Combine(_root, ".mauidevflow"), "{ invalid json");

        var error = Assert.Throws<InvalidOperationException>(() =>
            LayoutDiagnosticsPolicyLoader.LoadProjectPolicy(_root));

        Assert.Contains(".mauidevflow", error.Message, StringComparison.Ordinal);
    }
}
