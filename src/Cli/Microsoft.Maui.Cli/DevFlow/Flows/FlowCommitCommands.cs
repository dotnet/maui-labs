using System.CommandLine;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Testing = Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// Re-binds a plan sidecar to the current bytes of the Markdown flow it describes.
/// </summary>
/// <remarks>
/// <para>
/// <c>flow run</c> refuses a bundle whose sidecar digest no longer matches the flow, which is
/// correct: a plan is a review artifact and must describe the exact flow that was reviewed. What
/// was missing is the other half of that contract - a supported way for the author who just edited
/// their own flow to record the new bytes. Without it the only route was to load the shipping
/// assembly and call <c>MauiFlowRunReportSerializer.ComputeFlowDigest</c> by reflection, which is
/// neither discoverable nor reviewable.
/// </para>
/// <para>
/// The safety property is unchanged: nothing here relaxes the check in
/// <c>CommittedFlowBundleLoader</c>, a stale sidecar still refuses to run, and the flow itself must
/// still parse and validate before it can be blessed. What changes is that re-blessing becomes an
/// explicit, auditable act that also drops any approval bound to the superseded bytes, so an
/// approval of the old flow can never be carried silently onto the new one.
/// </para>
/// </remarks>
internal static class FlowCommitCommands
{
    private const long MaximumFileBytes = 1_048_576;

    internal static Command Create(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output,
        Action markError)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(markError);

        var command = new Command(
            "commit",
            "Re-bind a plan sidecar to the current bytes of its Markdown flow after an authored edit");
        var fileArgument = new Argument<string>("file")
        {
            Description = "Committed Markdown flow path; the matching .maui-plan.json sidecar is updated in place",
        };
        var planOption = new Option<string?>("--plan")
        {
            Description = "Matching plan sidecar path (defaults to <flow-base>.maui-plan.json beside the flow)",
        };
        var checkOption = new Option<bool>("--check")
        {
            Description = "Report whether the sidecar is current without writing to it; exits nonzero when it is stale",
        };
        command.Add(fileArgument);
        command.Add(planOption);
        command.Add(checkOption);

        command.SetAction(async (ctx, ct) =>
        {
            var json = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            try
            {
                var result = await ExecuteAsync(
                    ctx.GetValue(fileArgument)!,
                    ctx.GetValue(planOption),
                    ctx.GetValue(checkOption),
                    ct).ConfigureAwait(false);
                output.WriteResult(result, json, static value =>
                {
                    Console.WriteLine(value.Message);
                    Console.WriteLine($"Flow digest: {value.Digest}");
                    Console.WriteLine($"Plan: {value.PlanPath}");
                    if (value.RemovedApprovals > 0)
                        Console.WriteLine($"Approvals dropped (bound to the superseded flow): {value.RemovedApprovals}");
                });
                if (!result.Ok)
                    markError();
            }
            catch (FlowCommitException ex)
            {
                output.WriteError(ex.Message, json, ex.Code);
                markError();
            }
        });

        return command;
    }

    internal static async Task<FlowCommitCliResult> ExecuteAsync(
        string flowPath,
        string? planPath,
        bool checkOnly,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(flowPath))
            throw new FlowCommitException("flow-path-required", "A Markdown flow path is required.");
        if (!flowPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw new FlowCommitException("flow-invalid", "The committed flow must be a .md file.");

        var fullFlowPath = Path.GetFullPath(flowPath);
        if (!File.Exists(fullFlowPath))
            throw new FlowCommitException("flow-missing", $"The flow '{flowPath}' does not exist.");

        var expectedPlanPath = Path.Combine(
            Path.GetDirectoryName(fullFlowPath)!,
            Path.GetFileNameWithoutExtension(fullFlowPath) + ".maui-plan.json");
        var fullPlanPath = string.IsNullOrWhiteSpace(planPath)
            ? expectedPlanPath
            : Path.GetFullPath(planPath);
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(fullPlanPath),
                Path.TrimEndingDirectorySeparator(expectedPlanPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new FlowCommitException(
                "plan-sidecar-mismatch",
                "The plan must be the matching .maui-plan.json sidecar beside the committed Markdown flow.");
        }
        if (!File.Exists(fullPlanPath))
        {
            throw new FlowCommitException(
                "plan-missing",
                $"No plan sidecar was found at '{fullPlanPath}'. Author the plan first; commit only re-binds an existing one.");
        }

        var markdown = await ReadBoundedTextAsync(fullFlowPath, cancellationToken).ConfigureAwait(false);
        var parsed = Testing.FlowMarkdown.Parse(markdown, fullFlowPath);
        if (!parsed.Ok || parsed.Flow is null)
        {
            throw new FlowCommitException(
                "flow-invalid",
                parsed.Error ?? "The committed Markdown flow could not be parsed.");
        }

        // Refuse to bless a flow that would not be accepted for execution. Re-binding is only a
        // convenience for an edit the author already believes in, never a way to record a
        // digest for something that cannot run.
        var flowValidation = Testing.FlowValidator.Validate(parsed.Flow);
        if (!flowValidation.Ok)
        {
            throw new FlowCommitException(
                "flow-invalid",
                "The committed Markdown flow failed validation: " + string.Join("; ", flowValidation.Errors));
        }

        var digest = Testing.MauiFlowRunReportSerializer.ComputeFlowDigest(parsed.Flow);
        var flowFileName = Path.GetFileName(fullFlowPath);
        var flowId = ReadExtensionString(parsed.Flow.ExtensionData, "flowId");
        var flowRevision = ReadExtensionInt(parsed.Flow.ExtensionData, "revision");

        var planText = await ReadBoundedTextAsync(fullPlanPath, cancellationToken).ConfigureAwait(false);
        JsonObject document;
        try
        {
            document = JsonNode.Parse(planText)?.AsObject()
                ?? throw new FlowCommitException("plan-invalid", $"'{fullPlanPath}' is not a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new FlowCommitException("plan-invalid", $"'{fullPlanPath}' is not valid JSON: {ex.Message}");
        }

        if (document["flow"] is not JsonObject reference)
        {
            reference = new JsonObject();
            document["flow"] = reference;
        }

        var previousDigest = ReadString(reference, "digest");
        var current =
            string.Equals(ReadString(reference, "path"), flowFileName, StringComparison.Ordinal) &&
            string.Equals(previousDigest, digest, StringComparison.OrdinalIgnoreCase) &&
            IdentityMatches(reference, flowId, flowRevision);

        if (current)
        {
            return new FlowCommitCliResult
            {
                Ok = true,
                Changed = false,
                FlowPath = fullFlowPath,
                PlanPath = fullPlanPath,
                Digest = digest,
                PreviousDigest = previousDigest,
                Message = "The plan sidecar already matches the committed flow.",
            };
        }

        if (checkOnly)
        {
            return new FlowCommitCliResult
            {
                Ok = false,
                Changed = false,
                FlowPath = fullFlowPath,
                PlanPath = fullPlanPath,
                Digest = digest,
                PreviousDigest = previousDigest,
                Message = "The plan sidecar is stale. Run 'maui devflow flow commit' without --check to re-bind it.",
            };
        }

        reference["path"] = flowFileName;
        reference["digest"] = digest;
        if (flowId is not null)
            reference["flowId"] = flowId;
        if (flowRevision is not null)
            reference["revision"] = flowRevision.Value;

        // An approval names the bytes it approved. Once those bytes change, carrying the approval
        // forward would assert a review that never happened, so a superseded approval is dropped
        // and the count is reported rather than silently absorbed.
        var removedApprovals = DropSupersededApprovals(document, digest);

        var serialized = document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        // Prove the rewritten sidecar is one `flow run` will accept *before* it replaces the
        // author's file. Validating after the write would leave a rejected sidecar on disk while
        // reporting failure, destroying plan content the operator still believes they have.
        var verification = Testing.MauiTestPlanValidator.ValidateJson(serialized, out var plan);
        if (!verification.IsValid || plan is null)
        {
            throw new FlowCommitException(
                "plan-invalid",
                "The re-bound plan sidecar is not valid, so the existing sidecar was left untouched: "
                    + string.Join("; ", verification.Errors));
        }

        await WriteAtomicAsync(fullPlanPath, serialized + Environment.NewLine, cancellationToken)
            .ConfigureAwait(false);

        return new FlowCommitCliResult
        {
            Ok = true,
            Changed = true,
            FlowPath = fullFlowPath,
            PlanPath = fullPlanPath,
            Digest = digest,
            PreviousDigest = previousDigest,
            RemovedApprovals = removedApprovals,
            Message = "The plan sidecar is now bound to the committed flow.",
        };
    }

    private static bool IdentityMatches(JsonObject reference, string? flowId, int? flowRevision)
    {
        if (flowId is not null && !string.Equals(ReadString(reference, "flowId"), flowId, StringComparison.Ordinal))
            return false;
        if (flowRevision is not null && ReadInt(reference, "revision") != flowRevision)
            return false;
        return true;
    }

    /// <summary>
    /// Reads a JSON string member without throwing on a wrong type. A sidecar is operator-authored
    /// text, so a numeric <c>"digest"</c> is an input the command has to report, not crash on.
    /// </summary>
    private static string? ReadString(JsonObject owner, string name)
        => owner[name] is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;

    private static int? ReadInt(JsonObject owner, string name)
        => owner[name] is JsonValue value &&
           value.GetValueKind() == JsonValueKind.Number &&
           value.TryGetValue<int>(out var parsed)
            ? parsed
            : null;

    private static async Task WriteAtomicAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("n");
        try
        {
            await File.WriteAllTextAsync(temporary, contents, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
            throw;
        }
    }

    /// <summary>
    /// Retains an approval only when it names the new flow bytes. An approval with no digest, a
    /// blank digest, or a non-object entry cannot vouch for any particular bytes, so carrying it
    /// forward would assert a review that never happened; it is dropped and counted like any other
    /// superseded approval.
    /// </summary>
    private static int DropSupersededApprovals(JsonObject document, string digest)
    {
        if (document["approvals"] is not JsonArray approvals)
            return 0;

        var retained = new JsonArray();
        var removed = 0;
        foreach (var entry in approvals.ToArray())
        {
            approvals.Remove(entry);
            var bound = entry is JsonObject approval ? ReadString(approval, "digest") : null;
            if (string.IsNullOrWhiteSpace(bound) ||
                !string.Equals(bound, digest, StringComparison.OrdinalIgnoreCase))
            {
                removed++;
                continue;
            }
            retained.Add(entry);
        }
        document["approvals"] = retained;
        return removed;
    }

    private static async Task<string> ReadBoundedTextAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length > MaximumFileBytes)
            throw new FlowCommitException("file-too-large", $"'{path}' exceeds the supported size limit.");
        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadExtensionString(Dictionary<string, JsonElement>? data, string name)
        => data is not null &&
           data.TryGetValue(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadExtensionInt(Dictionary<string, JsonElement>? data, string name)
        => data is not null &&
           data.TryGetValue(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}

internal sealed class FlowCommitException : Exception
{
    public FlowCommitException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
