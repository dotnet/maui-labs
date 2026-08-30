namespace Microsoft.Maui.DevFlow.Devices;

/// <summary>
/// Decides whether a path DevFlow was handed may be opened as a device recording.
/// <para>
/// Two different untrusted parties name these paths. The device host names the file it wrote, and
/// the Inspector then copies, hashes, and serves it. Neither is entitled to point DevFlow at an
/// arbitrary local file, so containment is checked against a root DevFlow owns.
/// </para>
/// <para>
/// A lexical prefix check alone is not containment. A symlink, a directory junction, or any other
/// reparse point <em>inside</em> the trusted root passes the string comparison while the bytes that
/// are actually read come from somewhere else entirely — and on Windows an alternate data stream
/// suffix (<c>run.mp4:hidden</c>) reads a different stream of the same file. So the path is
/// resolved to its final target, segment by segment, and re-checked against the resolved root.
/// </para>
/// </summary>
public static class DeviceRecordingPathGuard
{
    /// <summary>Bounds link chasing so a link cycle is a refusal, not a hang.</summary>
    private const int MaxSegments = 128;

    private static StringComparison Comparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Returns the fully resolved path when <paramref name="reported"/> names an <c>.mp4</c> whose
    /// final target lies inside <paramref name="trustedRoot"/>, and <c>null</c> otherwise. Callers
    /// must treat <c>null</c> as "there is no recording" rather than as an error to route around.
    /// <para>
    /// The result is idempotent: feeding a returned path back in, against the same root, yields
    /// that same path. Callers re-prove containment on a handle before reading or hashing it, so a
    /// guard that refused its own output would drop legitimate recordings on any machine whose
    /// recording root is reached through a link.
    /// </para>
    /// </summary>
    public static string? Resolve(string? reported, string trustedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedRoot);
        if (string.IsNullOrWhiteSpace(reported))
            return null;

        string full;
        string root;
        try
        {
            full = Path.GetFullPath(reported);
            root = Path.GetFullPath(trustedRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or IOException
                or System.Security.SecurityException)
        {
            return null;
        }

        // Refuse before touching the filesystem: an alternate data stream never names a recording,
        // and its presence means the bytes read are not the bytes the file's own metadata describes.
        if (HasAlternateDataStream(full) || HasAlternateDataStream(root))
            return null;
        if (!full.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            return null;

        // A root of "C:\" or "/" is not a trust boundary — it contains every .mp4 on the machine,
        // so containment would be satisfied by any file the host cared to name. A caller that
        // arrives here with one has mis-derived its root (an empty configured directory collapsing
        // to a drive letter is the usual way), and answering "contained" would turn that mistake
        // into arbitrary local file disclosure. It is refused rather than repaired, because there
        // is no narrower root this method could invent on the caller's behalf.
        if (IsFilesystemRoot(root))
            return null;

        // The root is resolved first, before anything is compared against it. A trusted root can
        // itself be reached through a link — macOS resolves /var to /private/var, and a Windows
        // TEMP under a junctioned profile behaves the same way — so the identical directory is
        // named by two different absolute paths. Comparing against only the unresolved form makes
        // this method non-idempotent: it would refuse the very path it had just returned, because
        // that path is spelled in the resolved namespace while the root is spelled in the other.
        var resolvedRoot = ResolveFinalPath(root);
        if (resolvedRoot is null)
            return null;

        // The resolved spelling is checked too: a directory link whose target is a drive root would
        // otherwise smuggle the widest possible root past the check above.
        if (IsFilesystemRoot(resolvedRoot))
            return null;

        // A cheap lexical refusal for a path that is under neither spelling of the root: it can
        // never become contained, so there is no reason to walk a hostile path's links to find out.
        // This is an optimisation, not the containment decision — that is made below, on the fully
        // resolved candidate against the fully resolved root.
        if (!IsWithin(full, root) && !IsWithin(full, resolvedRoot))
            return null;

        var resolvedFull = ResolveFinalPath(full);
        if (resolvedFull is null)
            return null;

        // The extension is re-checked on the target: a link named run.mp4 that resolves to a
        // payload with any other extension is not a recording.
        if (!resolvedFull.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            return null;
        if (HasAlternateDataStream(resolvedFull))
            return null;
        return IsWithin(resolvedFull, resolvedRoot) ? resolvedFull : null;
    }

    /// <summary>
    /// Resolves every segment of an absolute path through any reparse point it crosses, so a
    /// junction in the middle of the path cannot leave a stale trusted prefix behind. A segment
    /// that does not exist yet is kept as written, which is what lets a caller validate an output
    /// path before the host creates the file.
    /// </summary>
    internal static string? ResolveFinalPath(string absolutePath)
    {
        string full;
        try { full = Path.GetFullPath(absolutePath); }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }

        var pathRoot = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(pathRoot))
            return null;

        var segments = full[pathRoot.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > MaxSegments)
            return null;

        var current = pathRoot;
        foreach (var segment in segments)
        {
            try
            {
                current = Path.GetFullPath(Path.Combine(current, segment));
                // returnFinalTarget walks the whole chain, so one hop per segment is the complete
                // answer; a cycle surfaces as an IOException rather than as a loop here.
                FileSystemInfo? target = Directory.Exists(current)
                    ? new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true)
                    : File.Exists(current)
                        ? new FileInfo(current).ResolveLinkTarget(returnFinalTarget: true)
                        : null;
                if (target is not null)
                    current = Path.GetFullPath(target.FullName);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException
                    or NotSupportedException or PathTooLongException or System.Security.SecurityException)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>
    /// True when a Windows path carries an NTFS alternate data stream suffix. The colon is legal
    /// only in the drive specifier, so anything after the path root is a stream name.
    /// </summary>
    internal static bool HasAlternateDataStream(string absolutePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(absolutePath))
            return false;
        var pathRoot = Path.GetPathRoot(absolutePath) ?? "";
        return absolutePath.AsSpan(pathRoot.Length).IndexOf(':') >= 0;
    }

    /// <summary>Strict containment: the root itself is not a recording.</summary>
    internal static bool IsWithin(string candidate, string root)
    {
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, Comparison);
    }

    /// <summary>
    /// True when an absolute path names nothing but a volume or filesystem root — <c>C:\</c>,
    /// <c>\\server\share\</c>, or <c>/</c>. Such a "root" bounds nothing, so it is never a usable
    /// trust boundary for a recording directory.
    /// </summary>
    internal static bool IsFilesystemRoot(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return true;

        var pathRoot = Path.GetPathRoot(absolutePath);
        if (string.IsNullOrEmpty(pathRoot))
            return false;

        // Compare with a single trailing separator on both sides so "C:\" and "C:" — and "/" on
        // Unix, whose own root already carries the separator — reach the same answer.
        return string.Equals(
            Trim(absolutePath),
            Trim(pathRoot),
            Comparison);

        static string Trim(string value) =>
            value.Length > 1
                ? value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : value;
    }
}
