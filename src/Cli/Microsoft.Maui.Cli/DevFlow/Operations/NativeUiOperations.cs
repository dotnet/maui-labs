using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;
using ActionResult = Microsoft.Maui.DevFlow.Driver.ActionResult;

namespace Microsoft.Maui.Cli.DevFlow;

internal enum ElementActionKind
{
    Tap,
    TextInput,
    Focus,
    Gesture
}

internal sealed class NativeUiOperations(AgentClient client)
{
    internal Task<AgentStatus?> GetStatusAsync(int? window = null) =>
        client.GetStatusAsync(window);

    internal Task<List<ElementInfo>> GetTreeAsync(int depth, int? window) =>
        client.GetTreeAsync(depth, window);

    internal Task<ElementInfo?> GetElementAsync(string id) =>
        client.GetElementAsync(id);

    internal Task<string?> GetPropertyAsync(string id, string property) =>
        client.GetPropertyAsync(id, property);

    internal async Task<(string? Actual, bool Passed)> AssertAsync(string id, string property, string expected)
    {
        var actual = await client.GetPropertyAsync(id, property);

        return (actual, string.Equals(actual, expected, StringComparison.Ordinal));
    }

    internal Task<ActionResult> SetPropertyAsync(string id, string property, string value) =>
        client.SetPropertyResultAsync(id, property, value, captureEpoch: null, registryGeneration: null);

    internal Task<string> HitTestAsync(double x, double y, int? window) =>
        client.HitTestAsync(x, y, window);

    internal Task<JsonElement> GetMenusAsync(int? window) =>
        client.GetMenusAsync(window);

    internal Task<JsonElement> InvokeMenuAsync(
        string? id, string? path, string? title, string? key,
        string? modifiers, string? target, int? window) =>
        client.InvokeMenuAsync(id, path, title, key, modifiers, target, window);

    internal Task<bool> NavigateAsync(string route) =>
        client.NavigateAsync(route);

    internal Task<bool> ResizeAsync(int width, int height, int? window) =>
        client.ResizeAsync(width, height, window);

    internal Task<bool> ScrollAsync(
        string? id, double dx, double dy, bool animated, int? window,
        int? itemIndex, int? groupIndex, string? position) =>
        client.ScrollAsync(id, dx, dy, animated, window, itemIndex, groupIndex, position);

    internal Task<bool> TapAsync(ElementInfo element)
    {
        var (epoch, generation) = GetCaptureMetadata(element);

        return client.TapAsync(element.Id, epoch, generation);
    }

    internal Task<bool> FillAsync(ElementInfo element, string text)
    {
        var (epoch, generation) = GetCaptureMetadata(element);

        return client.FillAsync(element.Id, text, epoch, generation);
    }

    internal Task<bool> ClearAsync(ElementInfo element)
    {
        var (epoch, generation) = GetCaptureMetadata(element);

        return client.ClearAsync(element.Id, epoch, generation);
    }

    internal Task<bool> FocusAsync(ElementInfo element)
    {
        var (epoch, generation) = GetCaptureMetadata(element);

        return client.FocusAsync(element.Id, epoch, generation);
    }

    internal Task<GestureResult> GestureAsync(
        string type, ElementInfo? element, string? direction, double? distance, int? durationMs,
        double? scale, double? rotation, double? deltaX, double? deltaY, double? originX, double? originY, int? steps)
    {
        var (epoch, generation) = element is null ? (null, null) : GetCaptureMetadata(element);

        return client.GestureDetailedAsync(type, element?.Id, direction, distance, durationMs,
            scale, rotation, deltaX, deltaY, originX, originY, steps, epoch, generation);
    }

    internal async Task<(List<ElementInfo> Elements, bool TimedOut)> QueryAsync(
        string? type, string? automationId, string? text, string? selector = null,
        string? waitUntil = null, int timeout = 0)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeout);

        while (true)
        {
            var results = !string.IsNullOrWhiteSpace(selector)
                ? await client.QueryCssAsync(selector)
                : await client.QueryAsync(type, automationId, text);

            if (string.IsNullOrWhiteSpace(waitUntil)
                || (waitUntil.Equals("exists", StringComparison.OrdinalIgnoreCase) && results.Count > 0)
                || (waitUntil.Equals("gone", StringComparison.OrdinalIgnoreCase) && results.Count == 0))
                return (results, false);

            if (DateTime.UtcNow >= deadline)
                return (results, true);

            await Task.Delay(250);
        }
    }

    internal async Task<(ElementInfo? Element, int Count)> ResolveTargetAsync(
        string? automationId, string? type, string? text, int index,
        ElementActionKind? actionKind, bool preferActionable)
    {
        var results = await client.QueryAsync(type, automationId, text);
        if (results.Count == 0 || index >= results.Count)
            return (null, results.Count);

        var selected = results[index];
        if (!preferActionable || actionKind is null || GetActionTargetScore(selected, actionKind.Value) > 0)
            return (selected, results.Count);

        var actionable = results
            .Select((element, resultIndex) => new
            {
                Element = element,
                ResultIndex = resultIndex,
                Score = GetActionTargetScore(element, actionKind.Value)
            })
            .Where(candidate =>
                candidate.ResultIndex != index
                && candidate.Score > 0
                && IsRelatedElementRepresentation(selected, candidate.Element))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ResultIndex)
            .Select(candidate => candidate.Element)
            .FirstOrDefault()
            ?? selected;

        return (actionable, results.Count);
    }

    internal Task<LayoutInspectionResult?> AnalyzeLayoutAsync(
        string profile, string? rootElementId, string? checks, string minimumSeverity,
        bool includeEvidence, bool includePasses, int stabilityTimeoutMs, bool immediate,
        int? window, LayoutDiagnosticsPolicy policy, CancellationToken cancellationToken)
    {
        var request = new LayoutInspectionRequest
        {
            Profile = profile,
            MinimumSeverity = minimumSeverity,
            IncludeEvidence = includeEvidence,
            IncludePasses = includePasses,
            Scope = new LayoutInspectionScope
            {
                RootElementId = rootElementId,
                Window = window
            },
            Stability = new LayoutStabilityOptions
            {
                Mode = immediate ? "immediate" : "wait",
                TimeoutMs = stabilityTimeoutMs
            },
            Rules = string.IsNullOrWhiteSpace(checks)
                ? null
                : checks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Suppressions = policy.Suppressions.ToList()
        };

        return client.AnalyzeLayoutAsync(request, cancellationToken);
    }

    internal async Task InspectLayoutAsync(
        string profile, string? rootElementId, string? checks, string minimumSeverity,
        bool includeEvidence, bool includePasses, int stabilityTimeoutMs, bool immediate,
        int? window, LayoutDiagnosticsPolicy policy, bool watch,
        Action<LayoutInspectionResult?> inspected, CancellationToken cancellationToken)
    {
        do
        {
            var result = await AnalyzeLayoutAsync(profile, rootElementId, checks,
                minimumSeverity, includeEvidence, includePasses, stabilityTimeoutMs, immediate,
                window, policy, cancellationToken);
            inspected(result);

            if (result is null || !watch)
                return;
            await Task.Delay(1000, cancellationToken);
        }
        while (!cancellationToken.IsCancellationRequested);
    }

    private static (long? CaptureEpoch, long? RegistryGeneration) GetCaptureMetadata(ElementInfo element) =>
        element.CaptureEpoch > 0
            ? (element.CaptureEpoch, element.RegistryGeneration)
            : (null, null);

    private static bool IsRelatedElementRepresentation(ElementInfo selected, ElementInfo candidate)
    {
        if (string.Equals(candidate.OwnerId, selected.Id, StringComparison.Ordinal)
            || string.Equals(selected.OwnerId, candidate.Id, StringComparison.Ordinal))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(selected.AutomationId)
            && string.Equals(
                selected.AutomationId,
                candidate.AutomationId,
                StringComparison.Ordinal)
            && IsNativeOrSynthetic(candidate);
    }

    private static bool IsNativeOrSynthetic(ElementInfo element) =>
        string.Equals(element.Origin, "native", StringComparison.OrdinalIgnoreCase)
            || element.Id.StartsWith("native:", StringComparison.Ordinal)
            || element.FullType.StartsWith(
                "Microsoft.Maui.DevFlow.Agent.Core.",
                StringComparison.Ordinal);

    private static int GetActionTargetScore(ElementInfo element, ElementActionKind actionKind)
    {
        var score = 0;
        var capabilities = element.Capabilities ?? [];
        var traits = element.Traits ?? [];
        var gestures = element.Gestures ?? [];

        switch (actionKind)
        {
            case ElementActionKind.Tap:
                if (capabilities.Any(capability => capability.Equals("invoke", StringComparison.OrdinalIgnoreCase)))
                    score += 100;
                if (element.Role is "button" or "link" or "menuitem" or "tab" or "checkbox" or "radio" or "switch")
                    score += 60;
                if (element.Type is "Button" or "ImageButton" or "CheckBox" or "Switch" or "RadioButton"
                    or "ToolbarItem" or "BackButton" or "FlyoutButton" or "FlyoutItem" or "FlyoutToggle"
                    or "Tab" or "MenuItem" or "Picker" or "DatePicker" or "TimePicker"
                    or "ShellContent" or "ShellSection")
                {
                    score += 50;
                }
                if (gestures.Any(gesture => gesture.Contains("tap", StringComparison.OrdinalIgnoreCase)))
                    score += 40;
                break;

            case ElementActionKind.TextInput:
                if (capabilities.Any(capability => capability.Equals("set-value", StringComparison.OrdinalIgnoreCase)))
                    score += 100;
                if (element.Role is "textbox" or "searchbox")
                    score += 60;
                if (element.Type is "Entry" or "Editor" or "SearchBar" or "SearchHandler"
                    or "TextField" or "TextBox" or "TextArea" or "TextView"
                    or "UITextField" or "UITextView" or "EditText" or "NSTextField")
                {
                    score += 50;
                }
                break;

            case ElementActionKind.Focus:
                if (capabilities.Any(capability => capability.Equals("focus", StringComparison.OrdinalIgnoreCase)))
                    score += 100;
                if (traits.Any(trait => trait.Equals("focusable", StringComparison.OrdinalIgnoreCase)))
                    score += 60;
                if (element.Type is "Entry" or "Editor" or "SearchBar" or "SearchHandler"
                    or "Picker" or "DatePicker" or "TimePicker")
                {
                    score += 50;
                }
                break;

            case ElementActionKind.Gesture:
                if (gestures.Count > 0)
                    score += 100;
                if (traits.Any(trait => trait.Equals("scrollable", StringComparison.OrdinalIgnoreCase)))
                    score += 40;
                break;
        }

        if (score == 0)
            return 0;
        if (element.IsVisible)
            score += 4;
        if (element.IsEnabled)
            score += 4;
        if (element.Id.StartsWith("native:registered:", StringComparison.Ordinal))
            score += 20;
        if (element.FullType.StartsWith("Microsoft.Maui.DevFlow.Agent.Core.", StringComparison.Ordinal))
            score += 15;

        return score;
    }
}
