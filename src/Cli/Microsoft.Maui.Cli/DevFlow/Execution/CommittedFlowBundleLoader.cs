using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal sealed class CommittedFlowBundleLoader
{
    private const long MaximumFileBytes = 1_048_576;

    public async Task<CommittedFlowBundle> LoadAsync(
        string flowPath,
        string? planPath,
        CancellationToken cancellationToken = default)
    {
        var fullFlowPath = ValidateFile(flowPath, ".md", "flow");
        var expectedPlanPath = Path.Combine(
            Path.GetDirectoryName(fullFlowPath)!,
            Path.GetFileNameWithoutExtension(fullFlowPath) + ".maui-plan.json");
        var fullPlanPath = string.IsNullOrWhiteSpace(planPath)
            ? expectedPlanPath
            : Path.GetFullPath(planPath);
        if (!PathsEqual(fullPlanPath, expectedPlanPath))
        {
            throw FlowExecutionException.Invalid(
                "plan-sidecar-mismatch",
                "The plan must be the matching .maui-plan.json sidecar beside the committed Markdown flow.");
        }

        fullPlanPath = ValidateFile(fullPlanPath, ".json", "plan sidecar");
        var markdown = await ReadBoundedTextAsync(fullFlowPath, cancellationToken).ConfigureAwait(false);
        var parsed = FlowMarkdown.Parse(markdown, fullFlowPath);
        if (!parsed.Ok || parsed.Flow is null)
        {
            throw FlowExecutionException.Invalid(
                "flow-invalid",
                parsed.Error ?? "The committed Markdown flow could not be parsed.");
        }

        var flowValidation = FlowValidator.Validate(parsed.Flow);
        if (!flowValidation.Ok)
        {
            throw FlowExecutionException.Invalid(
                "flow-invalid",
                "The committed Markdown flow failed validation: " + string.Join("; ", flowValidation.Errors));
        }

        var planJson = await ReadBoundedTextAsync(fullPlanPath, cancellationToken).ConfigureAwait(false);
        var planValidation = MauiTestPlanValidator.ValidateJson(planJson, out var plan);
        if (!planValidation.IsValid || plan is null)
        {
            throw FlowExecutionException.Invalid(
                "plan-invalid",
                "The committed plan sidecar failed validation: " + string.Join("; ", planValidation.Errors));
        }

        if (IsDraft(parsed.Flow.ExtensionData) || IsDraft(plan.ExtensionData))
        {
            throw FlowExecutionException.Invalid(
                "draft-not-executable",
                "Draft flows and plans are not executable. Commit the reviewed Markdown and matching sidecar first.");
        }

        var flowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(parsed.Flow);
        if (!string.Equals(plan.Flow?.Path, Path.GetFileName(fullFlowPath), StringComparison.Ordinal))
        {
            throw FlowExecutionException.Invalid(
                "plan-flow-path-stale",
                "The plan sidecar is not bound to the selected canonical Markdown filename.");
        }
        if (!string.Equals(plan.Flow?.Digest, flowDigest, StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Invalid(
                "plan-flow-digest-stale",
                "The plan sidecar references a stale flow digest. Review the edited flow, then run " +
                $"'maui devflow flow commit {Path.GetFileName(fullFlowPath)}' to re-bind the sidecar to the current bytes.");
        }

        var flowId = ReadExtensionString(parsed.Flow.ExtensionData, "flowId");
        var flowRevision = ReadExtensionInt(parsed.Flow.ExtensionData, "revision");
        if (!string.IsNullOrWhiteSpace(plan.Flow?.FlowId) || flowId is not null)
        {
            if (string.IsNullOrWhiteSpace(plan.Flow?.FlowId) ||
                string.IsNullOrWhiteSpace(flowId) ||
                !string.Equals(plan.Flow.FlowId, flowId, StringComparison.Ordinal))
            {
                throw FlowExecutionException.Invalid(
                    "plan-flow-id-stale",
                    "The plan and Markdown flow identities do not match.");
            }
        }
        if (plan.Flow?.Revision is not null || flowRevision is not null)
        {
            if (plan.Flow?.Revision != flowRevision)
            {
                throw FlowExecutionException.Invalid(
                    "plan-flow-revision-stale",
                    "The plan and Markdown flow revisions do not match.");
            }
        }

        return new CommittedFlowBundle
        {
            FlowPath = fullFlowPath,
            PlanPath = fullPlanPath,
            Flow = parsed.Flow,
            Plan = plan,
            FlowDigest = flowDigest,
            FlowId = plan.Flow?.FlowId ?? flowId,
            FlowRevision = plan.Flow?.Revision ?? flowRevision,
        };
    }

    private static string ValidateFile(string path, string extension, string kind)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw FlowExecutionException.Invalid($"{kind}-path-missing", $"A {kind} path is required.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw FlowExecutionException.Invalid($"{kind}-path-invalid", $"The {kind} path is invalid.");
        }

        if (!File.Exists(fullPath) ||
            !string.Equals(Path.GetExtension(fullPath), extension, StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Invalid(
                $"{kind}-not-found",
                $"The {kind} must be an existing {extension} file.");
        }

        var info = new FileInfo(fullPath);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw FlowExecutionException.Invalid($"{kind}-reparse-point", $"The {kind} cannot be a symbolic link or reparse point.");
        if (info.Length > MaximumFileBytes)
            throw FlowExecutionException.Invalid($"{kind}-too-large", $"The {kind} exceeds the 1 MB execution limit.");
        return fullPath;
    }

    private static async Task<string> ReadBoundedTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw FlowExecutionException.Invalid("workflow-read-failed", "The committed workflow bundle could not be read.");
        }
    }

    private static bool PathsEqual(string first, string second)
        => string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsDraft(Dictionary<string, JsonElement>? extensionData)
    {
        if (extensionData is null)
            return false;
        if (extensionData.TryGetValue("draft", out var draft) &&
            draft.ValueKind is JsonValueKind.True)
        {
            return true;
        }
        return extensionData.TryGetValue("status", out var status) &&
            status.ValueKind == JsonValueKind.String &&
            string.Equals(status.GetString(), "draft", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadExtensionString(Dictionary<string, JsonElement>? extensionData, string name)
        => extensionData is not null &&
           extensionData.TryGetValue(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadExtensionInt(Dictionary<string, JsonElement>? extensionData, string name)
        => extensionData is not null &&
           extensionData.TryGetValue(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var number)
            ? number
            : null;
}
