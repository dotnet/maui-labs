namespace Microsoft.Maui.DevFlow.Devices;

/// <summary>
/// A recording path that has already been vouched for, carried together with the authority that
/// vouched for it.
/// <para>
/// A recording travels a long way from the device host that wrote it: it is hashed into a flow's
/// evidence, copied into an artifact root, copied again into the Inspector's serving directory, and
/// finally streamed over HTTP. Every one of those hops opens the bytes, and each therefore has to
/// re-prove containment on the handle it is about to read — a link swapped in between two hops is
/// exactly the attack the guard exists for.
/// </para>
/// <para>
/// Passing a bare string down that chain forces each hop to guess which root to check against, and
/// the only way to guess is to reach for a concrete surface — which is wrong the moment a second
/// surface exists, and wrong again after a hop that legitimately re-homes the file. Carrying the
/// authority alongside the path removes the guess: a consumer re-validates through
/// <see cref="ResolveForRead"/> without knowing, or being able to name, whose directory it is.
/// </para>
/// </summary>
public sealed class DeviceRecordingCapture
{
    /// <summary>
    /// Creates a capture for <paramref name="reportedPath"/>, which <paramref name="authority"/>
    /// must vouch for. The canonical spelling the authority returns is what gets stored, so a
    /// capture can never carry a path its own authority would refuse.
    /// <para>
    /// Asking the authority here rather than trusting the caller to have asked is the difference
    /// between a type that transports a decision and one that merely claims a decision was made.
    /// A caller that passes an unvouched path has a defect, and a constructor that accepted it
    /// would hand every downstream hop something to re-validate that had never been validated
    /// once. Callers that expect refusal as a normal outcome use <see cref="TryCreate"/> instead
    /// of catching this.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="authority"/> does not vouch for <paramref name="reportedPath"/>.
    /// </exception>
    public DeviceRecordingCapture(string reportedPath, IDeviceRecordingPathAuthority authority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportedPath);
        ArgumentNullException.ThrowIfNull(authority);
        Path = authority.ResolveContainedRecordingPath(reportedPath)
            ?? throw new ArgumentException(
                "The supplied authority does not vouch for this path as one of its recordings.",
                nameof(reportedPath));
        Authority = authority;
    }

    /// <summary>
    /// A capture for a path an authority may or may not vouch for, returning <c>null</c> instead of
    /// throwing when it does not. This is the shape every hop that reads a path named by an
    /// untrusted host wants: refusal there is an ordinary answer — "there is no recording" — rather
    /// than a defect worth an exception.
    /// </summary>
    public static DeviceRecordingCapture? TryCreate(
        string? reportedPath,
        IDeviceRecordingPathAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (string.IsNullOrWhiteSpace(reportedPath))
            return null;

        // Asked exactly once. Re-validating the answer through the public constructor would open a
        // second window onto a live filesystem an attacker is assumed to be mutating, and a refusal
        // in that window would surface as the exception this overload exists to never throw.
        var vouched = authority.ResolveContainedRecordingPath(reportedPath);
        return vouched is null ? null : new DeviceRecordingCapture(authority, vouched);
    }

    private DeviceRecordingCapture(IDeviceRecordingPathAuthority authority, string vouchedPath)
    {
        Path = vouchedPath;
        Authority = authority;
    }

    /// <summary>The canonical path the authority vouched for at the moment of capture.</summary>
    public string Path { get; }

    /// <summary>The surface entitled to say whether <see cref="Path"/> is one of its recordings.</summary>
    public IDeviceRecordingPathAuthority Authority { get; }

    /// <summary>
    /// Re-proves containment immediately before the bytes are opened, returning the resolved path or
    /// <c>null</c> when the authority no longer vouches for it. <c>null</c> means "there is no
    /// recording to read" and must never be treated as an error to route around.
    /// </summary>
    public string? ResolveForRead() => Authority.ResolveContainedRecordingPath(Path);

    /// <summary>
    /// A capture for a copy this process has just made into a root it owns itself, so the next hop
    /// re-validates against the directory the file is now actually in rather than the one it came
    /// from. Returns <c>null</c> when the new root will not vouch for the copy, which keeps a
    /// mis-derived destination from being published as if it had been checked.
    /// </summary>
    public static DeviceRecordingCapture? ForOwnedCopy(string copiedPath, string trustedRoot)
    {
        if (string.IsNullOrWhiteSpace(copiedPath) || string.IsNullOrWhiteSpace(trustedRoot))
            return null;

        return TryCreate(copiedPath, new TrustedRootRecordingPathAuthority(trustedRoot));
    }
}

/// <summary>
/// The authority a component holds over a recording directory it owns itself — an artifact root, a
/// serving directory — as opposed to a device surface's own capture root.
/// <para>
/// It exists so that a hop which re-homes a recording can keep answering the containment question
/// for the copy it now owns, instead of handing the next hop a path that only the previous root
/// would recognise. The rules are identical to every other root's, because they are the same guard.
/// </para>
/// </summary>
public sealed class TrustedRootRecordingPathAuthority : IDeviceRecordingPathAuthority
{
    public TrustedRootRecordingPathAuthority(string trustedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedRoot);
        Root = trustedRoot;
    }

    /// <summary>The directory this authority speaks for.</summary>
    public string Root { get; }

    public string? ResolveContainedRecordingPath(string? reported)
        => DeviceRecordingPathGuard.Resolve(reported, Root);
}
