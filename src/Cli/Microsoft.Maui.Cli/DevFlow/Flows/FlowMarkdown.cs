using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

public sealed class FlowParseResult
{
    public bool Ok { get; private init; }
    public MauiFlow? Flow { get; private init; }
    public string? Error { get; private init; }
    public string? File { get; private init; }

    public static FlowParseResult Success(MauiFlow flow, string? file) => new() { Ok = true, Flow = flow, File = file };
    public static FlowParseResult Fail(string error, string? file = null) => new() { Ok = false, Error = error, File = file };
}

/// <summary>
/// Reads/writes the dual-layer flow-test <c>.md</c> format: human prose plus a fenced
/// <c>```json maui-test</c> block that is the authoritative replay payload. Compatible with
/// Canvas-recorded tests.
/// </summary>
public static class FlowMarkdown
{
    // Cap the embedded JSON so a hostile/huge file can't blow up parsing.
    private const int MaxBlockChars = 4 * 1024 * 1024;

    private static readonly Regex BlockRegex = new(
        "```json maui-test\\s*\\r?\\n(.*?)\\r?\\n```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Extract and deserialize the authoritative flow payload from a <c>.md</c> document.</summary>
    public static FlowParseResult Parse(string markdown, string? file = null)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return FlowParseResult.Fail("Empty flow document.", file);

        var matches = BlockRegex.Matches(markdown);
        if (matches.Count == 0)
            return FlowParseResult.Fail("No ```json maui-test``` block found in the test file.", file);
        if (matches.Count > 1)
            return FlowParseResult.Fail("Multiple ```json maui-test``` blocks found; exactly one is required.", file);

        var jsonText = matches[0].Groups[1].Value;
        if (jsonText.Length > MaxBlockChars)
            return FlowParseResult.Fail("The maui-test block is too large to parse.", file);

        MauiFlow? flow;
        try
        {
            flow = JsonSerializer.Deserialize<MauiFlow>(jsonText, ReadOptions);
        }
        catch (JsonException ex)
        {
            return FlowParseResult.Fail($"Invalid JSON in the maui-test block: {ex.Message}", file);
        }

        if (flow is null)
            return FlowParseResult.Fail("The maui-test block did not deserialize to a flow.", file);
        if (flow.Steps is null)
            return FlowParseResult.Fail("The maui-test block has no steps[].", file);
        if (string.IsNullOrWhiteSpace(flow.Name))
            flow.Name = "scenario";

        return FlowParseResult.Success(flow, file);
    }

    /// <summary>Render a flow as a dual-layer <c>.md</c> (human prose + authoritative json block).</summary>
    public static string Serialize(MauiFlow flow)
    {
        var json = JsonSerializer.Serialize(flow, WriteOptions);
        var sb = new StringBuilder();
        sb.Append("# Scenario: ").Append(NoFence(flow.Name)).Append('\n').Append('\n');
        sb.Append("<!-- Recorded by MAUI DevFlow. The fenced ```json maui-test block below is the source of\n");
        sb.Append("     truth for replay; edit the prose freely but keep that block valid. -->\n\n");
        sb.Append("- **App:** ").Append(NoFence(flow.App ?? "(unknown)")).Append('\n');
        sb.Append("- **Platform:** ").Append(NoFence(flow.Platform ?? "(unknown)")).Append('\n');
        sb.Append("- **Recorded:** ").Append(NoFence(flow.RecordedAt ?? "(unknown)")).Append('\n');
        sb.Append("- **Preconditions:** ").Append(NoFence(flow.Preconditions ?? "App is launched and on its start page.")).Append('\n');
        sb.Append("- **Steps:** ").Append(flow.Steps.Count).Append('\n');
        var fragile = flow.Steps.Count(s => s.Fragile);
        if (fragile > 0)
            sb.Append("- **Warning:** ").Append(fragile).Append(" step(s) use a fragile selector (no AutomationId). Add AutomationIds for durable tests.\n");
        sb.Append('\n').Append("## Steps\n\n");
        if (flow.Steps.Count == 0)
            sb.Append("_(no steps recorded)_\n");
        foreach (var s in flow.Steps)
        {
            var flags = new List<string>();
            if (s.Fragile) flags.Add("fragile-selector");
            if (s.Navigated) flags.Add("page-changed");
            var suffix = flags.Count > 0 ? $"  _({string.Join(", ", flags)})_" : "";
            sb.Append(s.Seq).Append(". ").Append(NoFence(Label(s))).Append(suffix).Append('\n');
            foreach (var a in s.Asserts ?? Enumerable.Empty<FlowAssert>())
            {
                switch (a.Kind)
                {
                    case "propEquals": sb.Append("   - Expect ").Append(NoFence(a.Name)).Append(" == \"").Append(NoFence(a.Expected)).Append("\"\n"); break;
                    case "exists": sb.Append("   - Expect target still present\n"); break;
                    case "routeIs": sb.Append("   - Expect route ").Append(NoFence(a.Expected)).Append('\n'); break;
                    case "pageChanged": sb.Append("   - Note: screen changed\n"); break;
                }
            }
        }
        sb.Append('\n').Append("## Replay (machine-readable — source of truth)\n\n");
        sb.Append("```json maui-test\n").Append(json).Append("\n```\n");
        return sb.ToString();
    }

    internal static string Label(FlowStep s)
    {
        var who = Who(s.Target);
        return s.Action switch
        {
            FlowActions.Tap => $"Tap {who}".Trim(),
            FlowActions.Fill => $"Fill {who} = \"{s.Value}\"".Trim(),
            FlowActions.SetProperty => $"Set {who} property = \"{s.Value}\"".Trim(),
            FlowActions.Scroll => $"Scroll {(string.IsNullOrEmpty(who) ? "view" : who)}".Trim(),
            FlowActions.Navigate => $"Navigate to {s.Value}".Trim(),
            FlowActions.Back => "Go back",
            FlowActions.SetTheme => $"Set theme to {s.Value}".Trim(),
            FlowActions.Assert => "Assert",
            _ => s.Action,
        };
    }

    private static string Who(FlowSelector? t)
    {
        if (t is null) return "";
        if (!string.IsNullOrEmpty(t.AutomationId)) return $"#{t.AutomationId}";
        if (!string.IsNullOrEmpty(t.Text)) return $"\"{t.Text}\"";
        return t.Type ?? t.Id ?? "element";
    }

    /// <summary>
    /// Neutralizes any run of 3+ backticks in a human-layer string so a recorded value can't inject
    /// a second <c>```json maui-test</c> fence and make the file unparsable. The authoritative JSON
    /// block is unaffected (System.Text.Json escapes newlines in values, so no fence can form there).
    /// </summary>
    private static string NoFence(string? s) =>
        string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, "`{3,}", "`");
}
