// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Maui.Go;

namespace Microsoft.Maui.Go.Server;

/// <summary>
/// Comet Go dev server entry point.
/// Watches project files, compiles deltas, pushes to connected companion apps.
/// </summary>
public static class GoDevServer
{
	static readonly List<WebSocket> _clients = [];
	static readonly Lock _clientsLock = new();
	static DeltaCompiler? _compiler;
	static int _sequence;
	static string? _projectDir;

	public static async Task<int> RunAsync(string projectDir, int port, bool showQr, CancellationToken ct)
	{
		_projectDir = Path.GetFullPath(projectDir);

		// Discover project
		var csproj = Directory.GetFiles(_projectDir, "*.csproj").FirstOrDefault();
		if (csproj is null)
		{
			Console.Error.WriteLine($"No .csproj found in {_projectDir}");
			return 1;
		}

		var projectName = Path.GetFileNameWithoutExtension(csproj);
		Console.WriteLine();
		Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
		Console.WriteLine("║           Comet Go Dev Server                            ║");
		Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
		Console.WriteLine();
		Console.WriteLine($"  Project:  {projectName}");
		Console.WriteLine($"  Path:     {_projectDir}");
		Console.WriteLine($"  Port:     {port}");

		// Initialize the incremental compiler
		Console.WriteLine();
		Console.Write("  Compiling initial assembly...");
		_compiler = new DeltaCompiler(_projectDir, projectName);
		var initResult = _compiler.CompileInitial();

		if (!initResult.Success)
		{
			Console.WriteLine(" FAILED");
			Console.WriteLine();
			foreach (var err in initResult.Errors)
				Console.WriteLine($"  * {err}");
			return 1;
		}

		Console.WriteLine($" OK ({initResult.Pe!.Length} bytes)");

		// Show connection info
		var localIp = GetLocalIPAddress();
		var serverUrl = $"ws://{localIp}:{port}{GoProtocol.DefaultPath}";
		Console.WriteLine();
		Console.WriteLine($"  Server:   {serverUrl}");

		if (showQr)
		{
			Console.WriteLine();
			PrintQrCode(serverUrl);
		}

		Console.WriteLine();
		Console.WriteLine("  Waiting for connections... (Ctrl+C to stop)");
		Console.WriteLine();

		// Start HTTP + WebSocket listener
		var httpListener = new HttpListener();
		httpListener.Prefixes.Add($"http://+:{port}/");
		try
		{
			httpListener.Start();
		}
		catch (HttpListenerException)
		{
			// Fall back to localhost only
			httpListener = new HttpListener();
			httpListener.Prefixes.Add($"http://localhost:{port}/");
			httpListener.Start();
		}

		// Accept WebSocket connections
		var acceptTask = AcceptClientsAsync(httpListener, projectName, ct);

		// Start file watcher
		var watcherTask = WatchFilesAsync(ct);

		// Keepalive
		var pingTask = PingLoopAsync(ct);

		try
		{
			await Task.WhenAny(acceptTask, watcherTask, pingTask);
		}
		catch (OperationCanceledException) { }

		httpListener.Stop();
		return 0;
	}

	static async Task AcceptClientsAsync(HttpListener listener, string projectName, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			var context = await listener.GetContextAsync().WaitAsync(ct);

			if (!context.Request.IsWebSocketRequest)
			{
				// Serve basic info page for browser requests
				context.Response.StatusCode = 200;
				context.Response.ContentType = "text/plain";
				var msg = Encoding.UTF8.GetBytes($"Comet Go Dev Server — {projectName}\nConnect with the Comet Go companion app.");
				await context.Response.OutputStream.WriteAsync(msg, ct);
				context.Response.Close();
				continue;
			}

			var wsContext = await context.AcceptWebSocketAsync(null);
			var ws = wsContext.WebSocket;

			lock (_clientsLock)
				_clients.Add(ws);

			Console.WriteLine($"  Device connected! ({_clients.Count} total)");

			// Send welcome + initial assembly on a background task
			_ = HandleClientAsync(ws, projectName, ct);
		}
	}

	static async Task HandleClientAsync(WebSocket ws, string projectName, CancellationToken ct)
	{
		try
		{
			// Wait for Hello message
			var buffer = new byte[4096];
			var result = await ws.ReceiveAsync(buffer, ct);

			if (result.MessageType == WebSocketMessageType.Binary && result.Count > GoProtocol.HeaderSize)
			{
				var (type, payload) = GoProtocol.ParseFrame(buffer.AsMemory(0, result.Count));
				if (type == GoMessageType.Hello)
				{
					var hello = GoProtocol.DecodeJson<HelloMessage>(payload.Span);
					Console.WriteLine($"  {hello.DeviceName} ({hello.Platform}) — MetadataUpdate: {hello.SupportsMetadataUpdate}");
				}
			}

			// Send Welcome
			var welcome = GoProtocol.EncodeJson(GoMessageType.Welcome, new WelcomeMessage
			{
				ProjectName = projectName,
				AssemblyName = _compiler!.AssemblyName,
				ServerPort = GoProtocol.DefaultPort,
			});
			await ws.SendAsync(welcome, WebSocketMessageType.Binary, true, ct);

			// Send initial assembly
			var initFrame = GoProtocol.EncodeInitialAssembly(
				_compiler.AssemblyName,
				_compiler.CurrentPe!,
				_compiler.CurrentPdb!);
			await ws.SendAsync(initFrame, WebSocketMessageType.Binary, true, ct);
			Console.WriteLine($"  Sent initial assembly ({_compiler.CurrentPe!.Length} bytes)");

			// Keep reading (for pong responses, future commands)
			while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
			{
				result = await ws.ReceiveAsync(buffer, ct);
				if (result.MessageType == WebSocketMessageType.Close)
					break;
			}
		}
		catch (WebSocketException) { }
		catch (OperationCanceledException) { }
		finally
		{
			lock (_clientsLock)
				_clients.Remove(ws);
			Console.WriteLine($"  Device disconnected ({_clients.Count} remaining)");
		}
	}

	static async Task WatchFilesAsync(CancellationToken ct)
	{
		using var watcher = new FileSystemWatcher(_projectDir!)
		{
			Filter = "*.cs",
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
			EnableRaisingEvents = true,
		};

		// Debounce: wait for writes to settle
		var debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
		var pendingFiles = new HashSet<string>();
		var pendingLock = new Lock();

		debounceTimer.Elapsed += async (_, _) =>
		{
			string[] files;
			lock (pendingLock)
			{
				files = [.. pendingFiles];
				pendingFiles.Clear();
			}

			if (files.Length == 0) return;

			await CompileAndPushDelta(files);
		};

		watcher.Changed += (_, e) =>
		{
			if (e.FullPath.Contains("/obj/") || e.FullPath.Contains("/bin/") ||
				e.FullPath.Contains("\\obj\\") || e.FullPath.Contains("\\bin\\"))
				return;

			lock (pendingLock)
				pendingFiles.Add(e.FullPath);
			debounceTimer.Stop();
			debounceTimer.Start();
		};

		watcher.Created += (_, e) =>
		{
			lock (pendingLock)
				pendingFiles.Add(e.FullPath);
			debounceTimer.Stop();
			debounceTimer.Start();
		};

		// Block until cancelled
		await Task.Delay(Timeout.Infinite, ct);
	}

	static async Task CompileAndPushDelta(string[] changedFiles)
	{
		var relativePaths = changedFiles
			.Select(f => Path.GetRelativePath(_projectDir!, f))
			.ToArray();

		Console.Write($"  Change detected: {string.Join(", ", relativePaths)}... ");

		var result = _compiler!.CompileDelta();

		if (!result.Success)
		{
			Console.WriteLine("COMPILE ERROR");

			// Send compilation errors to clients
			var errorMsg = GoProtocol.EncodeJson(GoMessageType.CompilationError, new CompilationErrorMessage
			{
				Errors = result.Errors.Select(e => new CompilationDiagnostic
				{
					Message = e,
					Severity = "Error",
				}).ToList()
			});

			await BroadcastAsync(errorMsg);

			foreach (var err in result.Errors)
				Console.WriteLine($"    * {err}");
			return;
		}

		if (result.MetadataDelta is null)
		{
			if (result.Errors.Count > 0)
			{
				// Unsupported edit — tell the device what happened
				Console.WriteLine("RESTART REQUIRED");
				Console.WriteLine($"    {result.Errors[0]}");
				var restartMsg = GoProtocol.EncodeJson(GoMessageType.RestartRequired, new RestartRequiredMessage
				{
					Reason = result.Errors[0]
				});
				await BroadcastAsync(restartMsg);
			}
			else
			{
				Console.WriteLine("no changes");
			}
			return;
		}

		var seq = Interlocked.Increment(ref _sequence);
		var delta = new DeltaPayload(seq, _compiler.AssemblyName,
			result.MetadataDelta, result.ILDelta!, result.PdbDelta ?? []);

		var frame = GoProtocol.EncodeDelta(delta);
		await BroadcastAsync(frame);

		Console.WriteLine($"OK (delta #{seq}: {result.MetadataDelta.Length + result.ILDelta!.Length} bytes → {_clients.Count} device(s))");
	}

	static async Task BroadcastAsync(byte[] frame)
	{
		WebSocket[] clients;
		lock (_clientsLock)
			clients = [.. _clients];

		var tasks = clients
			.Where(ws => ws.State == WebSocketState.Open)
			.Select(ws => ws.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None));

		try { await Task.WhenAll(tasks); }
		catch (WebSocketException) { }
	}

	static async Task PingLoopAsync(CancellationToken ct)
	{
		var pingFrame = GoProtocol.EncodePingPong(GoMessageType.Ping);
		while (!ct.IsCancellationRequested)
		{
			await Task.Delay(15_000, ct);
			await BroadcastAsync(pingFrame);
		}
	}

	static string GetLocalIPAddress()
	{
		try
		{
			foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (ni.OperationalStatus != OperationalStatus.Up) continue;
				if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

				foreach (var addr in ni.GetIPProperties().UnicastAddresses)
				{
					if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
						return addr.Address.ToString();
				}
			}
		}
		catch { }
		return "localhost";
	}

	static void PrintQrCode(string url)
	{
		try
		{
			var qrGenerator = new QRCoder.QRCodeGenerator();
			var qrData = qrGenerator.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.L);
			var qrCode = new QRCoder.AsciiQRCode(qrData);
			var qrString = qrCode.GetGraphic(1, drawQuietZones: false);
			Console.WriteLine(qrString);
		}
		catch
		{
			Console.WriteLine($"  (QR generation failed — connect manually: {url})");
		}
	}
}
