using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

/// <summary>
/// The exact app the broker is attached to, for the purpose of reading independent business-oracle
/// evidence out of band.
/// </summary>
internal sealed record AttachedRunOracleTarget
{
    public required MauiTestPlan Plan { get; init; }

    /// <summary>The platform reported by the agent registration, such as <c>android</c>.</summary>
    public required string Platform { get; init; }

    /// <summary>The launch identity of the running app, used to reach its private storage.</summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// The device identity the agent reported about itself, such as
    /// <c>platform=android;avd=my-emulator</c>, or null when it recognised nothing.
    /// </summary>
    /// <remarks>
    /// The evaluator resolves this to a device its own transport can address. Taking the agent's
    /// self-report rather than a pre-resolved serial keeps this path independent of the device-host
    /// subsystem, which is a separate optional component: needing it merely to learn an adb serial
    /// would make oracle evidence unavailable on any machine where it is not running, even though
    /// adb — which the evaluator needs anyway — can answer the question directly.
    /// </remarks>
    public string? DeviceIdentity { get; init; }

    /// <summary>The instant after which no further evidence read may begin.</summary>
    public required DateTimeOffset Deadline { get; init; }
}

/// <summary>
/// What the declared evidence already said before the run started.
/// </summary>
/// <remarks>
/// A run that owns installation gets freshness for free: app-private storage is empty at launch, so
/// anything read afterwards was written by that run. A run the broker merely attached to has no
/// such guarantee — the app may have been running for hours with arbitrary prior state — so
/// "the record is present afterwards" proves nothing on its own. Recording which predicates were
/// already satisfied beforehand converts the claim into "this record did not exist before this run
/// and does now", which is the property the oracle is actually asserting.
/// </remarks>
internal sealed record AttachedRunOracleBaseline
{
    /// <summary>Whether the baseline was observed at all. Nothing may be certified without it.</summary>
    public bool Observed { get; init; }

    public string? UnavailableCode { get; init; }
    public string? UnavailableMessage { get; init; }

    /// <summary>
    /// Per oracle, the indexes of <c>expect.contains</c> predicates that the evidence already
    /// satisfied before the run. Any of these means the record predates the run.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> PreExistingContains { get; init; } =
        new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);

    public static AttachedRunOracleBaseline Unavailable(string code, string message)
        => new() { Observed = false, UnavailableCode = code, UnavailableMessage = message };
}

/// <summary>
/// Reads independent business-oracle evidence for a run against an app the broker did not install
/// or launch.
/// </summary>
internal interface IAttachedRunOracleEvaluator
{
    /// <summary>Whether this evaluator can serve the plan's declared oracles on this platform.</summary>
    bool SupportsAttachedRun(MauiTestPlan? plan, string? platform);

    Task<AttachedRunOracleBaseline> ObserveAttachedBaselineAsync(
        AttachedRunOracleTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates the declared oracles against the evidence as it stands now, refusing to certify
    /// anything the baseline shows already existed.
    /// </summary>
    Task<IReadOnlyList<MauiIndependentBusinessOracleResult>> EvaluateAttachedAsync(
        AttachedRunOracleTarget target,
        AttachedRunOracleBaseline baseline,
        CancellationToken cancellationToken = default);
}
