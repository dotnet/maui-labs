namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The policy about when a link failure may be treated as a skip.
/// <para>
/// A skipped security case is indistinguishable from a passing one in a test report, so the
/// predicate that grants the skip is itself worth pinning. It has exactly one job: prove the
/// failure was the platform limitation it claims to be, and fail the test for everything else.
/// </para>
/// </summary>
public class FileSystemLinksTests
{
    /// <summary>
    /// <c>ERROR_PRIVILEGE_NOT_HELD</c> is the only thing that excuses skipping a file symlink case,
    /// and only on Windows — every other supported platform creates file symlinks unprivileged, so
    /// there is no limitation there to excuse anything.
    /// </summary>
    [Fact]
    public void OnlyTheMissingSymlinkPrivilegeExcusesASkip()
    {
        var privilegeNotHeld = new IOException("A required privilege is not held by the client.")
        {
            HResult = unchecked((int)0x80070522),
        };

        Assert.Equal(
            OperatingSystem.IsWindows(),
            FileSystemLinks.IsWindowsSymlinkPrivilegeFailure(privilegeNotHeld));
    }

    /// <summary>
    /// The exception type is not evidence. A read-only directory, a denying ACL on the scratch
    /// path, or a file the test itself left open all raise <see cref="UnauthorizedAccessException"/>
    /// — and its default HRESULT is <c>0x80070005</c>, the very <c>ERROR_ACCESS_DENIED</c> code that
    /// used to be accepted. Every one of these is a defect in the test, so every one must fail it.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnrelatedFailures))]
    public void AnUnrelatedFailureIsADefectRatherThanASkip(Exception failure)
        => Assert.False(FileSystemLinks.IsWindowsSymlinkPrivilegeFailure(failure));

    public static TheoryData<Exception> UnrelatedFailures() =>
    [
        new UnauthorizedAccessException("Access to the path is denied."),
        new UnauthorizedAccessException("Access to the path is denied.")
        {
            HResult = unchecked((int)0x80070005),
        },
        new IOException("The disk is full.") { HResult = unchecked((int)0x80070070) },
        new IOException("Could not find a part of the path.") { HResult = unchecked((int)0x80070003) },
        new PlatformNotSupportedException(),
        new ArgumentException("The path is invalid."),
    ];

    /// <summary>A link that was never attempted proves nothing at all.</summary>
    [Fact]
    public void AnAbsentFailureIsNeverASkip()
        => Assert.False(FileSystemLinks.IsWindowsSymlinkPrivilegeFailure(null));
}
