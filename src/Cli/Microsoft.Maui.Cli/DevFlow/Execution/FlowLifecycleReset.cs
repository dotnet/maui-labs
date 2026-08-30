using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

/// <summary>
/// What a lifecycle owner is asked to re-establish. Every field describes state the caller wants
/// applied; none of it is evidence. The owner answers with what it actually applied.
/// </summary>
internal sealed record FlowLifecycleResetRequest
{
    /// <summary>Opaque reason recorded on the reset, for example a repair-validation proposal id.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The seed the caller expects to still be applied. When present, the owner refuses to attest a
    /// reset whose applied seed identity differs, so a caller can never talk an owner into
    /// re-labelling state it did not create.
    /// </summary>
    public string? ExpectedSeedIdentity { get; init; }

    /// <summary>Whether the reviewed plan requires a backend/test-data seed to be re-applied.</summary>
    public bool RequiresBackendSeed { get; init; }

    /// <summary>
    /// The reset strategy the reviewed plan declares. An owner that cannot perform exactly this
    /// strategy must refuse rather than substitute its own, so an unapproved reset never runs.
    /// </summary>
    public string? RequiredStrategy { get; init; }

    /// <summary>Whether the reviewed plan pins a collection-item identity.</summary>
    public bool RequiresCollectionItemKey { get; init; }
}

/// <summary>
/// The state a lifecycle owner has itself applied to the app under test. These are first-hand facts:
/// the owner installed the package, wiped its data, and chose the seed, so it can name them without
/// asking the running app. A field is null when the owner did not apply it — never guessed.
/// </summary>
internal sealed record FlowLifecycleAppliedState
{
    /// <summary>How the owner reset the app, for example <c>android-clear-app-data</c>.</summary>
    public required string Strategy { get; init; }

    /// <summary>Stable identity of the reset target: package, device, and owner in one string.</summary>
    public required string ResetIdentity { get; init; }

    /// <summary>Identity of the seed data set the owner applied, or null when it applied none.</summary>
    public string? SeedIdentity { get; init; }

    /// <summary>Digest of the app-state seed the owner applied.</summary>
    public required string SeedFingerprint { get; init; }

    /// <summary>
    /// Digest of the backend/test-data state the owner applied. When the owner applied no backend
    /// seed this is the well-known <see cref="FlowLifecycleResetFingerprints.NoBackendApplied"/>
    /// value, which states exactly that and is never presented as a measured backend state.
    /// </summary>
    public required string BackendStateFingerprint { get; init; }

    /// <summary>The seeded collection identity, or the well-known "none" sentinel.</summary>
    public required string CollectionItemKey { get; init; }

    /// <summary>Whether the app-state reset itself succeeded.</summary>
    public bool AppStateSucceeded { get; init; }

    /// <summary>Whether a backend/test-data reset succeeded, when one was required.</summary>
    public bool BackendTestDataSucceeded { get; init; }
}

/// <summary>
/// What a lifecycle owner would establish if it were asked to reset, described without resetting.
/// </summary>
/// <remarks>
/// This is an offer, not evidence. It exists so an author can declare the seed fingerprint that
/// admission will later compare against, instead of guessing a value that fails closed or spending
/// a one-shot run grant to discover it. Nothing here asserts that a reset happened.
/// </remarks>
internal sealed record FlowLifecycleResetOffer
{
    public required string OwnerId { get; init; }
    public required string Strategy { get; init; }
    public required string ResetIdentity { get; init; }
    public string? SeedIdentity { get; init; }
    public required string SeedFingerprint { get; init; }
    public required string BackendStateFingerprint { get; init; }
}

/// <summary>Result of one owner-performed reset.</summary>
internal sealed record FlowLifecycleResetOutcome
{
    public bool Succeeded { get; init; }
    public FlowLifecycleAppliedState? Applied { get; init; }
    public string? FailureCode { get; init; }
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
}

/// <summary>
/// A component that owns the app under test end to end: it installed the package, it can wipe and
/// re-seed it, and it can relaunch it. Ownership is the whole point — a component that merely talks
/// to a running app cannot answer <see cref="GetAppliedStateAsync"/> without echoing the app.
/// </summary>
/// <remarks>
/// <para>
/// A conforming owner must reset app state <em>without restarting the app process</em>. The broker
/// binds a repair validation to one agent instance: a re-registering agent is assigned a fresh
/// instance id and the previous registration is marked unavailable, and every post-reset step
/// (checkpoint observation, route restore, transient replay, and the checkpoint's own
/// agent-instance comparison) is fenced on the instance that was live when validation started.
/// An owner that force-stops and relaunches the app therefore invalidates the validation it was
/// asked to enable — after having destroyed user state. This is why no owner ships today: the
/// obvious Android implementation (<c>am force-stop</c> + <c>pm clear</c> + <c>am start</c>)
/// necessarily restarts the process, so it can never satisfy this contract.
/// </para>
/// <para>
/// A viable owner needs either an in-app reset surface that survives the process, or broker support
/// for re-binding a validation across a deliberate, owner-announced restart. Neither exists yet.
/// </para>
/// </remarks>
internal interface IFlowLifecycleResetOwner
{
    /// <summary>Stable identity of the owner, recorded in the reset identity.</summary>
    string OwnerId { get; }

    /// <summary>
    /// The state this owner currently has applied to the app, or null when it has not applied any
    /// (for example because it did not launch the connected app).
    /// </summary>
    Task<FlowLifecycleAppliedState?> GetAppliedStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-applies the owner's known state to the app and reports what it applied.</summary>
    Task<FlowLifecycleResetOutcome> ResetAsync(
        FlowLifecycleResetRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic fingerprints over facts a lifecycle owner applied. They deliberately contain no
/// clock, no random value, and nothing read back from the app, so the same owner re-applying the
/// same state to the same package on the same device produces the same fingerprint — which is what
/// makes a recorded pre-step checkpoint comparable to a freshly attested one.
/// </summary>
internal static class FlowLifecycleResetFingerprints
{
    /// <summary>
    /// States that the owner applied no backend or test-data seed. It is a claim about the owner's
    /// own action, not a measurement of a backend, and is only valid where the reviewed plan
    /// declares that no backend seed is required.
    /// </summary>
    internal const string NoBackendApplied = "no-backend-applied:v1";

    /// <summary>The well-known sentinel for "no collection item is pinned", as recorded elsewhere.</summary>
    internal const string NoCollectionItem = "none";

    private const string SeedPrefix = "app-state-seed:v1:";

    /// <summary>Builds the stable identity of what the owner resets.</summary>
    internal static string ResetIdentity(string ownerId, string strategy, string appIdentity, string deviceIdentity)
        => Digest("reset-identity:v1", ownerId, strategy, appIdentity, deviceIdentity);

    /// <summary>
    /// Digests the app-state seed the owner applied. <paramref name="seedIdentity"/> is null when the
    /// owner wiped the app and applied no seed; the digest then records that the state is the app's
    /// own post-wipe state for this exact package build, which is a fact the owner established.
    /// </summary>
    internal static string SeedFingerprint(
        string resetIdentity,
        string appBuildIdentity,
        string? seedIdentity)
        => SeedPrefix + Digest(
            "app-state-seed",
            resetIdentity,
            appBuildIdentity,
            string.IsNullOrWhiteSpace(seedIdentity) ? "wiped-no-seed" : seedIdentity.Trim());

    /// <summary>Digests a backend/test-data seed the owner actually applied.</summary>
    internal static string BackendFingerprint(string resetIdentity, string backendSeedIdentity)
        => "backend-test-data-seed:v1:" + Digest(
            "backend-test-data-seed",
            resetIdentity,
            backendSeedIdentity.Trim());

    private static string Digest(params string?[] material)
    {
        var canonical = string.Join(
            "\u001f",
            material.Select(static value => value?.Trim() ?? string.Empty));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..32];
    }
}
