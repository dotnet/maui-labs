// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Go;

namespace Microsoft.Maui.Go.CompanionApp;

/// <summary>
/// Connects to the MAUI Go dev server, receives hot reload deltas,
/// and applies them via MetadataUpdater.ApplyUpdate().
/// </summary>
public sealed class GoClient : IDisposable
{
	readonly ClientWebSocket _ws = new();
	readonly CancellationTokenSource _cts = new();
	Assembly? _userAssembly;
	string _serverUrl = "";

	public event Action<string>? StatusChanged;
	public event Action<string>? ErrorReceived;
	public event Action<int>? DeltaApplied;
	public event Action<Assembly>? AssemblyLoaded;
	public event Action? Connected;
	public event Action? Disconnected;
	public event Action<string>? RestartRequired;

	public bool IsConnected => _ws.State == WebSocketState.Open;

	/// <summary>
	/// Connect to the dev server and start receiving updates.
	/// </summary>
	public async Task ConnectAsync(string serverUrl)
	{
		_serverUrl = serverUrl;
		StatusChanged?.Invoke("Connecting...");

		try
		{
			await _ws.ConnectAsync(new Uri(serverUrl), _cts.Token);

			// Send Hello
			var hello = GoProtocol.EncodeJson(GoMessageType.Hello, new HelloMessage
			{
				DeviceId = Guid.NewGuid().ToString("N")[..8],
				DeviceName = Microsoft.Maui.Devices.DeviceInfo.Name,
				Platform = Microsoft.Maui.Devices.DeviceInfo.Platform.ToString(),
				RuntimeVersion = Environment.Version.ToString(),
				SupportsMetadataUpdate = System.Reflection.Metadata.MetadataUpdater.IsSupported,
			});
			await _ws.SendAsync(hello, WebSocketMessageType.Binary, true, _cts.Token);

			StatusChanged?.Invoke("Connected — waiting for project...");
			Connected?.Invoke();

			// Start receive loop
			_ = ReceiveLoopAsync();
		}
		catch (Exception ex)
		{
			StatusChanged?.Invoke($"Connection failed: {ex.Message}");
			ErrorReceived?.Invoke(ex.Message);
		}
	}

	async Task ReceiveLoopAsync()
	{
		var buffer = new byte[1024 * 1024]; // 1MB buffer for assembly payloads

		try
		{
			while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
			{
				var segment = new ArraySegment<byte>(buffer);
				var result = await _ws.ReceiveAsync(segment, _cts.Token);

				if (result.MessageType == WebSocketMessageType.Close)
					break;

				if (result.MessageType != WebSocketMessageType.Binary || result.Count < GoProtocol.HeaderSize)
					continue;

				// Collect full message (may span multiple frames)
				var totalBytes = result.Count;
				while (!result.EndOfMessage)
				{
					var remaining = new ArraySegment<byte>(buffer, totalBytes, buffer.Length - totalBytes);
					result = await _ws.ReceiveAsync(remaining, _cts.Token);
					totalBytes += result.Count;
				}

				var frame = buffer.AsMemory(0, totalBytes);
				var (type, payload) = GoProtocol.ParseFrame(frame);

				switch (type)
				{
					case GoMessageType.Welcome:
						var welcome = GoProtocol.DecodeJson<WelcomeMessage>(payload.Span);
						StatusChanged?.Invoke($"Project: {welcome.ProjectName}");
						break;

					case GoMessageType.InitialAssembly:
						HandleInitialAssembly(payload.Span);
						break;

					case GoMessageType.Delta:
						HandleDelta(payload.Span);
						break;

					case GoMessageType.CompilationError:
						var errors = GoProtocol.DecodeJson<CompilationErrorMessage>(payload.Span);
						var errorText = string.Join("\n", errors.Errors.Select(e => $"{e.FilePath}({e.Line}): {e.Message}"));
						ErrorReceived?.Invoke(errorText);
						StatusChanged?.Invoke($"⚠️ {errors.Errors.Count} compilation error(s)");
						break;

					case GoMessageType.RestartRequired:
						var restart = GoProtocol.DecodeJson<RestartRequiredMessage>(payload.Span);
						RestartRequired?.Invoke(restart.Reason);
						StatusChanged?.Invoke("🔄 Restart required");
						break;

					case GoMessageType.Ping:
						var pong = GoProtocol.EncodePingPong(GoMessageType.Pong);
						await _ws.SendAsync(pong, WebSocketMessageType.Binary, true, _cts.Token);
						break;
				}
			}
		}
		catch (WebSocketException) { }
		catch (OperationCanceledException) { }
		finally
		{
			StatusChanged?.Invoke("Disconnected");
			Disconnected?.Invoke();
		}
	}

	void HandleInitialAssembly(ReadOnlySpan<byte> payload)
	{
		var (assemblyName, pe, pdb) = GoProtocol.DecodeInitialAssembly(payload);

		// Load user assembly into default ALC
		_userAssembly = AssemblyLoadContext.Default.LoadFromStream(
			new MemoryStream(pe), new MemoryStream(pdb));

		StatusChanged?.Invoke($"✅ Loaded: {assemblyName}");
		AssemblyLoaded?.Invoke(_userAssembly);
	}

	void HandleDelta(ReadOnlySpan<byte> payload)
	{
		if (_userAssembly is null)
		{
			ErrorReceived?.Invoke("Received delta but no assembly loaded");
			return;
		}

		var delta = GoProtocol.DecodeDelta(payload);

		try
		{
			// Apply the delta via the standard .NET Hot Reload mechanism
			System.Reflection.Metadata.MetadataUpdater.ApplyUpdate(
				_userAssembly,
				delta.MetadataDelta,
				delta.ILDelta,
				delta.PdbDelta.Length > 0 ? delta.PdbDelta : ReadOnlySpan<byte>.Empty);

			StatusChanged?.Invoke($"🔥 Delta #{delta.Sequence} applied");
			DeltaApplied?.Invoke(delta.Sequence);
		}
		catch (Exception ex)
		{
			ErrorReceived?.Invoke($"Delta apply failed: {ex.Message}");
		}
	}

	public void Dispose()
	{
		_cts.Cancel();
		_ws.Dispose();
		_cts.Dispose();
	}
}
