using System.Net;
using System.Net.Sockets;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Hands out a loopback port that no other test in this process has been given.
/// </summary>
/// <remarks>
/// Asking the OS for port 0 and then releasing the listener before the real server binds is a
/// time-of-check/time-of-use race: the port goes straight back into the ephemeral pool and the
/// next test to ask can be handed the same number, so one of the two servers fails to bind or,
/// worse, a client silently talks to the other test's server. With this suite running many such
/// harnesses in parallel inside a single process, that race is the dominant source of "did not
/// start" flakes. Remembering every port handed out removes the in-process half of it; the probe
/// listener is still held while the port is claimed, so two concurrent callers cannot collide
/// either. Ports handed to other processes remain outside our control.
/// <para>
/// The handed-out set is never trimmed, so every reservation permanently retires a port for the
/// lifetime of the process. That is deliberate: reuse is exactly what this class exists to
/// prevent, and it is only affordable because the suite reserves a few hundred ports against an
/// ephemeral range of roughly 16k. A suite an order of magnitude larger would start losing the
/// 128-attempt retry loop and would need a bounded ring with an age-based eviction policy instead.
/// </para>
/// </remarks>
internal static class TestPorts
{
    private static readonly HashSet<int> Handed = [];
    private static readonly object Gate = new();

    internal static int Reserve()
    {
        for (var attempt = 0; attempt < 128; attempt++)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            lock (Gate)
            {
                if (Handed.Add(port))
                    return port;
            }
        }

        throw new InvalidOperationException(
            "Could not obtain a loopback port that this test process has not already used.");
    }
}
