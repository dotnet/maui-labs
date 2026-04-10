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
using Microsoft.Maui.HotReload;

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

		Console.WriteLine($"[GoClient] DOTNET_MODIFIABLE_ASSEMBLIES = {Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES")}");
		Console.WriteLine($"[GoClient] MetadataUpdater.IsSupported = {System.Reflection.Metadata.MetadataUpdater.IsSupported}");
		Console.WriteLine($"[GoClient] Connecting to {serverUrl}...");

		try
		{
			await _ws.ConnectAsync(new Uri(serverUrl), _cts.Token);
			Console.WriteLine("[GoClient] WebSocket connected!");

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
			Console.WriteLine("[GoClient] Hello sent, starting receive loop");

			StatusChanged?.Invoke("Connected — waiting for project...");
			Connected?.Invoke();

			// Start receive loop
			_ = ReceiveLoopAsync();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[GoClient] Connection failed: {ex.GetType().Name}: {ex.Message}");
			Console.WriteLine($"[GoClient] Stack: {ex.StackTrace}");
			StatusChanged?.Invoke($"Connection failed: {ex.Message}");
			ErrorReceived?.Invoke(ex.Message);
		}
	}

	async Task ReceiveLoopAsync()
	{
		var buffer = new byte[1024 * 1024]; // 1MB buffer for assembly payloads
		Console.WriteLine("[GoClient] Receive loop started");

		try
		{
			while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
			{
				var segment = new ArraySegment<byte>(buffer);
				var result = await _ws.ReceiveAsync(segment, _cts.Token);
				Console.WriteLine($"[GoClient] Received: type={result.MessageType} count={result.Count} endOfMessage={result.EndOfMessage}");

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
				Console.WriteLine($"[GoClient] Message: type={type} payloadSize={payload.Length}");

				switch (type)
				{
					case GoMessageType.Welcome:
						var welcome = GoProtocol.DecodeJson<WelcomeMessage>(payload.Span);
						Console.WriteLine($"[GoClient] Welcome: project={welcome.ProjectName}");
						StatusChanged?.Invoke($"Project: {welcome.ProjectName}");
						break;

					case GoMessageType.InitialAssembly:
						Console.WriteLine($"[GoClient] InitialAssembly: {payload.Length} bytes");
						HandleInitialAssembly(payload.Span);
						break;

					case GoMessageType.Delta:
						Console.WriteLine($"[GoClient] Delta received: {payload.Length} bytes");
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
		catch (WebSocketException ex) { Console.WriteLine($"[GoClient] WS error: {ex.Message}"); }
		catch (OperationCanceledException) { Console.WriteLine("[GoClient] Receive cancelled"); }
		catch (Exception ex) { Console.WriteLine($"[GoClient] Receive error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
		finally
		{
			StatusChanged?.Invoke("Disconnected");
			Disconnected?.Invoke();
		}
	}

	void HandleInitialAssembly(ReadOnlySpan<byte> payload)
	{
		try
		{
			var (assemblyName, pe, pdb) = GoProtocol.DecodeInitialAssembly(payload);
			Console.WriteLine($"[GoClient] Decoded assembly: name={assemblyName} pe={pe.Length}b pdb={pdb.Length}b");

			// Load user assembly into default ALC
			_userAssembly = AssemblyLoadContext.Default.LoadFromStream(
				new MemoryStream(pe), new MemoryStream(pdb));
			Console.WriteLine($"[GoClient] Assembly loaded: {_userAssembly.FullName}");

			// List exported types for diagnostics
			foreach (var t in _userAssembly.GetExportedTypes())
				Console.WriteLine($"[GoClient]   Type: {t.FullName} (base: {t.BaseType?.Name})");

			StatusChanged?.Invoke($"✅ Loaded: {assemblyName}");
			AssemblyLoaded?.Invoke(_userAssembly);
			Console.WriteLine("[GoClient] AssemblyLoaded event fired");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[GoClient] HandleInitialAssembly failed: {ex.GetType().Name}: {ex.Message}");
			Console.WriteLine($"[GoClient] Stack: {ex.StackTrace}");
			ErrorReceived?.Invoke($"Assembly load failed: {ex.Message}");
		}
	}

	void HandleDelta(ReadOnlySpan<byte> payload)
	{
		if (_userAssembly is null)
		{
			ErrorReceived?.Invoke("Received delta but no assembly loaded");
			return;
		}

		var delta = GoProtocol.DecodeDelta(payload);

		Console.WriteLine($"[GoClient] Delta #{delta.Sequence}: meta={delta.MetadataDelta.Length}b IL={delta.ILDelta.Length}b PDB={delta.PdbDelta.Length}b");
		Console.WriteLine($"[GoClient] MetadataUpdater.IsSupported={MetadataUpdater.IsSupported} Assembly={_userAssembly.FullName}");

		try
		{
			// Apply the delta via the standard .NET Hot Reload mechanism.
			// NOTE: We call ApplyUpdate even if IsSupported reports false because
			// on Mono the hot_reload component may be linked but IsSupported
			// may incorrectly return false. The native code still handles the update.
			MetadataUpdater.ApplyUpdate(
				_userAssembly,
				delta.MetadataDelta,
				delta.ILDelta,
				delta.PdbDelta.Length > 0 ? delta.PdbDelta : ReadOnlySpan<byte>.Empty);

			Console.WriteLine($"[GoClient] ✅ ApplyUpdate succeeded for delta #{delta.Sequence}");

			StatusChanged?.Invoke($"🔥 Delta #{delta.Sequence} applied");
			DeltaApplied?.Invoke(delta.Sequence);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[GoClient] ❌ ApplyUpdate failed: {ex.GetType().Name}: {ex.Message}");
			Console.WriteLine($"[GoClient] Stack: {ex.StackTrace}");

			// If ApplyUpdate throws because IsSupported=false, try reflecting into
			// the internal Mono metadata update API as a fallback
			if (!MetadataUpdater.IsSupported)
			{
				Console.WriteLine("[GoClient] Attempting Mono internal hot reload path...");
				TryMonoInternalApplyUpdate(delta);
			}
			else
			{
				ErrorReceived?.Invoke($"Delta apply failed: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// Fallback: call Mono's internal ApplyUpdate_internal icall directly via reflection.
	/// This bypasses the IsSupported check that is incorrectly false due to AOT intrinsic.
	/// The hot_reload Mono component IS linked and GetCapabilities() returns valid capabilities,
	/// so the native code can handle the update.
	/// </summary>
	void TryMonoInternalApplyUpdate(DeltaPayload delta)
	{
		try
		{
			// Strategy 1: Find the internal ApplyUpdate_internal icall on MetadataUpdater
			// Signature: static extern void ApplyUpdate_internal(IntPtr base_assm, byte[] dmeta, byte[] dIL, byte[] dpdb)
			var muType = typeof(MetadataUpdater);
			var internalMethod = muType.GetMethod("ApplyUpdate_internal",
				BindingFlags.Static | BindingFlags.NonPublic,
				null,
				new[] { typeof(IntPtr), typeof(byte[]), typeof(byte[]), typeof(byte[]) },
				null);

			if (internalMethod != null)
			{
				Console.WriteLine("[GoClient] Found ApplyUpdate_internal icall, attempting direct call...");

				// Get the native assembly handle via reflection
				// Assembly._mono_assembly is the IntPtr on Mono
				var handleField = typeof(Assembly).GetField("_mono_assembly", BindingFlags.Instance | BindingFlags.NonPublic)
					?? typeof(Assembly).GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic);

				IntPtr nativeHandle = IntPtr.Zero;
				if (handleField != null)
				{
					var val = handleField.GetValue(_userAssembly);
					if (val is IntPtr ptr) nativeHandle = ptr;
					else Console.WriteLine($"[GoClient] Handle field type: {val?.GetType().Name ?? "null"}");
				}
				else
				{
					// Alternative: use RuntimeAssembly.GetNativeHandle()
					var getHandle = _userAssembly!.GetType().GetMethod("GetNativeHandle",
						BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					if (getHandle != null)
					{
						nativeHandle = (IntPtr)getHandle.Invoke(_userAssembly, null)!;
					}
					else
					{
						Console.WriteLine("[GoClient] Could not get native assembly handle");
						// Dump fields for diagnosis
						foreach (var f in typeof(Assembly).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
							Console.WriteLine($"[GoClient]   Field: {f.Name} ({f.FieldType.Name})");
					}
				}

				if (nativeHandle != IntPtr.Zero)
				{
					internalMethod.Invoke(null, new object[]
					{
						nativeHandle,
						delta.MetadataDelta.ToArray(),
						delta.ILDelta.ToArray(),
						delta.PdbDelta.Length > 0 ? delta.PdbDelta.ToArray() : Array.Empty<byte>()
					});

					Console.WriteLine($"[GoClient] ✅ ApplyUpdate_internal succeeded for delta #{delta.Sequence}");
					TriggerCometReload();
					StatusChanged?.Invoke($"🔥 Delta #{delta.Sequence} applied");
					DeltaApplied?.Invoke(delta.Sequence);
					return;
				}
			}
			else
			{
				Console.WriteLine("[GoClient] ApplyUpdate_internal not found, dumping MetadataUpdater methods:");
				foreach (var m in muType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
					Console.WriteLine($"[GoClient]   {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
			}

			// Strategy 2: Try calling ApplyUpdate but with reflection to skip the IsSupported check
			// We can get the underlying delegate from the method and invoke it after the guard
			Console.WriteLine("[GoClient] Attempting strategy 2: AssemblyExtensions...");
			var assemblyExtType = typeof(AssemblyLoadContext).Assembly
				.GetType("System.Reflection.Metadata.AssemblyExtensions");
			if (assemblyExtType != null)
			{
				Console.WriteLine("[GoClient] Found AssemblyExtensions, listing methods:");
				foreach (var m in assemblyExtType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
					Console.WriteLine($"[GoClient]   {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
			}

			ErrorReceived?.Invoke("Could not find internal hot reload method");
		}
		catch (TargetInvocationException tie) when (tie.InnerException != null)
		{
			Console.WriteLine($"[GoClient] ❌ Internal apply failed: {tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
			Console.WriteLine($"[GoClient] Stack: {tie.InnerException.StackTrace}");
			ErrorReceived?.Invoke($"Hot reload failed: {tie.InnerException.Message}");
		}
		catch (Exception ex2)
		{
			Console.WriteLine($"[GoClient] ❌ Internal apply failed: {ex2.GetType().Name}: {ex2.Message}");
			ErrorReceived?.Invoke($"Hot reload failed: {ex2.Message}");
		}
	}

	/// <summary>
	/// Manually trigger Comet's hot reload pipeline.
	/// This is a safety net in case the standard [MetadataUpdateHandler] 
	/// attribute-based invocation doesn't fire automatically.
	/// </summary>
	static void TriggerCometReload()
	{
		try
		{
			MauiHotReloadHelper.TriggerReload();
			Console.WriteLine("[GoClient] Comet TriggerReload() called");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[GoClient] TriggerReload failed: {ex.Message}");
		}
	}

	public void Dispose()
	{
		_cts.Cancel();
		_ws.Dispose();
		_cts.Dispose();
	}
}
