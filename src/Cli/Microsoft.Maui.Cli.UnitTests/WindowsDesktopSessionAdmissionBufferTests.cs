using System;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// The WTSINFOEX buffer layout is why Windows flow admission failed on every machine it was tried
/// on: the union holding the level-1 data is 8-byte aligned, so reading the session id immediately
/// after Level returns alignment padding instead, the id never matches, and admission reports
/// 'desktop-lock-state-unavailable' on a perfectly usable desktop. The buffers below mirror the
/// layout observed from a live WTSQuerySessionInformation call on an active, unlocked session:
/// Level=1, pad, SessionId=2, SessionState=0, SessionFlags=1. Every offset here is hard-coded
/// rather than derived from <see cref="IntPtr.Size"/>, so an x86 test run cannot agree with a
/// production assumption that happens to be wrong on x86.
/// </summary>
public class WindowsDesktopSessionAdmissionBufferTests
{
    private const int Level1 = 1;
    private const int Unlocked = 1;
    private const int Locked = 0;

    /// <summary>The level-1 data offset, spelled out rather than computed from pointer width.</summary>
    private const int DataOffset = 8;

    /// <summary>Smallest buffer that still contains Level, SessionId, SessionState and SessionFlags.</summary>
    private const int SmallestUsableBuffer = DataOffset + (sizeof(int) * 3);

    private static Func<int, int> Buffer(int level, int sessionId, int sessionFlags)
        => offset => offset switch
        {
            0 => level,
            DataOffset => sessionId,
            DataOffset + sizeof(int) => 0, // SessionState: active
            DataOffset + (sizeof(int) * 2) => sessionFlags,
            _ => 0,
        };

    [Fact]
    public void ReadsTheSessionIdPastTheUnionAlignmentPadding()
    {
        var read = WindowsWtsSessionApi.TryReadDesktopLockState(
            Buffer(Level1, sessionId: 2, sessionFlags: Unlocked),
            bytesReturned: 160,
            sessionId: 2,
            out var state);

        Assert.True(read);
        Assert.Equal(WindowsDesktopLockState.Unlocked, state);
    }

    [Fact]
    public void ReportsALockedDesktopFromTheSameOffsets()
    {
        var read = WindowsWtsSessionApi.TryReadDesktopLockState(
            Buffer(Level1, sessionId: 2, sessionFlags: Locked),
            bytesReturned: 160,
            sessionId: 2,
            out var state);

        Assert.True(read);
        Assert.Equal(WindowsDesktopLockState.Locked, state);
    }

    /// <summary>
    /// The one assertion that fails on every architecture if production goes back to deriving the
    /// offset from pointer width. The buffer-shaped tests below can only catch that on x86, which
    /// is not where this suite normally runs.
    /// </summary>
    [Fact]
    public void TheLevel1DataOffsetIsAConstantEightOnEveryArchitecture()
    {
        Assert.Equal(DataOffset, WindowsWtsSessionApi.WtsInfoExLevel1DataOffset);
    }

    /// <summary>
    /// A buffer whose session id sits immediately after Level is the layout a 4-byte-aligned reader
    /// would expect, and it is not the layout Windows produces anywhere. Refusing it pins the
    /// parser to the real record on any run, and fails outright on an x86 run of a reverted fix.
    /// </summary>
    [Fact]
    public void RefusesABufferWhoseSessionIdSitsImmediatelyAfterLevel()
    {
        var read = WindowsWtsSessionApi.TryReadDesktopLockState(
            offset => offset switch
            {
                0 => Level1,
                sizeof(int) => 2, // where a 4-byte-aligned reader would look, and find nothing real
                _ => 0,
            },
            bytesReturned: 160,
            sessionId: 2,
            out var state);

        Assert.False(read);
        Assert.Equal(WindowsDesktopLockState.Unknown, state);
    }

    /// <summary>
    /// Pins the same constant from the length side. A real WTSINFOEXW is far larger than 20 bytes;
    /// 20 is only the lower bound this parser must enforce, because it is the last byte of
    /// SessionFlags. A reader that assumed a 4-byte data offset would settle for 16.
    /// </summary>
    [Fact]
    public void RequiresTheWholeAlignedRecordBeforeTrustingIt()
    {
        var buffer = Buffer(Level1, sessionId: 2, sessionFlags: Unlocked);

        Assert.False(WindowsWtsSessionApi.TryReadDesktopLockState(
            buffer,
            bytesReturned: SmallestUsableBuffer - 1,
            sessionId: 2,
            out var truncated));
        Assert.Equal(WindowsDesktopLockState.Unknown, truncated);

        Assert.True(WindowsWtsSessionApi.TryReadDesktopLockState(
            buffer,
            bytesReturned: SmallestUsableBuffer,
            sessionId: 2,
            out var complete));
        Assert.Equal(WindowsDesktopLockState.Unlocked, complete);
    }

    [Fact]
    public void RefusesABufferWhoseSessionIdDoesNotMatchTheQueriedSession()
    {
        var read = WindowsWtsSessionApi.TryReadDesktopLockState(
            Buffer(Level1, sessionId: 7, sessionFlags: Unlocked),
            bytesReturned: 160,
            sessionId: 2,
            out var state);

        Assert.False(read);
        Assert.Equal(WindowsDesktopLockState.Unknown, state);
    }

    [Fact]
    public void RefusesAnUnexpectedInfoLevel()
    {
        var read = WindowsWtsSessionApi.TryReadDesktopLockState(
            Buffer(level: 2, sessionId: 2, sessionFlags: Unlocked),
            bytesReturned: 160,
            sessionId: 2,
            out var state);

        Assert.False(read);
        Assert.Equal(WindowsDesktopLockState.Unknown, state);
    }

    [Fact]
    public void RefusesATruncatedBuffer()
    {
        var read = WindowsWtsSessionApi.TryReadDesktopLockState(
            Buffer(Level1, sessionId: 2, sessionFlags: Unlocked),
            bytesReturned: 8,
            sessionId: 2,
            out var state);

        Assert.False(read);
        Assert.Equal(WindowsDesktopLockState.Unknown, state);
    }
}
