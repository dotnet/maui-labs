#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Comet.Backend;

namespace Comet.DevTools
{
	/// <summary>
	/// A tiny in-process HTTP agent that lets external tooling (the DevFlow/ailoha CLI, an AI
	/// agent, or plain <c>curl</c>) inspect the live Comet UI tree and drive semantic actions —
	/// the same architecture ailoha uses for every framework, applied to Comet's own
	/// <see cref="ICometBackendNode"/> tree so it works identically on the SwiftUI and Compose
	/// backends.
	/// </summary>
	/// <remarks>
	/// <para>Actions are dispatched <em>semantically</em>: a tap resolves an element id to its
	/// owning <see cref="View"/> and raises the same event the native control raises through
	/// <c>ViewEventSink</c>. No screen geometry or HID injection is needed, so verification is
	/// deterministic and framework-independent.</para>
	/// <para>Built on <see cref="TcpListener"/> (not <c>HttpListener</c>, which is unavailable
	/// on the iOS CoreCLR runtime) with a hand-rolled HTTP/1.1 read. The host supplies a
	/// main-thread dispatcher because reactive writes must run on the UI thread.</para>
	/// <para>Endpoints: <c>GET /status</c>, <c>GET /tree</c>, <c>POST /tap</c>,
	/// <c>POST /fill</c>, <c>POST /toggle</c>, <c>POST /slider</c> — each action body is
	/// <c>{ "id": N, ... }</c> and the response echoes the post-action tree.</para>
	/// </remarks>
	public sealed partial class CometDevAgent
	{
		readonly int _port;
		readonly Action<Action> _dispatchToMain;
		TcpListener? _listener;
		volatile bool _running;

		public CometDevAgent(int port, Action<Action> dispatchToMain)
		{
			_port = port;
			_dispatchToMain = dispatchToMain ?? throw new ArgumentNullException(nameof(dispatchToMain));
		}

		/// <summary>The port the listener actually bound (may differ from the requested
		/// port when it was already taken — see <see cref="Start"/>).</summary>
		public int Port { get; private set; }

		/// <summary>Enables tracking and starts the listener on a background thread.
		/// A dev-tool port collision (another agent/app on the requested port — the sim
		/// shares the host's loopback) must not crash the app: scan forward up to 10
		/// ports and report the one that bound.</summary>
		public void Start()
		{
			CometDevRegistry.Enabled = true;
			SocketException? lastError = null;
			for (int candidate = _port; candidate < _port + 10; candidate++)
			{
				try
				{
					var listener = new TcpListener(IPAddress.Loopback, candidate);
					listener.Start();
					_listener = listener;
					Port = candidate;
					break;
				}
				catch (SocketException ex)
				{
					lastError = ex;
				}
			}
			if (_listener is null)
			{
				System.Diagnostics.Debug.WriteLine($"[CometDevAgent] no free port in {_port}..{_port + 9}: {lastError?.Message}");
				return; // dev agent unavailable; the app runs fine without it
			}
			if (Port != _port)
				Console.WriteLine($"[CometDevAgent] port {_port} in use; listening on {Port}");
			_running = true;
			var thread = new Thread(AcceptLoop) { IsBackground = true, Name = "CometDevAgent" };
			thread.Start();
		}

		public void Stop()
		{
			_running = false;
			try { _listener?.Stop(); } catch { }
		}

		void AcceptLoop()
		{
			while (_running)
			{
				TcpClient client;
				try { client = _listener!.AcceptTcpClient(); }
				catch { if (_running) continue; else break; }

				try { HandleClient(client); }
				catch { /* never let one bad request kill the loop */ }
				finally { try { client.Close(); } catch { } }
			}
		}

		void HandleClient(TcpClient client)
		{
			using var stream = client.GetStream();
			var (method, path, body) = ReadRequest(stream);
			if (method is null)
				return;

			// Screenshot returns binary PNG, not JSON.
			var bare = path;
			var qi = bare.IndexOf('?');
			if (qi >= 0) bare = bare.Substring(0, qi);
			if (method == "GET" && (bare == "/api/v1/ui/screenshot" || bare == "/screenshot"))
			{
				var png = RunOnMainBytes(() => CometDevRegistry.ScreenshotProvider?.Invoke());
				if (png is { Length: > 0 })
					WriteBinaryResponse(stream, png, "image/png");
				else
					WriteResponse(stream, 503, "{\"ok\":false,\"error\":\"no screenshot provider\"}");
				return;
			}

			string json;
			int status = 200;
			try
			{
				json = Route(method, path, body);
			}
			catch (Exception ex)
			{
				status = 400;
				json = $"{{\"ok\":false,\"error\":{JsonEncode(ex.Message)}}}";
			}

			WriteResponse(stream, status, json);
		}

		byte[]? RunOnMainBytes(Func<byte[]?> work)
		{
			var tcs = new TaskCompletionSource<byte[]?>();
			_dispatchToMain(() =>
			{
				try { tcs.SetResult(work()); }
				catch { tcs.SetResult(null); }
			});
			try { return tcs.Task.GetAwaiter().GetResult(); }
			catch { return null; }
		}

		static void WriteBinaryResponse(NetworkStream stream, byte[] payload, string contentType)
		{
			var head = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\n" +
				$"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
			var headBytes = Encoding.ASCII.GetBytes(head);
			stream.Write(headBytes, 0, headBytes.Length);
			stream.Write(payload, 0, payload.Length);
			stream.Flush();
		}

		string Route(string method, string path, string body)
		{
			// DevFlow/ailoha CLI wire-compatible surface (so `maui devflow ui …` drives Comet).
			// Passed the raw path so the handler can read query params (e.g. ?text=&type=).
			var rawPath = path;

			// Strip query string for the simple-route switch below.
			var q = path.IndexOf('?');
			if (q >= 0) path = path.Substring(0, q);

			if (path.StartsWith("/api/v1/", System.StringComparison.Ordinal))
				return RouteDevFlow(method, rawPath, body);

			switch (method, path)
			{
				case ("GET", "/status"):
					return RunOnMain(() => $"{{\"ok\":true,\"framework\":\"comet\",\"nodes\":{CometDevRegistry.Snapshot().Count}}}");

				case ("GET", "/"):
				case ("GET", "/tree"):
					return RunOnMain(() => TreeJson());

				case ("POST", "/tap"):
					return RunOnMain(() =>
					{
						var id = GetInt(body, "id");
						var view = Resolve(id);
						view.OnBackendEvent(EventIds.Clicked);
						view.OnBackendGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default));
						return Ok(id, "tap");
					});

				case ("POST", "/fill"):
					return RunOnMain(() =>
					{
						var id = GetInt(body, "id");
						var text = GetString(body, "text") ?? "";
						Resolve(id).OnBackendEvent(EventIds.TextChanged, text);
						return Ok(id, "fill");
					});

				case ("POST", "/toggle"):
					return RunOnMain(() =>
					{
						var id = GetInt(body, "id");
						var value = GetBool(body, "value");
						Resolve(id).OnBackendEvent(EventIds.Toggled, value);
						return Ok(id, "toggle");
					});

				case ("POST", "/slider"):
					return RunOnMain(() =>
					{
						var id = GetInt(body, "id");
						var value = GetDouble(body, "value");
						Resolve(id).OnBackendEvent(EventIds.ValueChanged, value);
						return Ok(id, "slider");
					});

				default:
					return "{\"ok\":false,\"error\":\"unknown route\"}";
			}
		}

		static View Resolve(int id) =>
			CometDevRegistry.Find(id) ?? throw new InvalidOperationException($"no element with id {id}");

		static string Ok(int id, string action) =>
			$"{{\"ok\":true,\"action\":{JsonEncode(action)},\"id\":{id},\"tree\":{TreeArray()}}}";

		static string TreeJson() => $"{{\"ok\":true,\"framework\":\"comet\",\"nodes\":{TreeArray()}}}";

		static string TreeArray()
		{
			var nodes = CometDevRegistry.Snapshot();
			var sb = new StringBuilder();
			sb.Append('[');
			for (int i = 0; i < nodes.Count; i++)
			{
				var n = nodes[i];
				if (i > 0) sb.Append(',');
				sb.Append('{');
				sb.Append("\"id\":").Append(n.Id);
				sb.Append(",\"parentId\":").Append(n.ParentId);
				sb.Append(",\"type\":").Append(JsonEncode(n.Type));
				sb.Append(",\"enabled\":").Append(n.Enabled ? "true" : "false");
				if (n.AutomationId is not null) sb.Append(",\"automationId\":").Append(JsonEncode(n.AutomationId));
				if (n.Text is not null) sb.Append(",\"text\":").Append(JsonEncode(n.Text));
				if (n.Value is not null) sb.Append(",\"value\":").Append(JsonEncode(n.Value));
				sb.Append(",\"props\":{");
				bool first = true;
				foreach (var (k, v) in n.Props)
				{
					if (!first) sb.Append(',');
					first = false;
					sb.Append(JsonEncode(k)).Append(':').Append(JsonEncode(v));
				}
				sb.Append("}}");
			}
			sb.Append(']');
			return sb.ToString();
		}

		// --- main-thread marshaling ---

		string RunOnMain(Func<string> work)
		{
			var tcs = new TaskCompletionSource<string>();
			_dispatchToMain(() =>
			{
				try { tcs.SetResult(work()); }
				catch (Exception ex) { tcs.SetException(ex); }
			});
			try { return tcs.Task.GetAwaiter().GetResult(); }
			catch (Exception ex) { return $"{{\"ok\":false,\"error\":{JsonEncode(ex.Message)}}}"; }
		}

		// --- minimal HTTP + JSON helpers ---

		static (string? method, string path, string body) ReadRequest(NetworkStream stream)
		{
			var headerBytes = new List<byte>(512);
			int prev = -1, prev2 = -1, prev3 = -1;
			int b;
			// Read until CRLFCRLF (end of headers).
			while ((b = stream.ReadByte()) != -1)
			{
				headerBytes.Add((byte)b);
				if (prev3 == '\r' && prev2 == '\n' && prev == '\r' && b == '\n')
					break;
				prev3 = prev2; prev2 = prev; prev = b;
			}
			if (headerBytes.Count == 0)
				return (null, "", "");

			var header = Encoding.ASCII.GetString(headerBytes.ToArray());
			var firstLine = header.Substring(0, header.IndexOf('\r'));
			var parts = firstLine.Split(' ');
			if (parts.Length < 2)
				return (null, "", "");
			var method = parts[0];
			var path = parts[1];

			int contentLength = 0;
			foreach (var line in header.Split(new[] { "\r\n" }, StringSplitOptions.None))
			{
				if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
					int.TryParse(line.Substring("Content-Length:".Length).Trim(), out contentLength);
			}

			string body = "";
			if (contentLength > 0)
			{
				var buf = new byte[contentLength];
				int read = 0;
				while (read < contentLength)
				{
					int r = stream.Read(buf, read, contentLength - read);
					if (r <= 0) break;
					read += r;
				}
				body = Encoding.UTF8.GetString(buf, 0, read);
			}

			return (method, path, body);
		}

		static void WriteResponse(NetworkStream stream, int status, string json)
		{
			var payload = Encoding.UTF8.GetBytes(json);
			var reason = status == 200 ? "OK" : "Bad Request";
			var head = $"HTTP/1.1 {status} {reason}\r\n" +
				"Content-Type: application/json\r\n" +
				$"Content-Length: {payload.Length}\r\n" +
				"Connection: close\r\n\r\n";
			var headBytes = Encoding.ASCII.GetBytes(head);
			stream.Write(headBytes, 0, headBytes.Length);
			stream.Write(payload, 0, payload.Length);
			stream.Flush();
		}

		static int GetInt(string body, string key)
		{
			using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
			if (doc.RootElement.TryGetProperty(key, out var el) && el.TryGetInt32(out var v))
				return v;
			throw new InvalidOperationException($"missing integer '{key}'");
		}

		static string? GetString(string body, string key)
		{
			using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
			return doc.RootElement.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
				? el.GetString() : null;
		}

		static bool GetBool(string body, string key)
		{
			using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
			return doc.RootElement.TryGetProperty(key, out var el) &&
				(el.ValueKind == JsonValueKind.True ||
				 (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b) && b));
		}

		static double GetDouble(string body, string key)
		{
			using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
			if (doc.RootElement.TryGetProperty(key, out var el) && el.TryGetDouble(out var v))
				return v;
			throw new InvalidOperationException($"missing number '{key}'");
		}

		static string JsonEncode(string s)
		{
			var sb = new StringBuilder(s.Length + 2);
			sb.Append('"');
			foreach (var c in s)
			{
				switch (c)
				{
					case '"': sb.Append("\\\""); break;
					case '\\': sb.Append("\\\\"); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					default:
						if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
						else sb.Append(c);
						break;
				}
			}
			sb.Append('"');
			return sb.ToString();
		}
	}
}
