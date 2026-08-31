using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Devices;
using Testing = Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(CommandDescription))]
[JsonSerializable(typeof(List<CommandDescription>))]
[JsonSerializable(typeof(AgentStatus))]
[JsonSerializable(typeof(ElementInfo))]
[JsonSerializable(typeof(List<ElementInfo>))]
[JsonSerializable(typeof(NetworkRequest))]
[JsonSerializable(typeof(List<NetworkRequest>))]
[JsonSerializable(typeof(ThemeResult))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(AgentRegistration))]
[JsonSerializable(typeof(List<AgentRegistration>))]
[JsonSerializable(typeof(AgentRegistration[]))]
[JsonSerializable(typeof(BrokerState))]
[JsonSerializable(typeof(DeviceControlResult))]
[JsonSerializable(typeof(DeviceMutationLeaseResult))]
[JsonSerializable(typeof(DeviceMutationLeaseRequest))]
[JsonSerializable(typeof(DeviceTarget))]
[JsonSerializable(typeof(DeviceTarget[]))]
[JsonSerializable(typeof(RegistrationMessage))]
[JsonSerializable(typeof(RouteCheckpoint))]
[JsonSerializable(typeof(RouteRestoreResult))]
[JsonSerializable(typeof(RouteCheckpointStatus))]
[JsonSerializable(typeof(AndroidDevFlowForwardingReport))]
[JsonSerializable(typeof(AndroidDevFlowDevice[]))]
[JsonSerializable(typeof(AndroidDevFlowPortForward[]))]
[JsonSerializable(typeof(ExtensionDescriptor))]
[JsonSerializable(typeof(ExtensionToolInfo))]
[JsonSerializable(typeof(ExtensionToolAnnotationsInfo))]
[JsonSerializable(typeof(Dictionary<string, ExtensionDescriptor>))]
[JsonSerializable(typeof(DiagnosticProblemBatch))]
[JsonSerializable(typeof(FlowValidationCliResult))]
[JsonSerializable(typeof(FlowReplayCliResult))]
[JsonSerializable(typeof(FlowRunCliResult))]
[JsonSerializable(typeof(FlowCommitCliResult))]
[JsonSerializable(typeof(AppleQaArtifactVerificationResult))]
[JsonSerializable(typeof(AppleQaVerifiedArtifact))]
[JsonSerializable(typeof(WorkflowRunStartRequest))]
[JsonSerializable(typeof(WorkflowRunAccessRequest))]
[JsonSerializable(typeof(Testing.FlowStepResult))]
[JsonSerializable(typeof(List<Testing.FlowStepResult>))]
[JsonSerializable(typeof(Testing.MauiFlowRunReport))]
[JsonSerializable(typeof(Testing.MauiTestExecutionManifest))]
[JsonSerializable(typeof(Testing.MauiPreviewQualificationReport))]
[JsonSerializable(typeof(Flows.FlowIdentityCliResult))]
[JsonSerializable(typeof(Flows.MauiQualificationCommandResult))]
internal sealed partial class DevFlowCliJsonContext : JsonSerializerContext;

internal sealed class FlowValidationCliResult
{
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("steps")] public int Steps { get; init; }
    [JsonPropertyName("errors")] public List<string> Errors { get; init; } = [];
    [JsonPropertyName("warnings")] public List<string> Warnings { get; init; } = [];
}

internal sealed class FlowReplayCliResult
{
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("passed")] public int Passed { get; init; }
    [JsonPropertyName("failed")] public int Failed { get; init; }
    [JsonPropertyName("divergencePoint")] public int? DivergencePoint { get; init; }
    [JsonPropertyName("stoppedEarly")] public bool StoppedEarly { get; init; }
    [JsonPropertyName("results")] public List<Testing.FlowStepResult> Results { get; init; } = [];
    [JsonPropertyName("evidencePath")] public string? EvidencePath { get; init; }
    [JsonPropertyName("report")] public Testing.MauiFlowRunReport? Report { get; init; }
    [JsonPropertyName("reportPath")] public string? ReportPath { get; init; }
    [JsonPropertyName("reportDigest")] public string? ReportDigest { get; init; }
}

/// <summary>Outcome of re-binding a plan sidecar to the current bytes of its Markdown flow.</summary>
internal sealed class FlowCommitCliResult
{
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("changed")] public bool Changed { get; init; }
    [JsonPropertyName("flowPath")] public string FlowPath { get; init; } = "";
    [JsonPropertyName("planPath")] public string PlanPath { get; init; } = "";
    [JsonPropertyName("digest")] public string Digest { get; init; } = "";
    [JsonPropertyName("previousDigest")] public string? PreviousDigest { get; init; }
    [JsonPropertyName("removedApprovals")] public int RemovedApprovals { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = "";
}

internal sealed class FlowRunCliResult
{    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("exitCategory")] public string ExitCategory { get; init; } = "";
    [JsonPropertyName("primaryExitCategory")] public string? PrimaryExitCategory { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("outputDirectory")] public string? OutputDirectory { get; init; }
    [JsonPropertyName("manifestPath")] public string? ManifestPath { get; init; }
    [JsonPropertyName("reportPath")] public string? ReportPath { get; init; }
    [JsonPropertyName("junitPath")] public string? JUnitPath { get; init; }
    [JsonPropertyName("evidencePath")] public string? EvidencePath { get; init; }

    public static FlowRunCliResult From(FlowExecutionResult result) => new()
    {
        Ok = result.Ok,
        ExitCategory = result.ExitCategory,
        PrimaryExitCategory = result.PrimaryExitCategory ?? result.ExitCategory,
        Message = result.Message,
        OutputDirectory = result.OutputDirectory,
        ManifestPath = result.ManifestPath,
        ReportPath = result.ReportPath,
        JUnitPath = result.JUnitPath,
        EvidencePath = result.EvidencePath,
    };
}

internal sealed class InspectorLaunchCliResult
{
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("agentId")] public string AgentId { get; init; } = "";
    [JsonPropertyName("agentPort")] public int AgentPort { get; init; }
    [JsonPropertyName("appName")] public string AppName { get; init; } = "";
    [JsonPropertyName("platform")] public string Platform { get; init; } = "";
    [JsonPropertyName("launched")] public bool Launched { get; init; }
    [JsonPropertyName("testHint")] public string? TestHint { get; init; }
    [JsonPropertyName("traceHint")] public string? TraceHint { get; init; }
}
