using Microsoft.Maui.Cli.DevFlow;

namespace Microsoft.Maui.DevFlow.Tests;

public class LayoutDiagnosticsPolicyTests
{
    [Fact]
    public void Load_ProjectConfig_ReadsLayoutSuppressions()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "maui-devflow-layout-policy-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "src", "App");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllText(
                Path.Combine(root, ".mauidevflow"),
                """
                {
                  "port": 9225,
                  "layoutDiagnostics": {
                    "suppressions": [
                      {
                        "ruleId": "layout.element-clipped",
                        "elementType": "Button",
                        "sourceFile": "Views/Page.xaml",
                        "sourceLineStart": 10,
                        "sourceLineEnd": 20,
                        "reason": "Expected fixture"
                      }
                    ]
                  }
                }
                """);

            var policy = LayoutDiagnosticsPolicyLoader.Load(
                nested,
                includeUserPolicy: false);

            var suppression = Assert.Single(policy.Suppressions);
            Assert.Equal("Button", suppression.ElementType);
            Assert.Equal("Views/Page.xaml", suppression.SourceFile);
            Assert.Equal(10, suppression.SourceLineStart);
            Assert.Equal("Expected fixture", suppression.Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_InvalidProjectPolicy_ThrowsWithPath()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "maui-devflow-layout-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, ".mauidevflow");
        try
        {
            File.WriteAllText(config, """{"layoutDiagnostics":""");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                LayoutDiagnosticsPolicyLoader.Load(root, includeUserPolicy: false));

            Assert.Contains(config, exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveProjectPolicy_PreservesExistingConfigAndReloadsSuppressions()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "maui-devflow-layout-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, ".mauidevflow");
        try
        {
            File.WriteAllText(config, """{"port":9225}""");
            var policy = new LayoutDiagnosticsPolicy
            {
                Suppressions =
                [
                    new Microsoft.Maui.DevFlow.Driver.LayoutSuppression
                    {
                        Fingerprint = "finding-1",
                        Reason = "Inspector suppression"
                    }
                ]
            };

            LayoutDiagnosticsPolicyLoader.SaveProjectPolicy(root, policy);
            var reloaded = LayoutDiagnosticsPolicyLoader.LoadProjectPolicy(root);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(config));

            Assert.Equal(9225, document.RootElement.GetProperty("port").GetInt32());
            Assert.Equal("finding-1", Assert.Single(reloaded.Suppressions).Fingerprint);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateProjectPolicy_ConcurrentMutationsPreserveAllSuppressions()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "maui-devflow-layout-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, ".mauidevflow");
        try
        {
            File.WriteAllText(config, """{"port":9225}""");
            var updates = Enumerable.Range(0, 8)
                .Select(index => Task.Run(() =>
                    LayoutDiagnosticsPolicyLoader.UpdateProjectPolicy(
                        root,
                        policy => policy.Suppressions.Add(new()
                        {
                            Fingerprint = $"finding-{index}"
                        }))))
                .ToArray();

            await Task.WhenAll(updates);

            var reloaded = LayoutDiagnosticsPolicyLoader.LoadProjectPolicy(root);
            Assert.Equal(
                Enumerable.Range(0, 8).Select(index => $"finding-{index}").Order(),
                reloaded.Suppressions.Select(item => item.Fingerprint).Order());
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SuppressionMatcher_DistinguishesExactAndBroadPolicies()
    {
        var finding = new Microsoft.Maui.DevFlow.Driver.LayoutFinding
        {
            Id = "finding-1",
            RuleId = "layout.element-clipped",
            Element = new()
            {
                Id = "button",
                Type = "Button",
                SourceFile = @"C:\repo\Views\Page.xaml",
                SourceLine = 12
            }
        };

        Assert.True(LayoutDiagnosticsSuppressionMatcher.Matches(
            new()
            {
                RuleId = "layout.element-clipped",
                SourceFile = "Views/Page.xaml",
                SourceLineStart = 10,
                SourceLineEnd = 20
            },
            finding));
        Assert.False(LayoutDiagnosticsSuppressionMatcher.Matches(
            new() { Fingerprint = "finding-2" },
            finding));
    }
}
