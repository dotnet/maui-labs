using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

/// <summary>
/// A lifecycle reset owner that re-establishes app state by invoking a DevFlow Action the app
/// itself registers, rather than by reinstalling or clearing the package from outside.
/// </summary>
/// <remarks>
/// <para>
/// This is the in-app reset surface <see cref="IFlowLifecycleResetOwner"/> describes. A repair
/// validation is fenced to one agent instance, so an owner that force-stops and relaunches the app
/// invalidates the validation it was asked to enable, after having destroyed state. Invoking an
/// in-process action keeps the same process, the same registration, and the same instance id, which
/// is what makes this owner conformant where an <c>adb pm clear</c> owner cannot be.
/// </para>
/// <para>
/// Ownership is preserved because the owner decides which seed to apply and names it: the app is
/// told what to establish and only reports success. Every fingerprint below is derived from facts
/// the owner supplied, never from a value the app echoed back, so an app cannot talk the owner into
/// attesting state it did not ask for.
/// </para>
/// </remarks>
internal sealed class AppActionFlowLifecycleResetOwner : IFlowLifecycleResetOwner
{
    /// <summary>The action an app registers to opt into agent-led repair validation.</summary>
    internal const string ResetActionName = "devflow-reset";

    /// <summary>
    /// The only strategy this owner can perform. It is deliberately not <c>uninstall-reinstall</c>
    /// or <c>pm-clear</c>: naming one of those would claim a package-level guarantee that an
    /// in-process action does not provide.
    /// </summary>
    internal const string ResetStrategy = "app-action-reset";

    /// <summary>Lease holder kind, matching the one the broker uses for its own validation steps.</summary>
    private const string LeaseHolderKind = "repair-validation";
    private const string LeaseLabel = "app-state-reset";

    private readonly int _agentPort;
    private readonly string _appIdentity;
    private readonly string _deviceIdentity;
    private readonly string _appBuildIdentity;
    private readonly string? _preclaimedMutationLeaseId;
    private FlowLifecycleAppliedState? _applied;

    internal AppActionFlowLifecycleResetOwner(
        int agentPort,
        string appIdentity,
        string deviceIdentity,
        string appBuildIdentity,
        string? preclaimedMutationLeaseId = null)
    {
        _agentPort = agentPort;
        _appIdentity = appIdentity;
        _deviceIdentity = deviceIdentity;
        _appBuildIdentity = appBuildIdentity;
        _preclaimedMutationLeaseId = string.IsNullOrWhiteSpace(preclaimedMutationLeaseId)
            ? null
            : preclaimedMutationLeaseId.Trim();
    }

    public string OwnerId => "devflow-app-action-reset";

    /// <summary>
    /// What this owner would establish if asked, without asking. The fingerprints are pure
    /// functions of facts the owner already holds, so describing them performs no reset and
    /// mutates nothing.
    /// </summary>
    /// <remarks>
    /// An author has to declare the seed fingerprint admission will compare against, but cannot
    /// compute it: it digests the owner id, strategy, app, device, and build. Without this the only
    /// options are to guess a value that fails closed, or to spend a one-shot run grant discovering
    /// it. This is deliberately an offer, not an attestation — nothing here is evidence that a reset
    /// happened, and <see cref="GetAppliedStateAsync"/> still reports nothing until one does.
    /// </remarks>
    internal FlowLifecycleResetOffer DescribeOffer(string? seedIdentity = null)
    {
        var seed = string.IsNullOrWhiteSpace(seedIdentity) ? null : seedIdentity.Trim();
        var resetIdentity = FlowLifecycleResetFingerprints.ResetIdentity(
            OwnerId,
            ResetStrategy,
            _appIdentity,
            _deviceIdentity);

        return new FlowLifecycleResetOffer
        {
            OwnerId = OwnerId,
            Strategy = ResetStrategy,
            ResetIdentity = resetIdentity,
            SeedIdentity = seed,
            SeedFingerprint = FlowLifecycleResetFingerprints.SeedFingerprint(
                resetIdentity,
                _appBuildIdentity,
                seed),
            BackendStateFingerprint = FlowLifecycleResetFingerprints.NoBackendApplied,
        };
    }

    /// <summary>Confirms the app currently advertises the reset action this owner depends on.</summary>
    internal Task<bool> CanResetAsync(CancellationToken cancellationToken = default)
        => SupportsResetActionAsync(cancellationToken);

    /// <summary>
    /// What this owner has applied so far. It stays null until the owner has actually performed a
    /// reset: before that it has established nothing, and reporting the app's current state would
    /// be echoing the app rather than attesting the owner's own action.
    /// </summary>
    public Task<FlowLifecycleAppliedState?> GetAppliedStateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_applied);

    public async Task<FlowLifecycleResetOutcome> ResetAsync(
        FlowLifecycleResetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Refuse everything this owner cannot itself establish, before touching the app. A reset
        // that runs and then cannot be attested has already destroyed the state it was meant to
        // restore, so each check below fails closed rather than proceeding hopefully.
        if (!string.IsNullOrWhiteSpace(request.RequiredStrategy) &&
            !string.Equals(request.RequiredStrategy.Trim(), ResetStrategy, StringComparison.Ordinal))
        {
            return Failed("repair-reset-strategy-unsupported");
        }

        if (request.RequiresBackendSeed)
            return Failed("repair-backend-seed-unsupported");

        if (request.RequiresCollectionItemKey)
            return Failed("repair-collection-item-unsupported");

        if (!await SupportsResetActionAsync(cancellationToken).ConfigureAwait(false))
            return Failed("repair-reset-action-unavailable");

        // The owner chooses the seed. The app is told which one to establish and never reports one
        // back, so a compromised or buggy app cannot relabel the state the owner attests.
        var seedIdentity = string.IsNullOrWhiteSpace(request.ExpectedSeedIdentity)
            ? null
            : request.ExpectedSeedIdentity.Trim();

        InvokeResult? result;
        try
        {
            result = await InvokeResetActionAsync(seedIdentity, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Any failure to invoke is reported as a refusal rather than rethrown. The attester
            // reads failure codes, and an exception escaping into the broker would abandon a
            // validation midway instead of failing it cleanly.
            return Failed("repair-app-state-reset-failed");
        }

        if (result is null || !result.Success)
            return Failed("repair-app-state-reset-failed");

        var resetIdentity = FlowLifecycleResetFingerprints.ResetIdentity(
            OwnerId,
            ResetStrategy,
            _appIdentity,
            _deviceIdentity);

        var applied = new FlowLifecycleAppliedState
        {
            Strategy = ResetStrategy,
            ResetIdentity = resetIdentity,
            SeedIdentity = seedIdentity,
            SeedFingerprint = FlowLifecycleResetFingerprints.SeedFingerprint(
                resetIdentity,
                _appBuildIdentity,
                seedIdentity),
            // This owner seeds no backend, and says so explicitly rather than leaving the field to
            // be read as an unmeasured backend state.
            BackendStateFingerprint = FlowLifecycleResetFingerprints.NoBackendApplied,
            CollectionItemKey = FlowLifecycleResetFingerprints.NoCollectionItem,
            AppStateSucceeded = true,
            BackendTestDataSucceeded = false,
        };

        _applied = applied;
        return new FlowLifecycleResetOutcome { Succeeded = true, Applied = applied };
    }

    /// <summary>
    /// Invokes the app's reset action under an explicit repair-validation lease.
    /// </summary>
    /// <remarks>
    /// While a broker is attached the agent refuses mutations that do not carry the broker's own
    /// authority. The owner therefore either claims a short-lived reset lease itself, or uses the
    /// lease the broker preclaimed by atomically adopting an idle trusted Inspector host. The
    /// component that claimed the lease also releases or transfers it. The implicit lease is
    /// disabled because an auto-acquired lease is never released and would collide with the replay
    /// this reset exists to enable.
    /// </remarks>
    private async Task<InvokeResult?> InvokeResetActionAsync(
        string? seedIdentity,
        CancellationToken cancellationToken)
    {
        var controlsLease = _preclaimedMutationLeaseId is null;
        var leaseId = _preclaimedMutationLeaseId ?? $"repair-validation-{Guid.NewGuid():N}";
        using var client = new AgentClient("localhost", _agentPort)
        {
            AutoAcquireMutationLease = false,
        };
        using var leaseScope = client.UseMutationLease(leaseId, LeaseHolderKind, LeaseLabel);

        if (controlsLease)
        {
            var claim = await client
                .ControlMutationLeaseAsync("claim", false, leaseId, LeaseHolderKind, LeaseLabel)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!claim.YouHold)
                return null;
        }
        else
        {
            var status = await client
                .ControlMutationLeaseAsync("status", false, leaseId, LeaseHolderKind, LeaseLabel)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!status.YouHold)
                return null;
        }

        try
        {
            var args = new JsonArray { seedIdentity };
            return await client
                .InvokeActionAsync(ResetActionName, args)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (controlsLease)
            {
                await client
                    .ControlMutationLeaseAsync("release", false, leaseId, LeaseHolderKind, LeaseLabel)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// True when the connected app registers the well-known reset action. Probed per reset rather
    /// than cached, because an app can be rebuilt with the action added or removed while the broker
    /// keeps running.
    /// </summary>
    private async Task<bool> SupportsResetActionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new AgentClient("localhost", _agentPort);
            var actions = await client.ListActionsAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!actions.TryGetProperty("actions", out var list) || list.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var action in list.EnumerateArray())
            {
                if (action.TryGetProperty("name", out var name) &&
                    string.Equals(name.GetString(), ResetActionName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // The probe asks one question: can this app be asked what it supports? Any failure —
            // unreachable agent, malformed payload, a client that rejects the call outright — is
            // answered "no", so an app is never assumed to support a reset it may not have.
        }

        return false;
    }

    private static FlowLifecycleResetOutcome Failed(string failureCode)
        => new() { Succeeded = false, FailureCode = failureCode };
}
