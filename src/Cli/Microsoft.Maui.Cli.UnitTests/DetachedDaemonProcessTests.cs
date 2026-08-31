using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Regression coverage for the broker daemon leaking the caller's stdout. The daemon used to be
/// started with <see cref="Process"/>, which on Windows always calls <c>CreateProcess</c> with
/// <c>bInheritHandles=TRUE</c>. A first run of <c>maui devflow flow run ... | Tee-Object</c> auto
/// starts the daemon, the daemon inherits the shell's end of the pipe, and the pipeline then stays
/// open for the daemon's whole lifetime even though the CLI process itself has already exited.
/// The defect and the code path that fixes it are both Windows-specific, so these tests assert
/// nothing on other platforms.
/// </summary>
public class DetachedDaemonProcessTests
{
    [Fact]
    public void Start_DoesNotLeakAnInheritablePipeIntoTheDaemon()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var daemon = StartStandInDaemon();
        Assert.NotNull(daemon);

        try
        {
            pipe.DisposeLocalCopyOfClientHandle();

            // The read returns end-of-stream only once no process holds the write handle. If the
            // daemon inherited it, this blocks until the daemon exits — which is the defect.
            var read = Task.Run(pipe.ReadByte);
            Assert.True(
                read.Wait(TimeSpan.FromSeconds(20)),
                "The daemon inherited the caller's pipe handle, so the pipeline never closed.");
            Assert.Equal(-1, read.Result);

            // Without this the test would also pass if the stand-in daemon had simply died.
            Assert.False(daemon!.HasExited, "The stand-in daemon exited early, so the read proved nothing.");
        }
        finally
        {
            KillDaemon(daemon);
        }
    }

    [Fact]
    public void Start_LeavesTheDaemonRunningAfterTheStarterDisposesIt()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var daemon = StartStandInDaemon();
        Assert.NotNull(daemon);

        try
        {
            Assert.True(daemon!.ProcessId > 0);
            Assert.False(daemon.HasExited);

            // Detaching the daemon must not kill it: staying up after `flow run` exits is the point.
            daemon.Dispose();
            using var child = Process.GetProcessById(daemon.ProcessId);
            Assert.False(child.HasExited);
        }
        finally
        {
            KillDaemon(daemon);
        }
    }

    /// <summary>A long-lived child that stands in for the daemon, which outlives the CLI on purpose.</summary>
    private static DetachedDaemonProcess? StartStandInDaemon()
        => DetachedDaemonProcess.Start(
            Environment.GetEnvironmentVariable("ComSpec")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            "/c ping -n 60 127.0.0.1 > nul");

    private static void KillDaemon(DetachedDaemonProcess? daemon)
    {
        if (daemon is null)
            return;
        try
        {
            using var child = Process.GetProcessById(daemon.ProcessId);
            child.Kill(entireProcessTree: true);
            child.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (ArgumentException)
        {
            // The stand-in daemon already exited.
        }
        catch (InvalidOperationException)
        {
            // The stand-in daemon already exited.
        }
        finally
        {
            daemon.Dispose();
        }
    }
}
