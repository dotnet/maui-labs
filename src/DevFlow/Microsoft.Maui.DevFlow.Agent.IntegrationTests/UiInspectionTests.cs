using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "UiInspection")]
public class UiInspectionTests : IntegrationTestBase
{
    public UiInspectionTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task Tree_ReturnsNonEmptyTree()
    {
        await NavigateToMainPageAsync();
        var tree = await Client.GetTreeAsync();

        Assert.NotNull(tree);
        Assert.NotEmpty(tree);
    }

    [Fact]
    public async Task Tree_WithDepth_LimitsChildren()
    {
        await NavigateToMainPageAsync();
        var shallow = await Client.GetTreeAsync(maxDepth: 1);
        var deep = await Client.GetTreeAsync(maxDepth: 10);

        Assert.NotEmpty(shallow);

        static int CountNodes(IEnumerable<ElementInfo> elements)
        {
            var count = 0;
            foreach (var element in elements)
            {
                count++;
                if (element.Children != null)
                    count += CountNodes(element.Children);
            }
            return count;
        }

        Assert.True(CountNodes(shallow) <= CountNodes(deep),
            "Depth-limited tree should not have more nodes than a deeper tree.");
    }

    [Fact]
    public async Task Tree_ElementsHaveBounds()
    {
        await NavigateToMainPageAsync();
        var tree = await Client.GetTreeAsync(maxDepth: 10);

        static ElementInfo? FindWithBounds(IEnumerable<ElementInfo> elements)
        {
            foreach (var element in elements)
            {
                if (element.Bounds is { Width: > 0, Height: > 0 })
                    return element;

                if (element.Children != null)
                {
                    var found = FindWithBounds(element.Children);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        var elementWithBounds = FindWithBounds(tree);
        Assert.NotNull(elementWithBounds);
        Output.WriteLine($"Found element with bounds: {elementWithBounds!.Type} ({elementWithBounds.Bounds!.Width}x{elementWithBounds.Bounds.Height})");
    }

    [Fact]
    public async Task Query_ByType_ReturnsElements()
    {
        await NavigateToMainPageAsync();
        var buttons = await Client.QueryAsync(type: ButtonTypeName);

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Equal(ButtonTypeName, button.Type));
    }

    [Fact]
    public async Task Query_ByAutomationId_ReturnsExactElement()
    {
        await NavigateToMainPageAsync();
        var elements = await Client.QueryAsync(automationId: "AddButton");

        Assert.Single(elements);
        Assert.Equal("AddButton", elements[0].AutomationId);
    }

    [Fact]
    public async Task Query_ByAutomationId_HasCorrectProperties()
    {
        await NavigateToMainPageAsync();
        var element = await FindElementAsync("HeaderLabel");

        Assert.Equal("HeaderLabel", element.AutomationId);
        Assert.NotNull(element.Text);
        Assert.Contains("Todos", element.Text!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Query_ByText_ReturnsElements()
    {
        await NavigateToMainPageAsync();
        var elements = await Client.QueryAsync(text: "Todos");

        Assert.NotEmpty(elements);
        Assert.Contains(elements, element => element.Text != null && element.Text.Contains("Todos", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Query_ByCssSelector_ReturnsElements()
    {
        await NavigateToMainPageAsync();
        var elements = await Client.QueryCssAsync($"{ButtonTypeName}#AddButton");

        Assert.NotEmpty(elements);
        Assert.Contains(elements, element => element.AutomationId == "AddButton");
    }

    [Fact]
    public async Task Query_NoResults_ReturnsEmpty()
    {
        var elements = await Client.QueryAsync(type: "NonExistentControlType99");
        Assert.Empty(elements);
    }

    [Fact]
    [Trait(TestFramework.Trait, TestFramework.Maui)]
    public async Task LayoutDiagnostics_DetectsClippingWithoutFlaggingIntentionalOverlap()
    {
        await NavigateToPageAsync("//layoutdiagnostics", "ClippedButton");

        var result = await Client.AnalyzeLayoutAsync(new LayoutInspectionRequest
        {
            Profile = "agent",
            MinimumSeverity = "info",
            Stability = new LayoutStabilityOptions
            {
                Mode = "wait",
                TimeoutMs = 3000
            }
        });

        Assert.NotNull(result);
        var clippedFinding = Assert.Single(result!.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.ElementClipped
            && finding.Element.AutomationId == "ClippedButton");
        Assert.EndsWith(
            "LayoutDiagnosticsTestPage.xaml",
            clippedFinding.Element.SourceFile,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Path.IsPathRooted(clippedFinding.Element.SourceFile));
        Assert.True(clippedFinding.Element.SourceLine > 0);
        Assert.DoesNotContain(result.Findings, finding =>
            finding.Outcome == "violation"
            && finding.Element.AutomationId is "IntentionalCard" or "IntentionalBadge");
    }

    [Fact]
    [Trait(TestFramework.Trait, TestFramework.Maui)]
    public async Task LayoutDiagnostics_ExhaustiveProfileReportsIntentionalGeometricOverlap()
    {
        await NavigateToPageAsync("//layoutdiagnostics", "IntentionalBadge");

        var result = await Client.AnalyzeLayoutAsync(new LayoutInspectionRequest
        {
            Profile = "exhaustive",
            MinimumSeverity = "info",
            Rules = [LayoutDiagnosticRules.GeometricOverlap],
            Stability = new LayoutStabilityOptions { Mode = "immediate" }
        });

        Assert.NotNull(result);
        Assert.Contains(result!.Findings, finding =>
            finding.RuleId == LayoutDiagnosticRules.GeometricOverlap
            && (finding.Element.AutomationId is "IntentionalCard" or "IntentionalBadge"
                || finding.RelatedElements.Any(related =>
                    related.Element.AutomationId is "IntentionalCard" or "IntentionalBadge")));
    }

    [Fact]
    [Trait(TestFramework.Trait, TestFramework.Maui)]
    public async Task LayoutDiagnostics_MissingRootIsIncomplete()
    {
        await NavigateToMainPageAsync();

        var result = await Client.AnalyzeLayoutAsync(
            new LayoutInspectionRequest
            {
                Profile = "ci",
                Scope = new LayoutInspectionScope
                {
                    RootElementId = "definitely-missing"
                },
                Stability = new LayoutStabilityOptions
                {
                    Mode = "immediate"
                }
            });

        Assert.NotNull(result);
        Assert.Equal(0, result!.Snapshot.NodeCount);
        Assert.True(result.Summary.Incomplete > 0);
        Assert.Contains(
            result.Coverage.Limitations,
            limitation => limitation.Contains(
                "was not found",
                StringComparison.Ordinal));
    }

    // AppKit renders both labels and text fields as NSTextField, so the Label/Entry
    // split this asserts on is specific to MAUI's normalised control names.
    [Fact]
    [Trait(TestFramework.Trait, TestFramework.Maui)]
    public async Task Query_MultipleTypes_ReturnsAppropriateResults()
    {
        await NavigateToMainPageAsync();

        var labels = await Client.QueryAsync(type: "Label");
        var entries = await Client.QueryAsync(type: "Entry");

        Assert.NotEmpty(labels);
        Assert.NotEmpty(entries);

        var labelIds = labels.Select(e => e.Id).ToHashSet();
        var entryIds = entries.Select(e => e.Id).ToHashSet();
        Assert.Empty(labelIds.Intersect(entryIds));
    }

    [Fact]
    public async Task Element_ById_ReturnsElement()
    {
        await NavigateToMainPageAsync();
        var addButton = await FindElementAsync("AddButton");

        var element = await Client.GetElementAsync(addButton.Id);

        Assert.NotNull(element);
        Assert.Equal(addButton.Id, element!.Id);
        Assert.Equal("AddButton", element.AutomationId);
    }

    [Fact]
    public async Task HitTest_AtKnownCoordinates_ReturnsElement()
    {
        await NavigateToMainPageAsync();
        var addButton = await FindElementAsync("AddButton");
        Assert.NotNull(addButton.Bounds);

        var centerX = addButton.Bounds!.X + (addButton.Bounds.Width / 2);
        var centerY = addButton.Bounds.Y + (addButton.Bounds.Height / 2);

        var elementId = await Client.HitTestAsync(centerX, centerY);

        Assert.NotNull(elementId);
        Assert.NotEmpty(elementId);
    }

    // Guards the documented hit-test envelope (openapi.yaml requires x/y/window/captureEpoch/
    // registryGeneration/elements) that InspectorServer's click-to-select path depends on: it looks
    // for a root-level "elements" property and silently treats a bare array — or any other missing
    // field — as "no element here". This is a plain JsonElement parse (not AgentClient.HitTestAsync,
    // which just hands back the raw body) so a regression to the old bare-array shape fails here
    // across every platform, native included, rather than only being caught by Inspector consumers.
    [Fact]
    public async Task HitTest_Response_MatchesDocumentedEnvelope()
    {
        await NavigateToMainPageAsync();
        var addButton = await FindElementAsync("AddButton");
        Assert.NotNull(addButton.Bounds);

        var centerX = addButton.Bounds!.X + (addButton.Bounds.Width / 2);
        var centerY = addButton.Bounds.Y + (addButton.Bounds.Height / 2);

        var json = await GetJsonAsync(
            $"/api/v1/ui/hit-test?x={centerX.ToString(CultureInfo.InvariantCulture)}&y={centerY.ToString(CultureInfo.InvariantCulture)}");

        Assert.Equal(JsonValueKind.Object, json.ValueKind);

        Assert.True(json.TryGetProperty("x", out _), "Response is missing 'x'.");
        Assert.True(json.TryGetProperty("y", out _), "Response is missing 'y'.");

        Assert.True(json.TryGetProperty("window", out var windowProperty), "Response is missing 'window'.");
        Assert.Equal(0, windowProperty.GetInt32());

        Assert.True(json.TryGetProperty("captureEpoch", out var epochProperty), "Response is missing 'captureEpoch'.");
        Assert.True(epochProperty.GetInt64() >= 1, "captureEpoch must be a positive integer per the OpenAPI contract.");

        Assert.True(json.TryGetProperty("registryGeneration", out var generationProperty), "Response is missing 'registryGeneration'.");
        Assert.True(generationProperty.GetInt64() >= 0, "registryGeneration must be non-negative per the OpenAPI contract.");

        Assert.True(json.TryGetProperty("elements", out var elementsProperty), "Response is missing root-level 'elements' — InspectorServer's click-to-select would silently find no candidate.");
        Assert.Equal(JsonValueKind.Array, elementsProperty.ValueKind);
        var elements = elementsProperty.EnumerateArray().ToList();
        Assert.NotEmpty(elements);
        Assert.Contains(elements, element => element.GetProperty("id").GetString() == addButton.Id);
    }

    [Fact]
    public async Task Screenshot_ReturnsValidPng()
    {
        var bytes = await Client.ScreenshotAsync();

        if (bytes == null)
        {
            var raw = await GetRawAsync("/api/v1/ui/screenshot");
            var body = await raw.Content.ReadAsStringAsync();
            Output.WriteLine($"Screenshot raw response: {(int)raw.StatusCode} — {body}");
            Output.WriteLine("Screenshot not available on this platform.");
            return;
        }

        Assert.True(bytes.Length > 100, "Screenshot should have reasonable size");
        Assert.Equal((byte)0x89, bytes[0]);
        Assert.Equal((byte)0x50, bytes[1]);
        Assert.Equal((byte)0x4E, bytes[2]);
        Assert.Equal((byte)0x47, bytes[3]);
    }

    [Fact]
    public async Task Screenshot_OfElement_ReturnsImage()
    {
        await NavigateToMainPageAsync();
        var addButton = await FindElementAsync("AddButton");

        var bytes = await Client.ScreenshotAsync(elementId: addButton.Id);
        if (bytes == null)
        {
            Output.WriteLine("Element screenshot not available on this platform.");
            return;
        }

        Assert.True(bytes.Length > 0);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Tree_WindowsNativeDialog_IncludesNativeElements()
    {
        if (!Platform.Equals("windows", StringComparison.OrdinalIgnoreCase))
        {
            Output.WriteLine("Windows native dialog inspection test skipped on non-Windows platform.");
            return;
        }

        await NavigateToPageAsync("//dialogs", "AlertOkOnlyBtn");

        var trigger = await FindElementAsync("AlertOkOnlyBtn");
        Assert.True(await Client.TapAsync(trigger.Id).WaitAsync(TimeSpan.FromSeconds(5)));

        var okButton = await WaitForNativeButtonAsync("OK");
        var tree = await Client.GetTreeAsync(maxDepth: 8);
        var flattened = Flatten(tree).ToList();

        Assert.Contains(flattened, e => e.Id.StartsWith("native:", StringComparison.Ordinal));
        Assert.Contains(flattened, e => e.Id.StartsWith("native:", StringComparison.Ordinal) && e.Traits?.Contains("dialog") == true);

        Assert.True(await Client.TapAsync(okButton.Id));
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Tree_CustomMauiBackend_IncludesRegisteredNativeElement()
    {
        if (Platform is not ("macos" or "gtk" or "wpf"))
        {
            Output.WriteLine("Registered toolbar bridge test only applies to custom MAUI desktop backends.");
            return;
        }

        await NavigateToMainPageAsync();

        var expectedRole = Platform == "macos" ? "toolbar-item" : "shell-flyout";
        ElementInfo? registeredNativeElement = null;
        await WaitForAsync(async () =>
        {
            var tree = await Client.GetTreeAsync(maxDepth: 12);
            registeredNativeElement = Flatten(tree).FirstOrDefault(element =>
                element.Id.StartsWith("native:registered:", StringComparison.Ordinal)
                && element.Role == expectedRole);
            return registeredNativeElement is not null;
        }, timeoutMs: 10000);

        Assert.NotNull(registeredNativeElement!.OwnerId);
        Assert.Equal("native", registeredNativeElement.Origin);
        Assert.NotEmpty(registeredNativeElement.Capabilities ?? []);
    }

    async Task<ElementInfo> WaitForNativeButtonAsync(string text)
    {
        ElementInfo? match = null;
        await WaitForAsync(async () =>
        {
            var buttons = await Client.QueryAsync(type: ButtonTypeName, text: text);
            match = buttons.FirstOrDefault(e =>
                e.Id.StartsWith("native:", StringComparison.Ordinal) &&
                string.Equals(e.Text, text, StringComparison.OrdinalIgnoreCase));
            return match is not null;
        }, timeoutMs: 5000);

        return match!;
    }

    static IEnumerable<ElementInfo> Flatten(IEnumerable<ElementInfo> elements)
    {
        foreach (var element in elements)
        {
            yield return element;
            if (element.Children is not null)
            {
                foreach (var child in Flatten(element.Children))
                    yield return child;
            }
        }
    }
}