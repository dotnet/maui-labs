using System.CommandLine;
using System.Text;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

/// <summary>
/// <c>maui devflow evidence</c>: preview, capture, and view <c>.mauitrace</c> evidence bundles.
///
/// Every command follows the DevFlow CLI conventions — the shared <c>--json</c>/<c>--no-json</c>
/// options, <see cref="IDevFlowOutputWriter"/> for output, and structured errors on stderr.
/// </summary>
internal static class EvidenceCommands
{
    public static Command Create(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        Option<string> agentHostOption,
        Option<int> agentPortOption,
        IDevFlowOutputWriter output,
        Func<string, int, bool, Task<AgentClient>> clientFactory,
        Action onError)
    {
        var command = new Command("evidence",
            "Capture a shareable, redacted .mauitrace evidence bundle from the running app");

        command.Add(CreatePreviewCommand(jsonOption, noJsonOption, agentHostOption, agentPortOption, output, clientFactory, onError));
        command.Add(CreateCaptureCommand(jsonOption, noJsonOption, agentHostOption, agentPortOption, output, clientFactory, onError));
        command.Add(CreateViewCommand(jsonOption, noJsonOption, output, onError));
        return command;
    }

    private static Command CreatePreviewCommand(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        Option<string> agentHostOption,
        Option<int> agentPortOption,
        IDevFlowOutputWriter output,
        Func<string, int, bool, Task<AgentClient>> clientFactory,
        Action onError)
    {
        var screenshotOption = CreateScreenshotOption();
        var elementOption = CreateElementOption();
        var logLimitOption = CreateLogLimitOption();
        var networkLimitOption = CreateNetworkLimitOption();
        var workflowOption = CreateWorkflowOption();
        var outputOption = new Option<string?>("--output", "-o")
        {
            Description = $"Destination that 'capture' would use, echoed back in the plan (default: ./{EvidenceFormat.DefaultFolderName}/<app>-<timestamp>{EvidenceFormat.FileExtension})"
        };

        var command = new Command("preview",
            "Show exactly what an evidence capture would include and exclude, without writing anything")
        {
            screenshotOption,
            elementOption,
            logLimitOption,
            networkLimitOption,
            outputOption,
            workflowOption,
        };

        command.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            try
            {
                string? workflow = null;
                var workflowPath = ctx.GetValue(workflowOption);
                if (!string.IsNullOrWhiteSpace(workflowPath))
                {
                    var read = ReadWorkflowFile(workflowPath);
                    if (read.Error is not null)
                    {
                        output.WriteError(read.Error, isJson, "InvocationError");
                        onError();
                        return;
                    }
                    workflow = read.Text;
                }
                using var client = await clientFactory(
                    ctx.GetValue(agentHostOption)!,
                    ctx.GetValue(agentPortOption),
                    !isJson);
                var plan = await EvidenceCapture.PreviewAsync(client, new EvidenceRequest
                {
                    IncludeScreenshot = ctx.GetValue(screenshotOption),
                    SelectedElementId = ctx.GetValue(elementOption),
                    LogLimit = ctx.GetValue(logLimitOption),
                    NetworkLimit = ctx.GetValue(networkLimitOption),
                    OutputPath = ctx.GetValue(outputOption),
                    WorkflowMarkdown = workflow,
                    Source = "cli",
                }, ct);

                if (isJson) output.WriteRawJson(EvidenceJson.Serialize(plan, indented: true));
                else Console.WriteLine(FormatPlan(plan));
            }
            catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex))
            {
                output.WriteError(ex.Message, isJson, suggestions: ["Run 'maui devflow agent status' to confirm the selected app is connected"]);
                onError();
            }
        });

        return command;
    }

    private static Command CreateCaptureCommand(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        Option<string> agentHostOption,
        Option<int> agentPortOption,
        IDevFlowOutputWriter output,
        Func<string, int, bool, Task<AgentClient>> clientFactory,
        Action onError)
    {
        var outputOption = new Option<string?>("--output", "-o")
        {
            Description = $"Bundle path (default: ./{EvidenceFormat.DefaultFolderName}/<app>-<timestamp>{EvidenceFormat.FileExtension} under the project root)"
        };
        var overwriteOption = new Option<bool>("--overwrite")
        {
            Description = "Overwrite an existing bundle (default: fail if the file exists)",
            DefaultValueFactory = _ => false,
        };
        var screenshotOption = CreateScreenshotOption();
        var workflowOption = CreateWorkflowOption();
        var elementOption = CreateElementOption();
        var logLimitOption = CreateLogLimitOption();
        var networkLimitOption = CreateNetworkLimitOption();

        var command = new Command("capture", "Write a redacted evidence bundle for the connected app")
        {
            outputOption,
            overwriteOption,
            screenshotOption,
            workflowOption,
            elementOption,
            logLimitOption,
            networkLimitOption,
        };

        command.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            try
            {
                var workflowPath = ctx.GetValue(workflowOption);
                string? workflow = null;
                if (!string.IsNullOrWhiteSpace(workflowPath))
                {
                    var read = ReadWorkflowFile(workflowPath!);
                    if (read.Error is not null)
                    {
                        output.WriteError(read.Error, isJson, "InvocationError");
                        onError();
                        return;
                    }
                    workflow = read.Text;
                }

                using var client = await clientFactory(
                    ctx.GetValue(agentHostOption)!,
                    ctx.GetValue(agentPortOption),
                    !isJson);
                var result = await EvidenceCapture.CaptureAsync(client, new EvidenceRequest
                {
                    IncludeScreenshot = ctx.GetValue(screenshotOption),
                    Overwrite = ctx.GetValue(overwriteOption),
                    OutputPath = ctx.GetValue(outputOption),
                    WorkflowMarkdown = workflow,
                    SelectedElementId = ctx.GetValue(elementOption),
                    LogLimit = ctx.GetValue(logLimitOption),
                    NetworkLimit = ctx.GetValue(networkLimitOption),
                    Source = "cli",
                }, ct);

                if (!result.Ok)
                {
                    output.WriteError(result.Error ?? "Could not capture evidence.", isJson, "InvocationError");
                    onError();
                    return;
                }

                if (isJson) output.WriteRawJson(EvidenceJson.Serialize(result, indented: true));
                else Console.WriteLine(FormatCapture(result));
            }
            catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex))
            {
                output.WriteError(ex.Message, isJson, suggestions: ["Run 'maui devflow agent status' to confirm the selected app is connected"]);
                onError();
            }
        });

        return command;
    }

    private static Command CreateViewCommand(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output,
        Action onError)
    {
        var bundleArgument = new Argument<string>("bundle")
        {
            Description = $"Path to a {EvidenceFormat.FileExtension} bundle"
        };
        var noOpenOption = new Option<bool>("--no-open")
        {
            Description = "Generate the report without opening it in a browser",
            DefaultValueFactory = _ => false,
        };
        var reportOption = new Option<string?>("--output-report")
        {
            Description = "Write the generated HTML report to this .html path instead of a temporary file"
        };
        var overwriteOption = new Option<bool>("--overwrite")
        {
            Description = "Overwrite an existing report file",
            DefaultValueFactory = _ => false
        };

        var command = new Command("view",
            "Validate a bundle and generate a static, self-contained HTML report from its contents")
        {
            bundleArgument,
            noOpenOption,
            reportOption,
            overwriteOption,
        };

        command.SetAction((ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            try
            {
                var result = EvidenceCapture.View(
                    ctx.GetValue(bundleArgument)!,
                    ctx.GetValue(reportOption),
                    open: !ctx.GetValue(noOpenOption),
                    overwrite: ctx.GetValue(overwriteOption));

                if (!result.Ok)
                {
                    output.WriteError(result.Error ?? "Could not read the bundle.", isJson, "InvocationError");
                    onError();
                    return Task.CompletedTask;
                }

                if (isJson) output.WriteRawJson(EvidenceJson.Serialize(result, indented: true));
                else Console.WriteLine(FormatView(result));
            }
            catch (Exception ex) when (EvidenceCapture.IsExpectedFailure(ex))
            {
                output.WriteError(ex.Message, isJson);
                onError();
            }
            return Task.CompletedTask;
        });

        return command;
    }

    private static Option<bool> CreateScreenshotOption() => new("--include-screenshot")
    {
        Description = "Include a screenshot (off by default — a screenshot may show on-screen data)",
        DefaultValueFactory = _ => false,
    };

    private static Option<string?> CreateWorkflowOption() => new("--workflow")
    {
        Description = "Markdown reproduction steps to attach (max 1 MiB; secrets and structured values are scrubbed)"
    };

    private static Option<string?> CreateElementOption() => new("--element")
    {
        Description = "Element id to highlight in the bundle (metadata only; no text or values are captured)"
    };

    private static Option<int> CreateLogLimitOption() => new("--log-limit")
    {
        Description = $"Maximum log entries to include (1-{EvidenceFormat.MaxLogLimit})",
        DefaultValueFactory = _ => EvidenceFormat.DefaultLogLimit,
    };

    private static Option<int> CreateNetworkLimitOption() => new("--network-limit")
    {
        Description = $"Maximum network request summaries to include (1-{EvidenceFormat.MaxNetworkLimit})",
        DefaultValueFactory = _ => EvidenceFormat.DefaultNetworkLimit,
    };

    internal static (string? Text, string? Error) ReadWorkflowFile(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, "Workflow path is not a valid file path.");
        }

        if (!full.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return (null, "Workflow files must be Markdown (.md).");
        if (!File.Exists(full))
            return (null, $"Workflow file not found: {full}");

        try
        {
            using var input = File.OpenRead(full);
            if (input.Length > EvidenceFormat.MaxWorkflowBytes)
                return (null, $"Workflow file is larger than {EvidenceFormat.MaxWorkflowBytes / 1024} KB.");
            var buffer = new byte[checked((int)EvidenceFormat.MaxWorkflowBytes + 1)];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = input.Read(buffer, count, buffer.Length - count);
                if (read == 0) break;
                count += read;
            }
            if (count > EvidenceFormat.MaxWorkflowBytes)
                return (null, $"Workflow file is larger than {EvidenceFormat.MaxWorkflowBytes / 1024} KB.");
            using var content = new MemoryStream(buffer, 0, count, writable: false);
            using var reader = new StreamReader(content, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            return (reader.ReadToEnd(), null);
        }
        catch (DecoderFallbackException)
        {
            return (null, "The workflow file contains invalid text encoding.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"Could not read the workflow file: {ex.Message}");
        }
    }

    // ── human-readable formatting ────────────────────────────────────────────────────────────

    internal static string FormatPlan(EvidencePlan plan)
    {
        var text = new StringBuilder();
        text.AppendLine($"Evidence preview for {plan.App?.Name ?? "the connected app"} ({plan.Platform?.Name ?? "unknown platform"})");
        text.AppendLine($"  Format v{plan.FormatVersion} · redaction ruleset v{plan.RedactionVersion} · source {plan.Source}");
        if (plan.OutputPath is not null)
            text.AppendLine($"  Would write: {plan.OutputPath} (~{EvidenceReportRenderer.FormatBytes(plan.EstimatedBytes)})");

        if (plan.Environment is { } environment)
        {
            text.AppendLine("  Current environment (observations, not an enforced replay contract):");
            foreach (var (label, value) in EvidenceReportRenderer.EnvironmentFacts(environment))
            {
                if (!string.IsNullOrWhiteSpace(value))
                    text.AppendLine($"    {label}: {Terminal(value)}");
            }
            foreach (var gap in environment.Unavailable)
                text.AppendLine($"    Not captured/enforced - {gap.Name}: {Terminal(gap.Reason)}");
        }
        text.AppendLine("  Includes:");
        foreach (var entry in plan.Included)
        {
            var count = entry.Count is null ? "" : $" ({entry.Count} items)";
            text.AppendLine($"    - {entry.Name}{count}: {entry.Description}");
        }

        if (plan.Excluded.Count > 0)
        {
            text.AppendLine("  Excluded:");
            foreach (var exclusion in plan.Excluded)
                text.AppendLine($"    - {exclusion.Name}: {exclusion.Reason}");
        }

        text.AppendLine("  Never captured:");
        foreach (var item in plan.NeverIncluded)
            text.AppendLine($"    - {item}");

        text.AppendLine($"  Screenshot: {(plan.Screenshot.Included ? "included (opt-in)" : plan.Screenshot.OmittedReason ?? "not included")}");
        text.AppendLine($"  Limits: {plan.Limits.Logs} logs · {plan.Limits.Network} requests · {plan.Limits.TreeElements} elements");

        if (plan.Warnings.Count > 0)
        {
            text.AppendLine("  Warnings:");
            foreach (var warning in plan.Warnings)
                text.AppendLine($"    ! {warning}");
        }

        return text.ToString().TrimEnd();
    }

    internal static string FormatCapture(EvidenceCaptureResult result)
    {
        var text = new StringBuilder();
        text.AppendLine($"Evidence bundle written: {Terminal(result.Path)} ({EvidenceReportRenderer.FormatBytes(result.Bytes)})");
        if (result.Manifest is { } manifest)
        {
            foreach (var entry in manifest.Entries.Where(entry => entry.Count is not null))
                text.AppendLine($"  {entry.Name}: {entry.Count} captured item(s)");
            foreach (var exclusion in manifest.Excluded)
                text.AppendLine($"  Omitted {exclusion.Name}: {Terminal(exclusion.Reason)}");
        }
        text.AppendLine($"  Screenshot: {(result.Manifest?.Screenshot.Included == true ? "included" : "omitted")}");
        text.AppendLine($"  Redaction ruleset v{result.Manifest?.RedactionVersion}");
        if (result.Manifest?.Warnings is { Count: > 0 })
        {
            foreach (var warning in result.Manifest.Warnings)
                text.AppendLine($"  ! {Terminal(warning)}");
        }
        text.Append($"  View it with: maui devflow evidence view \"{Terminal(result.Path)}\"");
        return text.ToString();
    }

    internal static string FormatView(EvidenceViewResult result)
    {
        var text = new StringBuilder();
        text.AppendLine($"Report generated: {Terminal(result.Report)}");
        text.AppendLine($"  Bundle: {Terminal(result.Bundle)}");
        text.AppendLine($"  Entries: {string.Join(", ", result.Entries.Select(Terminal))}");
        if (result.Manifest is not null)
        {
            text.AppendLine($"  Captured {Terminal(result.Manifest.CapturedUtc)} by {Terminal(result.Manifest.Source)} · redaction ruleset v{result.Manifest.RedactionVersion}");
        }
        foreach (var warning in result.Warnings)
            text.AppendLine($"  ! {Terminal(warning)}");
        text.Append(result.Opened ? "  Opened in your default browser." : "  Open it in a browser to read the report.");
        return text.ToString();
    }

    private static string Terminal(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsControl(character) ||
                char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format)
            {
                builder.Append($"\\u{(int)character:X4}");
            }
            else
            {
                builder.Append(character);
            }
        }
        return builder.ToString();
    }
}
