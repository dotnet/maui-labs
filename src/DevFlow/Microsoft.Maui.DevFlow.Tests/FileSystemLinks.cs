namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Filesystem link creation for tests that exercise the recording path guard.
/// <para>
/// It is shared rather than duplicated because the interesting part is not the call — it is the
/// policy about failure. A link that cannot be created is almost never a reason to skip: symlinks
/// are unprivileged on Linux and macOS, and a directory-level reparse point is creatable without
/// elevation everywhere, junctions included. The single genuine exception is a file symlink on an
/// unprivileged Windows session without Developer Mode. Letting each test invent its own
/// <c>if (!created) return;</c> is how a security assertion quietly stops running while its test
/// keeps reporting green.
/// </para>
/// </summary>
internal static class FileSystemLinks
{
    /// <summary>
    /// Creates a directory-level reparse point, falling back to a junction on Windows because that
    /// needs no elevation — and a junction is exactly the escape the guard has to survive.
    /// </summary>
    public static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return Directory.Exists(link);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return OperatingSystem.IsWindows() && TryCreateJunction(link, target);
        }
    }

    /// <summary>
    /// Creates a directory link, failing the test rather than skipping when it cannot: every
    /// supported platform can make one without elevation, so a failure here is a defect.
    /// </summary>
    public static void CreateDirectoryLink(string link, string target)
        => Assert.True(
            TryCreateDirectoryLink(link, target),
            "A directory-level reparse point is creatable without elevation on every supported " +
            $"platform, so failing to link '{link}' to '{target}' is a defect, not a reason to skip.");

    /// <summary>
    /// Creates a file symlink, reporting the failure rather than swallowing it so a caller can prove
    /// the failure is the one platform limitation that excuses skipping.
    /// </summary>
    public static bool TryCreateFileLink(string link, string target, out Exception? failure)
    {
        failure = null;
        try
        {
            File.CreateSymbolicLink(link, target);
            return File.Exists(link);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            failure = exception;
            return false;
        }
    }

    /// <summary>
    /// Creates a file symlink and returns whether the caller may proceed, refusing to let a failure
    /// pass as a silent skip. On Linux and macOS a failure fails the test outright — the alternative
    /// is a vacuous green on the platforms where the security case is actually exercisable. On
    /// Windows the skip is accepted only when the Win32 code proves the missing symlink privilege.
    /// </summary>
    public static bool CreatedFileLink(string link, string target)
    {
        if (TryCreateFileLink(link, target, out var failure))
            return true;

        Assert.True(
            OperatingSystem.IsWindows(),
            "File symbolic links are creatable without elevation on this platform, so failing to " +
            $"create one is a defect rather than a reason to skip: {Describe(failure)}");
        Assert.True(
            IsWindowsSymlinkPrivilegeFailure(failure),
            "Only ERROR_PRIVILEGE_NOT_HELD — an unprivileged Windows session without Developer " +
            "Mode — excuses skipping a file symlink case. This failure was something else, so it " +
            $"is a defect in the test rather than a platform limitation: {Describe(failure)}");
        return false;
    }

    /// <summary>
    /// The one failure that excuses a skip: <c>ERROR_PRIVILEGE_NOT_HELD</c> (1314), which is what
    /// <c>CreateSymbolicLink</c> returns on a Windows session without Developer Mode and without
    /// <c>SeCreateSymbolicLinkPrivilege</c>. .NET surfaces it as an <see cref="IOException"/> whose
    /// HRESULT is <c>0x80070522</c>, so the proof is the Win32 code and nothing else.
    /// <para>
    /// The exception's <em>type</em> proves nothing. <see cref="UnauthorizedAccessException"/> is
    /// equally what a read-only directory, a denying ACL on the scratch path, or a file the test
    /// itself left open produces, and accepting the type let any of those silently disable the
    /// containment assertion while the test still reported green — the exact failure this helper
    /// exists to prevent.
    /// </para>
    /// <para>
    /// <c>ERROR_ACCESS_DENIED</c> (5) is deliberately <em>not</em> accepted either, even though it
    /// looks like the same family. <c>0x80070005</c> is the default HRESULT carried by every
    /// <see cref="UnauthorizedAccessException"/> .NET constructs, so admitting it would re-admit
    /// precisely the unrelated denials being excluded — and a missing symlink privilege does not
    /// report itself that way in the first place.
    /// </para>
    /// </summary>
    internal static bool IsWindowsSymlinkPrivilegeFailure(Exception? failure)
    {
        if (failure is null || !OperatingSystem.IsWindows())
            return false;
        const int ErrorPrivilegeNotHeld = unchecked((int)0x80070522);
        return failure.HResult == ErrorPrivilegeNotHeld;
    }

    private static string Describe(Exception? failure)
        => failure is null
            ? "the link was reported created but does not exist"
            : $"{failure.GetType().Name} (HRESULT 0x{failure.HResult:X8}): {failure.Message}";

    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                ArgumentList = { "/c", "mklink", "/J", link, target },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (process is null)
                return false;
            process.WaitForExit(10_000);
            return Directory.Exists(link) &&
                new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
