using System.CommandLine;
using System.Text;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal static class FlowHandoffCommands
{
    public static Command CreateReproduce(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        Option<string> agentHostOption,
        Option<string?> deviceOption,
        Option<string> platformOption,
        IDevFlowOutputWriter output,
        Func<IFlowReproductionCoordinator> createCoordinator,
        Action onError)
    {
        var flowArgument = new Argument<string>("file")
        {
            Description = "Committed Markdown flow path; the matching .maui-plan.json sidecar is required"
        };
        var importOption = new Option<string?>("--import")
        {
            Description = "Imported flow-run.json or .mauitrace diagnostic artifact"
        };
        var kindOption = new Option<string?>("--kind")
        {
            Description = "Imported artifact kind when extension inference is unavailable: flow-run or mauitrace"
        };
        kindOption.AcceptOnlyFromAmong(ArtifactTrustImportKinds.FlowRun, ArtifactTrustImportKinds.Evidence);
        var planOption = new Option<string?>("--plan")
        {
            Description = "Matching plan sidecar path (defaults to <flow-base>.maui-plan.json beside the flow)"
        };
        var projectOption = new Option<string?>("--project")
        {
            Description = "MAUI app .csproj to build, deploy, and bind exactly"
        };
        var frameworkOption = new Option<string?>("--framework", "-f")
        {
            Description = "Platform target framework (required only when the project has multiple matching TFMs)"
        };
        var configurationOption = new Option<string>("--configuration", "-c")
        {
            Description = "App build configuration",
            DefaultValueFactory = _ => "Debug"
        };
        var outputOption = new Option<string?>("--output", "-o")
        {
            Description = "Required new or empty output directory for the local run and local-reproduction.json"
        };
        var cleanupOption = new Option<string>("--cleanup")
        {
            Description = "Owned app cleanup: none, stop, or uninstall (package removal applies only to a package newly installed by this invocation)",
            DefaultValueFactory = _ => FlowExecutionCleanupPolicies.Stop
        };
        cleanupOption.AcceptOnlyFromAmong(
            FlowExecutionCleanupPolicies.None,
            FlowExecutionCleanupPolicies.Stop,
            FlowExecutionCleanupPolicies.Uninstall);
        var agentWaitOption = new Option<int>("--agent-wait-seconds")
        {
            Description = "Maximum time to wait for a matching new agent instance",
            DefaultValueFactory = _ => 90
        };
        var evidenceOption = new Option<bool>("--evidence-on-failure")
        {
            Description = "Capture the existing redacted failure .mauitrace into the output directory"
        };

        var command = new Command(
            "reproduce",
            "Import bounded diagnostic evidence, run a new exact local execution, and stop after trust evaluation")
        {
            flowArgument,
            importOption,
            kindOption,
            planOption,
            projectOption,
            frameworkOption,
            configurationOption,
            outputOption,
            cleanupOption,
            agentWaitOption,
            evidenceOption,
        };
        command.SetAction(async (ctx, ct) =>
        {
            var json = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            try
            {
                var importedPath = ctx.GetValue(importOption);
                var outputDirectory = ctx.GetValue(outputOption);
                if (string.IsNullOrWhiteSpace(importedPath) || string.IsNullOrWhiteSpace(outputDirectory))
                {
                    output.WriteError(
                        "Flow reproduce requires --import and --output.",
                        json,
                        "InvalidArgument");
                    onError();
                    return;
                }

                var result = await createCoordinator().ReproduceAsync(new FlowReproductionRequest
                {
                    ImportedArtifactPath = importedPath,
                    ImportedArtifactKind = ctx.GetValue(kindOption),
                    Execution = new FlowExecutionRequest
                    {
                        FlowPath = ctx.GetValue(flowArgument)!,
                        PlanPath = ctx.GetValue(planOption),
                        ProjectPath = ctx.GetValue(projectOption) ?? "",
                        Platform = ctx.GetResult(platformOption)?.Tokens.Count > 0
                            ? ctx.GetValue(platformOption)!
                            : "android",
                        TargetFramework = ctx.GetValue(frameworkOption),
                        Configuration = ctx.GetValue(configurationOption)!,
                        AgentHost = ctx.GetValue(agentHostOption)!,
                        DeviceSerial = ctx.GetValue(deviceOption),
                        OutputDirectory = outputDirectory,
                        CleanupPolicy = ctx.GetValue(cleanupOption)!,
                        CaptureFailureEvidence = ctx.GetValue(evidenceOption),
                        AgentWaitTimeout = TimeSpan.FromSeconds(ctx.GetValue(agentWaitOption)),
                    },
                }, ct);

                if (json)
                {
                    output.WriteRawJson(Encoding.UTF8.GetString(
                        MauiLocalReproductionReportSerializer.SerializeToUtf8Bytes(result.Report)));
                }
                else
                {
                    Console.WriteLine(result.Report.Matched
                        ? "Local reproduction: exact match"
                        : "Local reproduction: no exact match");
                    Console.WriteLine($"Trust state: {result.Report.TrustState}");
                    Console.WriteLine($"Report: {MauiLocalReproductionReportSerializer.FileName}");
                    if (result.Report.Matched)
                    {
                        Console.WriteLine("Open the Inspector/Test Workbench repair review. No approval or proposal was created.");
                        Console.WriteLine("The CLI binding is not persisted in the broker; re-import the artifact for Workbench review.");
                    }
                }

                if (!result.Ok)
                    onError();
            }
            catch (FlowExecutionException ex)
            {
                output.WriteError(ex.Message, json, ex.ExitCategory);
                onError();
            }
            catch
            {
                output.WriteError(
                    "Local reproduction failed before a safe result could be written.",
                    json,
                    "InvocationError");
                onError();
            }
        });
        return command;
    }

    public static Command CreateTriage(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output,
        Func<IFlowTriageCoordinator> createCoordinator,
        Action onError)
    {
        var manifestOption = new Option<string?>("--manifest")
        {
            Description = "Bounded execution-manifest.json input"
        };
        var reportOption = new Option<string?>("--report")
        {
            Description = "Bounded flow-run.json input"
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "Deterministic output format: json or markdown",
            DefaultValueFactory = _ => FlowTriageOutputFormats.Json
        };
        formatOption.AcceptOnlyFromAmong(FlowTriageOutputFormats.Json, FlowTriageOutputFormats.Markdown);
        var outputOption = new Option<string?>("--output", "-o")
        {
            Description = "Optional new output file; existing files are never overwritten"
        };
        var command = new Command(
            "triage",
            "Create deterministic diagnostic-only triage from a bounded execution manifest and flow report")
        {
            manifestOption,
            reportOption,
            formatOption,
            outputOption,
        };
        command.SetAction(async (ctx, ct) =>
        {
            var json = output.ResolveJsonMode(
                ctx.GetValue(jsonOption),
                ctx.GetValue(noJsonOption));
            var format = ctx.GetValue(formatOption)!;
            if (json &&
                string.Equals(format, FlowTriageOutputFormats.Markdown, StringComparison.Ordinal))
            {
                output.WriteError(
                    "Flow triage cannot emit Markdown while effective JSON output is active. Use --format json or pass --no-json.",
                    json: true,
                    errorType: "InvalidArgument");
                onError();
                return;
            }

            var manifestPath = ctx.GetValue(manifestOption);
            var reportPath = ctx.GetValue(reportOption);
            if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(reportPath))
            {
                output.WriteError(
                    "Flow triage requires --manifest and --report.",
                    json,
                    "InvalidArgument");
                onError();
                return;
            }

            try
            {
                var result = await createCoordinator().AnalyzeAsync(new FlowTriageRequest
                {
                    ManifestPath = manifestPath,
                    ReportPath = reportPath,
                    Format = format,
                    OutputPath = ctx.GetValue(outputOption),
                }, ct);
                Console.Write(Encoding.UTF8.GetString(result.Content));
                if (result.Content.Length == 0 || result.Content[^1] != (byte)'\n')
                    Console.WriteLine();
            }
            catch (FlowExecutionException ex)
            {
                output.WriteError(ex.Message, json, ex.ExitCategory);
                onError();
            }
            catch
            {
                output.WriteError(
                    "Flow triage failed before a safe result could be written.",
                    json,
                    "InvocationError");
                onError();
            }
        });
        return command;
    }
}
