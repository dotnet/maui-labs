// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Sockets;

namespace Microsoft.Maui.Cli.Commands;

internal sealed class ReservedTcpPort(int port, TcpListener listener) : IDisposable
{
	bool _disposed;
	TcpListener? _listener = listener;

	public int Port { get; } = port;

	public TcpListener DetachListener()
	{
		if (_disposed || _listener is null)
			throw new ObjectDisposedException(nameof(ReservedTcpPort));

		var detached = _listener;
		_listener = null;
		_disposed = true;
		return detached;
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_listener?.Stop();
		_listener = null;
		_disposed = true;
	}
}
