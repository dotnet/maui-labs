using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Utils;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal static class FlowExecutionExitCategories
{
    public const string Pass = "pass";
    public const string TestFailure = "test-failure";
    public const string InfrastructureFailure = "infrastructure-failure";
    public const string Unsupported = "unsupported";
    public const string Unverified = "unverified";
    public const string UnknownCompletion = "unknown-completion";
    public const string InvalidConfiguration = "invalid-configuration";

    public static bool IsSuccess(string? value)
        => string.Equals(value, Pass, StringComparison.Ordinal);
}

internal static class FlowExecutionCleanupPolicies
{
    public const string None = "none";
    public const string Stop = "stop";
    public const string Uninstall = "uninstall";

    public static bool IsKnown(string? value)
        => value is None or Stop or Uninstall;
}

internal sealed record FlowExecutionRequest
{
    public required string FlowPath { get; init; }
    public string? PlanPath { get; init; }
    public required string ProjectPath { get; init; }
    public string Platform { get; init; } = "android";
    public string? TargetFramework { get; init; }
    public string Configuration { get; init; } = "Debug";
    public string AgentHost { get; init; } = "localhost";
    public string? DeviceSerial { get; init; }
    public string? OutputDirectory { get; init; }
    public string CleanupPolicy { get; init; } = FlowExecutionCleanupPolicies.Stop;
    public bool CaptureFailureEvidence { get; init; }
    public bool CaptureFailureEvidenceScreenshot { get; init; }
    public TimeSpan AgentWaitTimeout { get; init; } = TimeSpan.FromSeconds(90);
}

internal sealed record FlowExecutionResult
{
    public string ExitCategory { get; init; } = FlowExecutionExitCategories.InfrastructureFailure;

    /// <summary>
    /// What the run itself concluded about the app under test, before any owned cleanup failure
    /// was folded into <see cref="ExitCategory"/>. The two differ only when teardown failed.
    /// </summary>
    public string? PrimaryExitCategory { get; init; }

    public string? Message { get; init; }
    public string? OutputDirectory { get; init; }
    public string? ManifestPath { get; init; }
    public string? ReportPath { get; init; }
    public string? JUnitPath { get; init; }
    public string? EvidencePath { get; init; }
    public MauiFlowRunReport? Report { get; init; }
    public MauiTestExecutionManifest? Manifest { get; init; }

    public bool Ok => FlowExecutionExitCategories.IsSuccess(ExitCategory);
}

internal interface IFlowExecutionCoordinator
{
    Task<FlowExecutionResult> RunAsync(
        FlowExecutionRequest request,
        CancellationToken cancellationToken = default);
}

internal class FlowExecutionException : Exception
{
    public FlowExecutionException(string exitCategory, string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ExitCategory = exitCategory;
        Code = code;
    }

    public string ExitCategory { get; }
    public string Code { get; }

    /// <summary>
    /// Optional operator-facing diagnostics file written into the run output directory before the
    /// failure was raised, so the actionable detail survives the bounded report message.
    /// </summary>
    public FlowExecutionDiagnosticsArtifact? DiagnosticsArtifact { get; init; }

    public static FlowExecutionException Invalid(string code, string message)
        => new(FlowExecutionExitCategories.InvalidConfiguration, code, message);

    public static FlowExecutionException Unsupported(string code, string message)
        => new(FlowExecutionExitCategories.Unsupported, code, message);

    public static FlowExecutionException Infrastructure(string code, string message, Exception? innerException = null)
        => new(FlowExecutionExitCategories.InfrastructureFailure, code, message, innerException);

    public static FlowExecutionException UnknownCompletion(string code, string message, Exception? innerException = null)
        => new(FlowExecutionExitCategories.UnknownCompletion, code, message, innerException);
}

internal sealed class FlowExecutionPlatformLaunchException : FlowExecutionException
{
    public FlowExecutionPlatformLaunchException(
        FlowExecutionException failure,
        FlowExecutionPlatformSession session)
        : base(failure.ExitCategory, failure.Code, failure.Message, failure)
    {
        Session = session;
        DiagnosticsArtifact = failure.DiagnosticsArtifact;
    }

    public FlowExecutionPlatformSession Session { get; }
}

/// <summary>
/// A bounded, redacted diagnostics file that a failing stage persisted into the run output
/// directory. Only the relative file name is reported so no absolute host path leaks.
/// </summary>
internal sealed record FlowExecutionDiagnosticsArtifact
{
    public required string FileName { get; init; }
    public required string Digest { get; init; }
    public required long SizeBytes { get; init; }
    public string Kind { get; init; } = "build-log";
    public string MediaType { get; init; } = "text/plain";
}

internal sealed record CommittedFlowBundle
{
    public required string FlowPath { get; init; }
    public required string PlanPath { get; init; }
    public required MauiFlow Flow { get; init; }
    public required MauiTestPlan Plan { get; init; }
    public required string FlowDigest { get; init; }
    public string? FlowId { get; init; }
    public int? FlowRevision { get; init; }
}

internal sealed record AppArtifactResolutionRequest
{
    public required string ProjectPath { get; init; }
    public required string AgentSessionId { get; init; }
    public string? TargetFramework { get; init; }
    public required string Configuration { get; init; }
    public required string WorkDirectory { get; init; }
    public string Platform { get; init; } = "app";
    public IReadOnlyList<string> TargetFrameworkPlatformIdentifiers { get; init; } = [];
    public IReadOnlyList<string> CandidateArtifactTypes { get; init; } = [];
    public IReadOnlyList<string> UnsupportedArtifactTypes { get; init; } = [];
    public string? UnsupportedArtifactCode { get; init; }
    public string? UnsupportedArtifactMessage { get; init; }
    public string? RuntimeIdentifier { get; init; }
}

internal interface IAppArtifactResolver
{
    Task<ResolvedAppArtifact> ResolveAsync(
        AppArtifactResolutionRequest request,
        CancellationToken cancellationToken = default);
}

internal static class AppArtifactSigningStates
{
    public const string Signed = "signed";
    public const string Unsigned = "unsigned";
    public const string NotApplicable = "not-applicable";
    public const string Unknown = "unknown";
}

internal sealed record ResolvedAppArtifact
{
    public required string Path { get; init; }
    public required string ProjectPath { get; init; }
    public required string AgentSessionId { get; init; }
    public string? ReferenceName { get; init; }
    public required string TargetFramework { get; init; }
    public string? TargetPlatformIdentifier { get; init; }
    public string? RuntimeIdentifier { get; init; }
    public required string Configuration { get; init; }
    public string? ApplicationId { get; init; }
    public required string ArtifactType { get; init; }
    public string? ArtifactContractVersion { get; init; }
    public string? ArtifactRole { get; init; }
    public string? TargetRuntimeKind { get; init; }
    public string? DeploymentModel { get; init; }
    public string? LaunchIdentityKind { get; init; }
    public string? LaunchIdentity { get; init; }
    public string? SigningState { get; init; }
    public bool Installable { get; init; }
    public bool Launchable { get; init; }
    public required string PackageDigest { get; init; }

    /// <summary>
    /// A signing-insensitive digest of the package payload, or <see langword="null"/> when the
    /// artifact shape does not support one. See <see cref="NormalizedPayloadDigest"/>.
    /// </summary>
    public string? NormalizedPayloadDigest { get; init; }

    public string? OwnedOutputRoot { get; init; }
}

internal sealed record FlowExecutionPlatformRequest
{
    public required ResolvedAppArtifact Artifact { get; init; }
    public required FlowExecutionPlatformPreflight Preflight { get; init; }
    public int BrokerPort { get; init; }
}

internal sealed record FlowExecutionPlatformPreflightRequest
{
    public required ResolvedAppArtifact Artifact { get; init; }
    public string? DeviceSerial { get; init; }
}

/// <summary>
/// The device preconditions decidable from the committed flow, evaluated before the app build.
/// </summary>
internal sealed record FlowExecutionDeviceAdmissionRequest
{
    /// <summary>The app identity the flow declares, or null when the flow does not name one.</summary>
    public string? DeclaredAppId { get; init; }
    public string? DeviceSerial { get; init; }
}

internal sealed record FlowExecutionPlatformPreflight
{
    public required Device Device { get; init; }
    public required string DeviceSerial { get; init; }
    public required string PackageId { get; init; }
    public object? State { get; init; }
}

internal sealed record FlowExecutionPlatformSession
{
    public required Device Device { get; init; }
    public required string DeviceSerial { get; init; }
    public required string PackageId { get; init; }
    public required string Platform { get; init; }
    public required string RuntimeKind { get; init; }
    public required string DeviceProfile { get; init; }
    public bool Experimental { get; init; }
    public bool RequireAgentDeviceIdentity { get; init; }
    public int? ProcessId { get; init; }
    public bool InstalledByInvocation { get; init; }
    public bool LaunchedByInvocation { get; init; }
    public object? State { get; init; }
}

internal sealed record FlowExecutionCleanupResult
{
    public bool Succeeded { get; init; }
    public bool PackageStopped { get; init; }
    public bool PackageUninstalled { get; init; }
    public bool UninstallSkippedNotOwned { get; init; }
    public string? DetailCode { get; init; }
}

internal sealed record FlowExecutionPlatformDescriptor
{
    public required string Platform { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<string> CommandAliases { get; init; }
    public required IReadOnlyList<string> FlowPlatformAliases { get; init; }
    public required IReadOnlyList<string> AgentPlatformAliases { get; init; }
    public required IReadOnlyList<string> TargetFrameworkPlatformIdentifiers { get; init; }
    public required IReadOnlyList<string> CandidateArtifactTypes { get; init; }
    public IReadOnlyList<string> UnsupportedArtifactTypes { get; init; } = [];
    public string? UnsupportedArtifactCode { get; init; }
    public string? UnsupportedArtifactMessage { get; init; }
    public bool Experimental { get; init; }

    public bool MatchesCommand(string value)
        => CommandAliases.Any(alias => string.Equals(alias, value, StringComparison.OrdinalIgnoreCase));

    public bool MatchesFlowPlatform(string value)
        => FlowPlatformTags.Matches(value, FlowPlatformAliases);
}

internal interface IFlowExecutionPlatformAdapter
{
    FlowExecutionPlatformDescriptor Descriptor { get; }

    void ValidateHost();

    string? GetDefaultRuntimeIdentifier();

    /// <summary>
    /// Checks the device preconditions that can be decided from the committed flow alone, before
    /// anything expensive runs.
    /// </summary>
    /// <remarks>
    /// The pre-existing-app refusal used to fire during <c>platform-launch</c>, which is after a
    /// multi-minute app build - so an operator paid for the whole build to be told the run was
    /// never admissible. The flow itself names the app under test, so the same question can be
    /// asked first. This is an additional, earlier gate: the authoritative check still runs at
    /// deployment against the built artifact's real launch identity, because the flow's declared
    /// package is an authoring claim rather than proof of what the build produced.
    /// </remarks>
    Task ValidateDeviceAdmissionAsync(
        FlowExecutionDeviceAdmissionRequest request,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task<FlowExecutionPlatformPreflight> PreflightAsync(
        FlowExecutionPlatformPreflightRequest request,
        CancellationToken cancellationToken = default);

    Task<FlowExecutionPlatformSession> PrepareAndLaunchAsync(
        FlowExecutionPlatformRequest request,
        CancellationToken cancellationToken = default);

    Task EstablishAgentForwardingAsync(
        FlowExecutionPlatformSession session,
        int agentPort,
        int brokerPort,
        CancellationToken cancellationToken = default);

    Task<FlowExecutionCleanupResult> CleanupAsync(
        FlowExecutionPlatformSession session,
        string cleanupPolicy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the platform whether the app under test is still alive and, if not, whether it died
    /// abnormally.
    /// </summary>
    /// <remarks>
    /// This must run before cleanup, because cleanup deliberately stops or uninstalls the app and
    /// would replace the evidence with an operator-requested exit. Adapters that cannot answer
    /// return <see langword="null"/>, which the report records as "not probed" rather than as a
    /// clean exit.
    /// </remarks>
    Task<MauiFlowAppProcessEvidence?> ProbeAppProcessAsync(
        FlowExecutionAppProbeRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult<MauiFlowAppProcessEvidence?>(null);
}

/// <summary>What the app-process probe is allowed to look at.</summary>
internal sealed record FlowExecutionAppProbeRequest
{
    public required FlowExecutionPlatformSession Session { get; init; }

    /// <summary>
    /// When the run started. Platform crash records are historical, so anything older than this
    /// belongs to a previous run and must not be attributed to this one.
    /// </summary>
    public required DateTimeOffset RunStartedAt { get; init; }
}

internal sealed record ExactAgentBindingExpectation
{
    public required string SessionId { get; init; }
    public required string TargetFramework { get; init; }
    public required string Platform { get; init; }
    public required IReadOnlyList<string> PlatformAliases { get; init; }
    public required string PackageId { get; init; }
    public string? DeviceSerial { get; init; }
    public string? DeviceEmulatorId { get; init; }
    public bool RequireDeviceIdentityMatch { get; init; }
    public int? ProcessId { get; init; }
    public string? AppBuild { get; init; }
}

internal interface IExecutionProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IEnumerable<string>? environmentVariablesToRemove = null,
        CancellationToken cancellationToken = default);
}

internal sealed class ExecutionProcessRunner : IExecutionProcessRunner
{
    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IEnumerable<string>? environmentVariablesToRemove = null,
        CancellationToken cancellationToken = default)
        => ProcessRunner.RunAsync(
            fileName,
            arguments.ToArray(),
            workingDirectory: workingDirectory,
            timeout: timeout,
            environmentVariablesToRemove: environmentVariablesToRemove,
            cancellationToken: cancellationToken);
}
