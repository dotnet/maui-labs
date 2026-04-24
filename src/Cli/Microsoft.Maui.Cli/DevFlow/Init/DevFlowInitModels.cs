namespace Microsoft.Maui.Cli.DevFlow.Init;

using System.Text.Json.Serialization;

internal static class DevFlowInitStatus
{
    public const string Success = "success";
    public const string AlreadyPresent = "already_present";
    public const string Skipped = "skipped";
    public const string ManualRequired = "manual_required";
    public const string Failed = "failed";
    public const string Unsupported = "unsupported";
    public const string Disabled = "disabled";
}

internal sealed class DevFlowProjectCandidate
{
    public string ProjectPath { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Flavor { get; init; } = "";
    public bool IsSupported { get; init; }
    public bool NeedsBlazor { get; init; }
    public bool IsAlreadyIntegrated { get; init; }
    public string? MauiProgramPath { get; init; }
}

internal sealed class DevFlowInitOperationResult
{
    public string Name { get; init; } = "";
    public string Status { get; init; } = DevFlowInitStatus.Skipped;
    public string Detail { get; init; } = "";
    public List<string> FilesChanged { get; init; } = [];
    public List<string> ManualSteps { get; init; } = [];
}

internal sealed class DevFlowInitProjectResult
{
    public string ProjectPath { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Flavor { get; init; } = "";
    public string OverallStatus { get; set; } = DevFlowInitStatus.Skipped;
    public List<DevFlowInitOperationResult> Operations { get; init; } = [];
    public List<string> FilesChanged { get; init; } = [];
    public List<string> ManualSteps { get; init; } = [];
    public List<string> VerificationCommands { get; init; } = [];
}

internal sealed class DevFlowAiBootstrapResult
{
    public string OverallStatus { get; set; } = DevFlowInitStatus.Disabled;
    public List<string> DetectedHosts { get; init; } = [];
    public string? SelectedHostId { get; set; }
    public string? SelectedHostDisplayName { get; set; }
    public string BootstrapMode { get; set; } = "disabled";
    public List<string> FilesChanged { get; init; } = [];
    public List<string> ManualSteps { get; init; } = [];
}

internal sealed class DevFlowInitReport
{
    public string WorkspacePath { get; set; } = "";
    public string ReportPath { get; set; } = "";
    public string JsonReportPath { get; set; } = "";
    public string GeneratedAtUtc { get; set; } = "";
    public string CliVersion { get; set; } = "";
    public string ManifestVersion { get; set; } = "";
    public string ExecutionMode { get; set; } = "";
    public string OverallStatus { get; set; } = DevFlowInitStatus.Skipped;
    public DevFlowAiBootstrapResult AiBootstrap { get; set; } = new();
    public List<DevFlowInitProjectResult> Projects { get; init; } = [];
    public List<string> Notes { get; init; } = [];
    public List<string> NextSteps { get; init; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DevFlowInitReport))]
internal sealed partial class DevFlowInitReportJsonContext : JsonSerializerContext;
