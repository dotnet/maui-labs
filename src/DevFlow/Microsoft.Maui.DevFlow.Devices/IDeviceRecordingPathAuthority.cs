namespace Microsoft.Maui.DevFlow.Devices;

/// <summary>
/// Implemented by a device surface that owns a trusted directory for its own recordings and can
/// therefore say whether a reported path is one of them.
/// <para>
/// A recording path arrives from an untrusted device host and is then read, hashed, and published
/// as evidence, so every consumer re-proves containment on the handle it is about to open rather
/// than trusting the path it was handed. Only the surface that told the host where to write knows
/// what "contained" means, so the answer belongs here — a caller that reached for one surface's
/// root while driving another would be validating against a directory nothing writes to.
/// </para>
/// <para>
/// Deliberately separate from <see cref="IDeviceSurface"/>: recording is optional, most surfaces
/// never produce a file, and a default implementation on the main interface would invite a backend
/// to inherit an answer it has no standing to give. A surface that does not implement this cannot
/// have its recordings published as evidence, which is the safe direction — an unverifiable path
/// yields no artifact rather than an unverified one.
/// </para>
/// </summary>
public interface IDeviceRecordingPathAuthority
{
    /// <summary>
    /// Returns the fully resolved path when <paramref name="reported"/> names a recording this
    /// surface actually owns, and <c>null</c> otherwise. <c>null</c> means "there is no recording"
    /// and must never be treated as an error to route around.
    /// <para>
    /// Implementations must resolve links before deciding, and must be idempotent: a path returned
    /// here is accepted again unchanged, because callers validate the same handle more than once.
    /// </para>
    /// </summary>
    string? ResolveContainedRecordingPath(string? reported);
}
