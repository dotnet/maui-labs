using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal sealed record FlowStateEvidenceRequest
{
    public required MauiTestPlan Plan { get; init; }
    public required MauiFlow Flow { get; init; }
    public required ResolvedAppArtifact Artifact { get; init; }
}

internal sealed record FlowStateEvidenceResult
{
    public bool Supported { get; init; } = true;
    public string? DetailCode { get; init; }
    public string? Message { get; init; }
    public MauiFlowRunContext? RunContext { get; init; }
}

internal sealed record FlowStateAdmission
{
    public string? ProviderId { get; init; }
    public required MauiFlowRunContext RunContext { get; init; }
}

internal sealed record FlowPostRunOracleEvaluationRequest
{
    public required MauiTestPlan Plan { get; init; }
    public required MauiFlow Flow { get; init; }
    public required ResolvedAppArtifact Artifact { get; init; }
    public required string RunId { get; init; }
    public required string FlowDigest { get; init; }
    public required string DeviceIdentityFingerprint { get; init; }
    public required string AppBuildFingerprint { get; init; }
    public required string PackageDigest { get; init; }

    /// <summary>The platform the run was executed on, as reported by the platform adapter.</summary>
    public required string Platform { get; init; }

    /// <summary>
    /// The exact device the run executed against. A provider needs it to reach the same device
    /// out-of-band; it is never copied into the report, which carries only the hashed identity.
    /// </summary>
    public required string DeviceSerial { get; init; }

    /// <summary>The launch identity the platform adapter deployed and launched.</summary>
    public required string PackageId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset EndedAt { get; init; }
    public required DateTimeOffset EvaluationDeadline { get; init; }
    public required MauiFlowRunReport Report { get; init; }
}

internal sealed record FlowPostRunOracleEvidenceResult
{
    public bool Supported { get; init; } = true;
    public string? DetailCode { get; init; }
    public string? Message { get; init; }
    public string? RunId { get; init; }
    public string? FlowDigest { get; init; }
    public string? DeviceIdentityFingerprint { get; init; }
    public string? AppBuildFingerprint { get; init; }
    public string? PackageDigest { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public DateTimeOffset? ObservedAt { get; init; }
    public IReadOnlyList<MauiIndependentBusinessOracleResult> BusinessOracles { get; init; } = [];
}

internal interface IFlowStateEvidenceProvider
{
    string ProviderId { get; }
    bool Supports(FlowStateEvidenceRequest request);

    Task<FlowStateEvidenceResult> PrepareAsync(
        FlowStateEvidenceRequest request,
        CancellationToken cancellationToken = default);

    Task<FlowPostRunOracleEvidenceResult> EvaluatePostRunAsync(
        FlowPostRunOracleEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

internal interface IFlowStateEvidenceProviderRegistry
{
    Task<FlowStateAdmission> PrepareAsync(
        FlowStateEvidenceRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MauiIndependentBusinessOracleResult>> EvaluatePostRunAsync(
        FlowStateAdmission admission,
        FlowPostRunOracleEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class FlowStateEvidenceProviderRegistry : IFlowStateEvidenceProviderRegistry
{
    private readonly IReadOnlyList<IFlowStateEvidenceProvider> _providers;

    public FlowStateEvidenceProviderRegistry(IEnumerable<IFlowStateEvidenceProvider> providers)
        => _providers = (providers ?? throw new ArgumentNullException(nameof(providers))).ToArray();

    public async Task<FlowStateAdmission> PrepareAsync(
        FlowStateEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var policy = request.Plan.ParsedSideEffectPolicy;
        if (policy == MauiFlowSideEffectPolicy.NonReplayable)
        {
            throw FlowExecutionException.Unsupported(
                "non-replayable-flow",
                "Non-replayable plans are not supported by unattended flow run.");
        }

        var matches = _providers.Where(provider => provider.Supports(request)).ToArray();
        var providerRequired =
            policy is MauiFlowSideEffectPolicy.TestTenantResettable or MauiFlowSideEffectPolicy.Compensated ||
            HasCheckpointRequirements(request.Plan.Checkpoint);
        if (matches.Length > 1)
        {
            throw FlowExecutionException.Invalid(
                "state-evidence-provider-ambiguous",
                "Multiple allow-listed state evidence providers matched the plan.");
        }
        if (matches.Length == 0)
        {
            if (providerRequired)
            {
                throw FlowExecutionException.Unsupported(
                    "state-evidence-provider-missing",
                    "The plan requires reset or state evidence, but no matching allow-listed provider is registered.");
            }

            return new FlowStateAdmission
            {
                RunContext = CreateSideEffectFreeContext(),
            };
        }

        var result = await matches[0].PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Supported || result.RunContext is null)
        {
            throw FlowExecutionException.Unsupported(
                result.DetailCode ?? "state-evidence-provider-unsupported",
                result.Message ?? "The matching state evidence provider could not establish the plan's reset and evidence contract.");
        }
        if (result.RunContext.BusinessOracles.Count > 0)
        {
            throw FlowExecutionException.Invalid(
                "pre-run-oracle-evidence-not-allowed",
                "A state evidence provider returned business-oracle evidence before the run completed.");
        }

        result.RunContext.Intent = MauiFlowReplayIntents.OrdinaryReplay;
        var decision = MauiFlowReplaySafetyEvaluator.Evaluate(new MauiFlowRunRequest
        {
            Plan = request.Plan,
            Context = result.RunContext,
        });
        if (!decision.OrdinaryReplayAllowed)
        {
            throw FlowExecutionException.Invalid(
                "state-evidence-admission-denied",
                "The state evidence provider did not satisfy the plan's replay admission contract.");
        }
        return new FlowStateAdmission
        {
            ProviderId = matches[0].ProviderId,
            RunContext = result.RunContext,
        };
    }

    public async Task<IReadOnlyList<MauiIndependentBusinessOracleResult>> EvaluatePostRunAsync(
        FlowStateAdmission admission,
        FlowPostRunOracleEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(admission.ProviderId))
            return [];

        var provider = _providers.SingleOrDefault(candidate =>
            string.Equals(candidate.ProviderId, admission.ProviderId, StringComparison.Ordinal));
        if (provider is null)
        {
            throw FlowExecutionException.Infrastructure(
                "post-run-oracle-provider-missing",
                "The state evidence provider selected during admission is unavailable after execution.");
        }

        var result = await provider.EvaluatePostRunAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Supported)
        {
            throw FlowExecutionException.Infrastructure(
                result.DetailCode ?? "post-run-oracle-evaluation-failed",
                result.Message ?? "The state evidence provider could not evaluate post-run business oracles.");
        }
        ValidatePostRunBinding(result, request);
        return result.BusinessOracles;
    }

    private static MauiFlowRunContext CreateSideEffectFreeContext()
        => new()
        {
            Intent = MauiFlowReplayIntents.OrdinaryReplay,
            Preconditions = new MauiFlowReplayPreconditions
            {
                Expected = new MauiFlowCheckpoint(),
                ObservationDeferredUntilLaunch = true,
            },
            BusinessOracles = [],
            PriorMutationCompletionCertain = true,
        };

    private static bool HasCheckpointRequirements(MauiFlowCheckpointRequirements? checkpoint)
    {
        if (checkpoint is null)
            return false;
        return new[]
        {
            checkpoint.AppBuildFingerprint,
            checkpoint.SeedFingerprint,
            checkpoint.BackendStateFingerprint,
            checkpoint.AppStateSeed?.Fingerprint,
            checkpoint.BackendTestDataSeed?.Fingerprint,
            checkpoint.Locale,
            checkpoint.Theme,
            checkpoint.Orientation,
            checkpoint.Route,
            checkpoint.Window,
            checkpoint.Modal,
            checkpoint.CollectionItemKey,
            checkpoint.DisplayProfile,
        }.Any(static value => !string.IsNullOrWhiteSpace(value));
    }

    /// <summary>
    /// Copies the plan's declared checkpoint requirements into the run context's expected
    /// checkpoint, so the runner enforces them and the report records what the run required.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the fields a live agent actually reports are projected. The runner compares expected
    /// against observed field by field and treats a blank expected field as "no constraint", so
    /// projecting a field DevFlow cannot observe (a seed or backend-state fingerprint, or a
    /// collection item key) would turn every run into a checkpoint mismatch rather than enforcing
    /// anything. Those declarations stay the responsibility of a state evidence provider that can
    /// actually attest them.
    /// </para>
    /// <para>
    /// The expected checkpoint is a declaration, never an observation: it comes from the committed
    /// plan and is fixed before the run starts. Deriving it from the same step's live reading would
    /// make <c>checkpointMatches</c> self-fulfilling and destroy the locator-drift-versus-state-drift
    /// distinction it exists to draw.
    /// </para>
    /// <para>
    /// Because the runner compares this checkpoint at <em>every</em> step, a declared or supplied
    /// <c>route</c>, <c>window</c> or <c>modal</c> is an invariant of the whole run and not merely
    /// its entry state. A flow that deliberately navigates away from the declared route will report
    /// <c>routeMatches: false</c> from that point on, and a failure there classifies as
    /// <c>route-state-drift</c> rather than <c>locator-not-found</c>. Declare these fields only
    /// where they hold for the entire flow.
    /// </para>
    /// </remarks>
    internal static void ApplyDeclaredCheckpoint(
        MauiFlowRunContext context,
        MauiFlowCheckpointRequirements? declared)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (declared is null)
            return;
        var preconditions = context.Preconditions;
        if (preconditions is null)
            return;
        var expected = preconditions.Expected ?? new MauiFlowCheckpoint();
        expected.AppBuildFingerprint = Coalesce(expected.AppBuildFingerprint, declared.AppBuildFingerprint);
        expected.Route = Coalesce(expected.Route, declared.Route);
        expected.Window = Coalesce(expected.Window, declared.Window);
        expected.Modal = Coalesce(expected.Modal, declared.Modal);
        expected.Locale = Coalesce(expected.Locale, declared.Locale);
        expected.Theme = Coalesce(expected.Theme, declared.Theme);
        expected.Orientation = Coalesce(expected.Orientation, declared.Orientation);
        expected.DisplayProfile = Coalesce(expected.DisplayProfile, declared.DisplayProfile);
        preconditions.Expected = expected;
    }

    private static string? Coalesce(string? current, string? declared)
        => string.IsNullOrWhiteSpace(current) ? Trim(declared) : current;

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidatePostRunBinding(
        FlowPostRunOracleEvidenceResult result,
        FlowPostRunOracleEvaluationRequest request)
    {
        if (!string.Equals(result.RunId, request.RunId, StringComparison.Ordinal) ||
            !string.Equals(result.FlowDigest, request.FlowDigest, StringComparison.Ordinal) ||
            !string.Equals(
                result.DeviceIdentityFingerprint,
                request.DeviceIdentityFingerprint,
                StringComparison.Ordinal) ||
            !string.Equals(result.AppBuildFingerprint, request.AppBuildFingerprint, StringComparison.Ordinal) ||
            !string.Equals(result.PackageDigest, request.PackageDigest, StringComparison.Ordinal) ||
            result.StartedAt != request.StartedAt ||
            result.EndedAt != request.EndedAt ||
            result.ObservedAt is null ||
            result.ObservedAt < request.EndedAt ||
            result.ObservedAt > request.EvaluationDeadline)
        {
            throw FlowExecutionException.Infrastructure(
                "post-run-oracle-binding-mismatch",
                "Post-run business-oracle evidence was not bound to the exact run, device, build, flow, and time window.");
        }

        if (result.BusinessOracles.Any(oracle =>
                oracle.ObservedAt is null ||
                oracle.ObservedAt < request.EndedAt ||
                oracle.ObservedAt > request.EvaluationDeadline))
        {
            throw FlowExecutionException.Infrastructure(
                "post-run-oracle-time-invalid",
                "Post-run business-oracle evidence was observed outside the bounded evaluation window.");
        }
    }
}
