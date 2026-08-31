using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.DevFlow.Testing;

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
            using var document = JsonDocument.Parse(jsonText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return FlowParseResult.Fail("The maui-test block must contain a JSON object.", file);
            var normalizedJson = NormalizeLegacySchema(document.RootElement, out var schemaError);
            if (schemaError is not null)
                return FlowParseResult.Fail(schemaError, file);

            using var normalizedDocument = JsonDocument.Parse(normalizedJson);
            if (!TryGetProperty(normalizedDocument.RootElement, "schema", out var schema) ||
                schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out _))
            {
                return FlowParseResult.Fail("The maui-test block requires an integer schema.", file);
            }
            if (!TryGetProperty(normalizedDocument.RootElement, "steps", out var steps) ||
                steps.ValueKind != JsonValueKind.Array)
            {
                return FlowParseResult.Fail("The maui-test block requires a steps[] array.", file);
            }

            flow = JsonSerializer.Deserialize(
                normalizedDocument.RootElement.GetRawText(),
                MauiFlowJsonContext.Default.MauiFlow);
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

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeLegacySchema(JsonElement root, out string? error)
    {
        error = null;
        if (TryGetProperty(root, "schema", out _))
            return root.GetRawText();

        if (!TryGetProperty(root, "schemaVersion", out var legacySchema))
            return root.GetRawText();

        if (legacySchema.ValueKind != JsonValueKind.Number || !legacySchema.TryGetInt32(out var schema))
        {
            error = "Legacy maui-test blocks using schemaVersion must be migrated to an integer schema field before loading. Replace schemaVersion with schema or re-save the flow from a current DevFlow host.";
            return root.GetRawText();
        }

        var node = JsonNode.Parse(root.GetRawText()) as JsonObject;
        if (node is null)
        {
            error = "Legacy maui-test blocks using schemaVersion must be migrated to an integer schema field before loading. Replace schemaVersion with schema or re-save the flow from a current DevFlow host.";
            return root.GetRawText();
        }

        node["schema"] = schema;
        node.Remove("schemaVersion");
        return node.ToJsonString();
    }

    /// <summary>Render a flow as a dual-layer <c>.md</c> (human prose + authoritative json block).</summary>
    public static string Serialize(MauiFlow flow)
    {
        var json = JsonSerializer.Serialize(flow, MauiFlowJsonContext.Default.MauiFlow);
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
            if (s.Args?.SecretEnvironmentVariable is not null) flags.Add("secret-input");
            var suffix = flags.Count > 0 ? $"  _({string.Join(", ", flags)})_" : "";
            sb.Append(s.Seq).Append(". ").Append(NoFence(Label(s))).Append(suffix).Append('\n');
            foreach (var a in s.Asserts ?? Enumerable.Empty<FlowAssert>())
            {
                switch (a.Kind)
                {
                    case "propEquals": sb.Append("   - Expect ").Append(NoFence(a.Name)).Append(" == \"").Append(NoFence(a.Expected)).Append("\"\n"); break;
                    case "exists": sb.Append("   - Expect target still present\n"); break;
                    case "notExists": sb.Append("   - Expect target no longer present\n"); break;
                    case "routeIs": sb.Append("   - Expect route ").Append(NoFence(a.Expected)).Append('\n'); break;
                    case "pageChanged": sb.Append("   - Note: screen changed\n"); break;
                }
            }
        }
        sb.Append('\n').Append("## Replay (machine-readable — source of truth)\n\n");
        sb.Append("```json maui-test\n").Append(json).Append("\n```\n");
        return sb.ToString();
    }

    /// <summary>
    /// Replaces only the authoritative JSON payload in an existing valid flow document. This
    /// preserves user-authored prose and unknown Markdown outside the fenced payload while using
    /// the canonical Testing serializer for the executable content.
    /// </summary>
    public static string? ReplaceAuthoritativePayload(string? markdown, MauiFlow? flow)
    {
        if (string.IsNullOrWhiteSpace(markdown) || flow is null)
            return null;

        var matches = BlockRegex.Matches(markdown);
        if (matches.Count != 1)
            return null;

        var payload = matches[0].Groups[1];
        var json = JsonSerializer.Serialize(flow, MauiFlowJsonContext.Default.MauiFlow);
        return string.Concat(
            markdown.AsSpan(0, payload.Index),
            json,
            markdown.AsSpan(payload.Index + payload.Length));
    }

    internal static string Label(FlowStep s)
    {
        if (!string.IsNullOrWhiteSpace(s.Label))
            return s.Label.Trim();
        var who = Who(s.Target);
        return s.Action switch
        {
            FlowActions.Tap => $"Tap {who}".Trim(),
            FlowActions.Fill when s.Args?.SecretEnvironmentVariable is { } variable =>
                $"Fill {who} from environment variable {variable}".Trim(),
            FlowActions.Fill => $"Fill {who} = \"{s.Value}\"".Trim(),
            FlowActions.SetProperty when s.Args?.SecretEnvironmentVariable is { } variable =>
                $"Set {who} property from environment variable {variable}".Trim(),
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
