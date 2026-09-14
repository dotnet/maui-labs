using Microsoft.Maui.DevFlow.Driver;
using System.Globalization;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

public sealed class FlowAssertResult
{
    public string Kind { get; set; } = "";
    /// <summary>true = passed, false = failed, null = not evaluated (report-only or skipped).</summary>
    public bool? Ok { get; set; }
    public bool Skipped { get; set; }
    public string? Name { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
}

public sealed class FlowStepResult
{
    public int Seq { get; set; }
    public string Action { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public List<FlowAssertResult> Asserts { get; set; } = new();
}

public sealed class FlowReplayReport
{
    public bool Ok { get; set; }
    public string Name { get; set; } = "scenario";
    public string? File { get; set; }
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public List<FlowStepResult> Results { get; set; } = new();
}

/// <summary>
/// Replays a recorded <see cref="MauiFlow"/> against a running app via <see cref="AgentClient"/>,
/// producing a per-step pass/fail report. Uses durable-selector resolution, drive-then-verify,
/// and polling of hard assertions (propEquals/exists) with re-resolution each
/// attempt to tolerate async navigation. A failed drive short-circuits that step's assertions.
/// </summary>
public sealed class FlowReplayer
{
    private readonly AgentClient _agent;
    private readonly int _pollTries;
    private readonly int _pollGapMs;

    public FlowReplayer(AgentClient agent, int pollTries = 4, int pollGapMs = 300)
    {
        _agent = agent;
        _pollTries = Math.Max(1, pollTries);
        _pollGapMs = Math.Max(0, pollGapMs);
    }

    public async Task<FlowReplayReport> ReplayAsync(MauiFlow flow, string? file = null, CancellationToken ct = default)
    {
        var report = new FlowReplayReport { Name = flow.Name, File = file, Total = flow.Steps.Count };

        foreach (var step in flow.Steps)
        {
            ct.ThrowIfCancellationRequested();
            var res = new FlowStepResult { Seq = step.Seq, Action = step.Action, Label = FlowMarkdown.Label(step) };

            var (driveOk, driveError) = await DriveAsync(step, ct);
            if (!driveOk)
            {
                // Causality: don't run assertions when the action itself didn't happen.
                res.Ok = false;
                res.Error = driveError;
                foreach (var a in step.Asserts ?? Enumerable.Empty<FlowAssert>())
                    res.Asserts.Add(new FlowAssertResult { Kind = a.Kind, Skipped = true, Name = a.Name, Expected = a.Expected });
            }
            else
            {
                res.Ok = true;
                foreach (var a in step.Asserts ?? Enumerable.Empty<FlowAssert>())
                {
                    if (!a.Verify)
                    {
                        res.Asserts.Add(new FlowAssertResult { Kind = a.Kind, Skipped = true, Name = a.Name, Expected = a.Expected });
                        continue;
                    }
                    var ar = await VerifyAsync(a, ct);
                    res.Asserts.Add(ar);
                    if (ar.Ok == false) res.Ok = false;
                }
            }

            if (res.Ok) report.Passed++;
            else report.Failed++;
            report.Results.Add(res);
        }

        report.Ok = report.Failed == 0;
        return report;
    }

    private async Task<(bool Ok, string? Error)> DriveAsync(FlowStep step, CancellationToken ct)
    {
        try
        {
            var args = step.Args;
            switch (step.Action)
            {
                case FlowActions.Tap:
                {
                    var id = await ResolveDriveTargetAsync(FlowValidator.EffectiveSelector(step), ct);
                    if (id is null) return (false, "tap target could not be resolved");
                    return await _agent.TapAsync(id) ? (true, null) : (false, "tap reported failure");
                }
                case FlowActions.Fill:
                {
                    var id = await ResolveDriveTargetAsync(FlowValidator.EffectiveSelector(step), ct);
                    if (id is null) return (false, "fill target could not be resolved");
                    return await _agent.FillAsync(id, args?.Text ?? step.Value ?? "") ? (true, null) : (false, "fill reported failure");
                }
                case FlowActions.SetProperty:
                {
                    var id = await ResolveDriveTargetAsync(FlowValidator.EffectiveSelector(step), ct);
                    if (id is null) return (false, "setProperty target could not be resolved");
                    var name = string.IsNullOrEmpty(args?.Name) ? "Text" : args!.Name!;
                    return await _agent.SetPropertyAsync(id, name, args?.Value ?? step.Value ?? "")
                        ? (true, null) : (false, "setProperty reported failure");
                }
                case FlowActions.Scroll:
                {
                    string? id = null;
                    var sel = FlowValidator.EffectiveSelector(step);
                    if (sel is not null && !sel.IsEmpty)
                    {
                        id = await ResolveDriveTargetAsync(sel, ct);
                        if (id is null) return (false, "scroll target could not be resolved");
                    }
                    var ok = await _agent.ScrollAsync(
                        elementId: id,
                        deltaX: args?.Dx ?? 0,
                        deltaY: args?.Dy ?? 0,
                        animated: args?.Animated ?? false,
                        itemIndex: args?.ItemIndex,
                        scrollToPosition: args?.Position);
                    return ok ? (true, null) : (false, "scroll reported failure");
                }
                case FlowActions.Navigate:
                {
                    var route = args?.Route ?? step.Value;
                    if (string.IsNullOrEmpty(route)) return (false, "navigate requires a route");
                    return await _agent.NavigateAsync(route) ? (true, null) : (false, "navigate reported failure");
                }
                case FlowActions.Back:
                    return await _agent.BackAsync() ? (true, null) : (false, "back reported failure");
                case FlowActions.Assert:
                    // Validation-only step: nothing to drive, so the step's assertions run next.
                    return (true, null);
                case FlowActions.SetTheme:
                {
                    var themeStr = args?.Theme ?? step.Value;
                    if (!TryParseTheme(themeStr, out var theme))
                        return (false, $"setTheme requires light|dark|system (got '{themeStr ?? "(none)"}')");
                    var r = await _agent.SetThemeAsync(theme);
                    return (r?.Success ?? true) ? (true, null) : (false, r?.Message ?? "setTheme reported failure");
                }
                default:
                    return (false, $"unknown action: {step.Action}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"drive failed: {ex.Message}");
        }
    }

    private async Task<FlowAssertResult> VerifyAsync(FlowAssert a, CancellationToken ct)
    {
        var r = new FlowAssertResult { Kind = a.Kind, Name = a.Name, Expected = a.Expected };
        for (var attempt = 0; attempt < _pollTries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (a.Kind == "propEquals")
                {
                    // Re-resolve every attempt — the element id can change across a navigation.
                    var id = await ResolveToIdAsync(a.Selector, ct);
                    if (id is not null)
                    {
                        var val = await _agent.GetPropertyAsync(id, string.IsNullOrEmpty(a.Name) ? "Text" : a.Name!);
                        r.Actual = val;
                        if (PropertyValuesEqual(val, a.Expected))
                        {
                            r.Ok = true;
                            return r;
                        }
                    }
                }
                else if (a.Kind == "exists")
                {
                    var id = await ResolveToIdAsync(a.Selector, ct);
                    if (id is not null)
                    {
                        r.Ok = true;
                        return r;
                    }
                }
                else
                {
                    // Unknown verify:true kind — don't fail the step on something we can't evaluate.
                    r.Skipped = true;
                    return r;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // fall through to retry
            }
            if (attempt < _pollTries - 1)
                await Task.Delay(_pollGapMs, ct);
        }
        r.Ok = false;
        return r;
    }

    internal static bool PropertyValuesEqual(string? actual, string? expected)
    {
        var left = (actual ?? string.Empty).Trim();
        var right = (expected ?? string.Empty).Trim();
        if (string.Equals(left, right, StringComparison.Ordinal)) return true;

        if (bool.TryParse(left, out var leftBool) &&
            bool.TryParse(right, out var rightBool))
            return leftBool == rightBool;

        if (decimal.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber) &&
            decimal.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber))
            return leftNumber == rightNumber;

        if (!TryNormalizeReadColor(left, out var actualRgba) ||
            !TryNormalizeExpectedColor(right, out var expectedRgba, out var expectedArgbAsRgba))
            return false;

        return string.Equals(actualRgba, expectedRgba, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actualRgba, expectedArgbAsRgba, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeReadColor(string value, out string rgba)
    {
        rgba = string.Empty;
        if (!value.StartsWith('#')) return false;
        var hex = value[1..];
        if (!hex.All(Uri.IsHexDigit)) return false;
        if (hex.Length == 6) rgba = hex + "FF";
        else if (hex.Length == 8) rgba = hex;
        else return false;
        return true;
    }

    private static bool TryNormalizeExpectedColor(string value, out string rgba, out string argbAsRgba)
    {
        rgba = string.Empty;
        argbAsRgba = string.Empty;
        if (!value.StartsWith('#')) return false;
        var hex = value[1..];
        if (!hex.All(Uri.IsHexDigit)) return false;
        if (hex.Length == 6)
        {
            rgba = hex + "FF";
            argbAsRgba = rgba;
            return true;
        }
        if (hex.Length != 8) return false;
        rgba = hex;
        argbAsRgba = hex[2..] + hex[..2];
        return true;
    }

    private async Task<string?> ResolveDriveTargetAsync(FlowSelector? selector, CancellationToken ct)
    {
        for (var attempt = 0; attempt < _pollTries; attempt++)
        {
            var id = await ResolveToIdAsync(selector, ct);
            if (id is not null)
                return id;
            if (attempt < _pollTries - 1)
                await Task.Delay(_pollGapMs, ct);
        }
        return null;
    }

    private async Task<string?> ResolveToIdAsync(FlowSelector? selector, CancellationToken ct)
    {
        if (selector is null || selector.IsEmpty) return null;
        ct.ThrowIfCancellationRequested();

        // Documented precedence: AutomationId > Text > TypeIndex (fragile) > raw Id (fragile).
        if (!string.IsNullOrEmpty(selector.AutomationId))
        {
            var r = await _agent.QueryAsync(automationId: selector.AutomationId);
            return r.Count > 0 ? r[0].Id : null;
        }
        if (!string.IsNullOrEmpty(selector.Text))
        {
            var r = await _agent.QueryAsync(text: selector.Text);
            return r.Count > 0 ? r[0].Id : null;
        }
        var typeIndex = selector.TypeIndex
            ?? (selector.SelectorKind == "typeIndex" && !string.IsNullOrEmpty(selector.Type) && selector.Index is not null
                ? new FlowTypeIndex { Type = selector.Type, Index = selector.Index.Value }
                : null);
        if (typeIndex is not null && !string.IsNullOrEmpty(typeIndex.Type))
        {
            var byType = await _agent.QueryAsync(type: typeIndex.Type);
            return typeIndex.Index >= 0 && typeIndex.Index < byType.Count ? byType[typeIndex.Index].Id : null;
        }
        if (!string.IsNullOrEmpty(selector.Id))
        {
            var el = await _agent.GetElementAsync(selector.Id);
            return el is not null ? selector.Id : null;
        }
        return null;
    }

    private static bool TryParseTheme(string? s, out DevFlowTheme theme)
    {
        theme = DevFlowTheme.System;
        if (string.IsNullOrWhiteSpace(s)) return false;
        switch (s.Trim().ToLowerInvariant())
        {
            case "light": theme = DevFlowTheme.Light; return true;
            case "dark": theme = DevFlowTheme.Dark; return true;
            case "system": theme = DevFlowTheme.System; return true;
            default: return false;
        }
    }
}
