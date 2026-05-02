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
	static string? _singleFilePath;

	public static async Task<int> RunAsync(string projectDir, int port, bool showQr, CancellationToken ct)
	{
		_projectDir = Path.GetFullPath(projectDir);
		_singleFilePath = null;

		// Discover project
		var csproj = Directory.GetFiles(_projectDir, "*.csproj").FirstOrDefault();
		if (csproj is null)
		{
			Console.Error.WriteLine($"No .csproj found in {_projectDir}");
			return 1;
		}

		var projectName = Path.GetFileNameWithoutExtension(csproj);

		_compiler = new DeltaCompiler(_projectDir, projectName);
		return await RunCoreAsync(projectName, _projectDir, port, showQr, ct);
	}

	/// <summary>
	/// Runs in single-file mode — watches and compiles a single .cs file.
	/// </summary>
	public static async Task<int> RunSingleFileAsync(string csFilePath, int port, bool showQr, CancellationToken ct)
	{
		var fullPath = Path.GetFullPath(csFilePath);
		if (!File.Exists(fullPath))
		{
			Console.Error.WriteLine($"File not found: {fullPath}");
			return 1;
		}

		_singleFilePath = fullPath;
		_projectDir = Path.GetDirectoryName(fullPath)!;

		var projectName = Path.GetFileNameWithoutExtension(fullPath);

		_compiler = DeltaCompiler.ForSingleFile(fullPath);
		return await RunCoreAsync(projectName, fullPath, port, showQr, ct);
	}

	static async Task<int> RunCoreAsync(string projectName, string displayPath, int port, bool showQr, CancellationToken ct)
	{
		var mode = _singleFilePath is not null ? "single-file" : "project";
		Console.WriteLine();
		Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
		Console.WriteLine("║           Comet Go Dev Server                            ║");
		Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
		Console.WriteLine();
		Console.WriteLine($"  Mode:     {mode}");
		Console.WriteLine($"  Project:  {projectName}");
		Console.WriteLine($"  Path:     {displayPath}");
		Console.WriteLine($"  Port:     {port}");

		// Initialize the incremental compiler
		Console.WriteLine();
		Console.Write("  Compiling initial assembly...");
		var initResult = _compiler!.CompileInitial();

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
		var watcherTask = _singleFilePath is not null
			? WatchSingleFileAsync(_singleFilePath, ct)
			: WatchFilesAsync(ct);

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

			// Send initial assembly — recompile from current source for reconnecting
			// clients so they get latest code, not stale startup assembly.
			byte[] peToSend, pdbToSend;
			if (_sequence > 0)
			{
				var freshCompiler = _singleFilePath is not null
					? DeltaCompiler.ForSingleFile(_singleFilePath)
					: new DeltaCompiler(_projectDir!, _compiler.AssemblyName);
				var freshResult = freshCompiler.CompileInitial();
				if (freshResult.Success && freshResult.Pe is not null)
				{
					peToSend = freshResult.Pe;
					pdbToSend = freshResult.Pdb!;
					Console.WriteLine($"  Recompiled fresh assembly for reconnect ({peToSend.Length} bytes)");
				}
				else
				{
					peToSend = _compiler.CurrentPe!;
					pdbToSend = _compiler.CurrentPdb!;
				}
			}
			else
			{
				peToSend = _compiler.CurrentPe!;
				pdbToSend = _compiler.CurrentPdb!;
			}
			var initFrame = GoProtocol.EncodeInitialAssembly(
				_compiler.AssemblyName, peToSend, pdbToSend);
			await ws.SendAsync(initFrame, WebSocketMessageType.Binary, true, ct);
			Console.WriteLine($"  Sent initial assembly ({peToSend.Length} bytes)");

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
		using var debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
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

	/// <summary>
	/// Watches a single .cs file for changes. Handles Changed and Renamed events
	/// since many editors save via temp-file + rename.
	/// </summary>
	static async Task WatchSingleFileAsync(string filePath, CancellationToken ct)
	{
		var dir = Path.GetDirectoryName(filePath)!;
		var fileName = Path.GetFileName(filePath);

		using var watcher = new FileSystemWatcher(dir)
		{
			Filter = fileName,
			IncludeSubdirectories = false,
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
			EnableRaisingEvents = true,
		};

		using var debounceTimer = new System.Timers.Timer(300) { AutoReset = false };

		debounceTimer.Elapsed += async (_, _) =>
		{
			await CompileAndPushDelta([filePath]);
		};

		void OnFileChanged(object sender, FileSystemEventArgs e)
		{
			debounceTimer.Stop();
			debounceTimer.Start();
		}

		watcher.Changed += OnFileChanged;
		watcher.Created += OnFileChanged;
		watcher.Renamed += (_, e) =>
		{
			// Editors save via temp+rename — if the target is our file, trigger
			if (string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase))
			{
				debounceTimer.Stop();
				debounceTimer.Start();
			}
		};

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
				Errors = result.Diagnostics.Count > 0
					? result.Diagnostics
					: result.Errors.Select(e => new CompilationDiagnostic
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

		var deadClients = new List<WebSocket>();

		foreach (var ws in clients)
		{
			if (ws.State != WebSocketState.Open)
			{
				deadClients.Add(ws);
				continue;
			}
			try { await ws.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None); }
			catch (WebSocketException) { deadClients.Add(ws); }
		}

		if (deadClients.Count > 0)
		{
			lock (_clientsLock)
				foreach (var ws in deadClients)
					_clients.Remove(ws);
		}
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
			// Use M (medium) error correction so partial obstructions / camera glare still scan.
			var qrData = qrGenerator.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M);

			// Generate a real PNG with guaranteed-square modules and write it
			// to a temp file. Terminal-rendered ASCII QRs depend on the
			// terminal's character aspect ratio (which varies by font and
			// terminal app) — modules can stretch and become unscannable.
			// A PNG sidesteps the problem entirely.
			var pngBytes = new QRCoder.PngByteQRCode(qrData).GetGraphic(20);
			var pngPath = Path.Combine(Path.GetTempPath(), "comet-go-qr.png");
			File.WriteAllBytes(pngPath, pngBytes);

			Console.WriteLine($"  QR code:  {pngPath}");
			Console.WriteLine();

			// Try to auto-open the PNG with the platform image viewer so the
			// user can scan from screen. If it fails, no harm — they can open
			// the file manually using the path above, or fall back to the
			// ASCII QR below.
			TryOpenInImageViewer(pngPath);

			// Also print an ASCII QR as a fallback (for headless/SSH where the
			// PNG can't be auto-opened). repeatPerModule=2 makes each module
			// 4 chars wide × 2 chars tall — closer to square than 2×1 in
			// most terminal fonts. The tight 1× rendering looks nicer but
			// fails to scan in many terminals (e.g. VS Code's default font).
			var qrCode = new QRCoder.AsciiQRCode(qrData);
			var qrString = qrCode.GetGraphic(2, drawQuietZones: true);
			Console.WriteLine(qrString);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"  (QR generation failed — {ex.Message})");
			Console.WriteLine($"  Connect manually: {url}");
		}
	}

	static void TryOpenInImageViewer(string path)
	{
		try
		{
			string fileName;
			string args;
			if (OperatingSystem.IsMacOS())
			{
				fileName = "open"; args = $"\"{path}\"";
			}
			else if (OperatingSystem.IsWindows())
			{
				fileName = "cmd"; args = $"/c start \"\" \"{path}\"";
			}
			else if (OperatingSystem.IsLinux())
			{
				fileName = "xdg-open"; args = $"\"{path}\"";
			}
			else { return; }

			var psi = new System.Diagnostics.ProcessStartInfo(fileName, args)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			using var _ = System.Diagnostics.Process.Start(psi);
		}
		catch
		{
			// Best effort — silently swallow if open command fails.
		}
	}
}
