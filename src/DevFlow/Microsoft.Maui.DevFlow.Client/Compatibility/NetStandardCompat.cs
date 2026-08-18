#if DEVFLOW_NETSTANDARD
using System.Net;
using System.Net.Sockets;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Supplies the handful of BCL overloads the protocol sources use that netstandard2.0 does not
/// declare. They are extension methods with the exact framework signatures, so the shared sources
/// compile unchanged on both targets and bind to the real BCL member on modern .NET (where this
/// file is not compiled at all).
/// </summary>
internal static class NetStandardCompat
{
    public static bool StartsWith(this string value, char c)
        => value.Length > 0 && value[0] == c;

    /// <summary>
    /// Cancellable connect for netstandard2.0, which only exposes the APM/EAP connect overloads.
    /// Cancellation disposes the socket, which is what aborts an in-flight connect; the resulting
    /// <see cref="ObjectDisposedException"/> or <see cref="SocketException"/> is translated into
    /// the <see cref="OperationCanceledException"/> the shared code expects.
    /// </summary>
    public static async Task ConnectAsync(
        this Socket socket, IPAddress address, int port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var registration = cancellationToken.Register(
            static state => SafeDispose((Socket)state!), socket);
        try
        {
            await Task.Factory.FromAsync(
                (callback, state) => socket.BeginConnect(address, port, callback, state),
                socket.EndConnect,
                state: null).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            cancellationToken.IsCancellationRequested && (ex is ObjectDisposedException || ex is SocketException))
        {
            throw new OperationCanceledException(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void SafeDispose(Socket socket)
    {
        try
        {
            socket.Dispose();
        }
        catch
        {
            // Racing the connect completion; nothing useful to do.
        }
    }
}
#endif
