using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent.Core;
#endif
#pragma warning disable CA1416

namespace CometComposeProbe
{
#if DEBUG
	/// <summary>
	/// Bootstraps the DevFlow in-app agent for CometComposeProbe without requiring UseMaui.
	/// Uses DevFlowAgentService.StartServerOnly (designed for Comet apps where
	/// Application.Current is unavailable) with an Android-native PixelCopy screenshot.
	/// </summary>
	static class DevFlowHelper
	{
		static ComposeProbeAgentService? _agent;

		public static void Start(Activity activity, View rootView)
		{
			var dispatcher = new ActivityDispatcher();

			_ = Task.Run(async () =>
			{
				try
				{
					var broker = new BrokerRegistration(
						project: "CometComposeProbe",
						tfm: "net11.0-android",
						platform: "Android",
						appName: "CometComposeProbe");

					var assignedPort = await broker.TryRegisterAsync(TimeSpan.FromSeconds(5));
					// The broker allocates host-global ports but multiple Comet samples may
					// be running side-by-side on the same device, each holding device-local
					// ports. Probe availability before constructing the agent (port is baked
					// into AgentHttpServer at construction time).
					var candidatePort = assignedPort ?? AgentOptions.DefaultPort;
					var resolvedPort = FindFreeLocalPort(candidatePort, maxScan: 20);
					var options = new AgentOptions
					{
						Port = resolvedPort,
						RequireMutationLease = false,
					};

					var agent = new ComposeProbeAgentService(activity, options);
					agent.SetBrokerRegistration(broker);
					broker.CurrentPort = resolvedPort;
					_agent = agent;

					dispatcher.Dispatch(() => agent.StartServerOnly(dispatcher));
					Console.WriteLine($"[CometComposeProbe] DevFlow agent started on port {resolvedPort}");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[CometComposeProbe] DevFlow startup failed: {ex.Message}");
				}
			});
		}

		/// <summary>
		/// Probes device-local port availability starting from <paramref name="start"/>,
		/// scanning up to <paramref name="maxScan"/> ports. Returns the first port that
		/// can be bound (immediately released). Falls back to <paramref name="start"/>
		/// if no free port is found (the server will log the bind failure).
		/// </summary>
		static int FindFreeLocalPort(int start, int maxScan)
		{
			for (int candidate = start; candidate < start + maxScan; candidate++)
			{
				try
				{
					using var probe = new System.Net.Sockets.TcpListener(
						System.Net.IPAddress.Loopback, candidate);
					probe.Start();
					probe.Stop();
					if (candidate != start)
						Console.WriteLine($"[CometComposeProbe] Broker-assigned port {start} in use; resolved to {candidate}");
					return candidate;
				}
				catch (System.Net.Sockets.SocketException)
				{
					// Port in use — try next.
				}
			}
			Console.WriteLine($"[CometComposeProbe] No free port in {start}..{start + maxScan - 1}; falling back to {start}");
			return start;
		}
	}

	/// <summary>
	/// DevFlowAgentService subclass for CometComposeProbe.
	/// Provides Android PixelCopy screenshot (captures GPU-rendered Compose content)
	/// and bridges the Comet visual tree (CometDevRegistry) to the DevFlow tree protocol.
	/// </summary>
	sealed class ComposeProbeAgentService : DevFlowAgentService
	{
		readonly Activity _activity;

		public ComposeProbeAgentService(Activity activity, AgentOptions? options = null)
			: base(options)
		{
			_activity = activity;
		}

		protected override async Task<HttpResponse> HandleTree(HttpRequest request)
		{
			int maxDepth = 0;
			if (request.QueryParams.TryGetValue("depth", out var depthStr))
				int.TryParse(depthStr, out maxDepth);

			// Snapshot on the UI thread so View.Frame (Yoga-arranged bounds) and
			// ApplyAllSetProperties read consistent state.
			var nodes = await DispatchAsync(() => Comet.DevTools.CometDevRegistry.Snapshot());
			var elements = CometRegistryToElementInfo(nodes, maxDepth);
			return HttpResponse.Json(elements);
		}

		protected override async Task<HttpResponse> HandleQuery(HttpRequest request)
		{
			request.QueryParams.TryGetValue("type", out var type);
			request.QueryParams.TryGetValue("text", out var text);
			request.QueryParams.TryGetValue("automationId", out var automationId);

			var nodes = await DispatchAsync(() => Comet.DevTools.CometDevRegistry.Snapshot());
			var flat = new List<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo>();
			foreach (var n in nodes)
			{
				if (type is not null && !string.Equals(StripGeneric(n.Type), type, StringComparison.OrdinalIgnoreCase))
					continue;
				if (automationId is not null && !string.Equals(n.AutomationId, automationId, StringComparison.OrdinalIgnoreCase))
					continue;
				if (text is not null && (n.Text is null || n.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0))
					continue;
				flat.Add(ToElementInfo(n));
			}
			return HttpResponse.Json(flat);
		}

		protected override async Task<HttpResponse> HandleElement(HttpRequest request)
		{
			if (!request.RouteParams.TryGetValue("id", out var id))
				return HttpResponse.Error("Element ID required");

			if (int.TryParse(id, out var numId))
			{
				var nodes = await DispatchAsync(() => Comet.DevTools.CometDevRegistry.Snapshot());
				foreach (var n in nodes)
				{
					if (n.Id == numId)
						return HttpResponse.Json(ToElementInfo(n));
				}
			}
			return HttpResponse.Error($"Element {id} not found", 404);
		}

		protected override async Task<HttpResponse> HandleTap(HttpRequest request)
		{
			var body = request.Body ?? "{}";
			var elementId = ReadJsonString(body, "elementId")
				?? ReadJsonString(body, "id");
			var automationId = ReadJsonString(body, "automationId");

			int? numId = null;
			if (elementId is not null && int.TryParse(elementId, out var parsed))
				numId = parsed;

			// Resolve by automationId if no numeric id
			if (numId is null && automationId is not null)
			{
				var nodes = await DispatchAsync(() => Comet.DevTools.CometDevRegistry.Snapshot());
				foreach (var n in nodes)
					if (string.Equals(n.AutomationId, automationId, StringComparison.OrdinalIgnoreCase))
					{ numId = n.Id; break; }
			}
			if (numId is null)
				return HttpResponse.Error("elementId or automationId required");

			// Route through the CometDevAgent's DevFlow tap path which handles
			// gesture bubbling and event dispatch.
			await DispatchAsync(() =>
			{
				Comet.DevTools.CometDevAgent.DispatchTap(numId.Value);
				return true;
			});
			return HttpResponse.Json(new { success = true, action = "tap", elementId = numId.Value.ToString() });
		}

		protected override async Task<HttpResponse> HandleSetProperty(HttpRequest request)
		{
			if (!request.RouteParams.TryGetValue("id", out var id) ||
				!int.TryParse(id, out var numId))
				return HttpResponse.Error("Element ID required");
			if (!request.RouteParams.TryGetValue("name", out var propertyName))
				return HttpResponse.Error("Property name required");

			var body = request.BodyAs<SetPropertyRequest>();
			if (body?.Value is null)
				return HttpResponse.Error("value is required");

			var result = await DispatchAsync(() =>
			{
				var view = Comet.DevTools.CometDevRegistry.Find(numId);
				return view is null
					? null
					: Comet.DevTools.CometDevAgent.DispatchSetProperty(
						view,
						propertyName,
						body.Value);
			});
			if (result is null)
				return HttpResponse.Error($"Element {id} not found", 404);

			return new HttpResponse { Body = result };
		}

		static string? ReadJsonString(string? body, string key)
		{
			if (string.IsNullOrWhiteSpace(body)) return null;
			try
			{
				using var doc = System.Text.Json.JsonDocument.Parse(body);
				return doc.RootElement.TryGetProperty(key, out var el) &&
					el.ValueKind == System.Text.Json.JsonValueKind.String
					? el.GetString() : null;
			}
			catch { return null; }
		}

		static string StripGeneric(string type)
		{
			var tick = type.IndexOf('`');
			return tick >= 0 ? type.Substring(0, tick) : type;
		}

		static List<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo> CometRegistryToElementInfo(
			List<Comet.DevTools.CometDevRegistry.NodeInfo> nodes, int maxDepth)
		{
			var byParent = new Dictionary<int, List<Comet.DevTools.CometDevRegistry.NodeInfo>>();
			var roots = new List<Comet.DevTools.CometDevRegistry.NodeInfo>();
			var nodeIds = new HashSet<int>();
			foreach (var n in nodes) nodeIds.Add(n.Id);

			foreach (var n in nodes)
			{
				if (n.ParentId < 0 || !nodeIds.Contains(n.ParentId))
					roots.Add(n);
				if (!byParent.TryGetValue(n.ParentId, out var list))
					byParent[n.ParentId] = list = new();
				list.Add(n);
			}

			var result = new List<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo>();
			foreach (var root in roots)
				result.Add(BuildTree(root, byParent, maxDepth, 0));
			return result;
		}

		static Microsoft.Maui.DevFlow.Agent.Core.ElementInfo BuildTree(
			Comet.DevTools.CometDevRegistry.NodeInfo n,
			Dictionary<int, List<Comet.DevTools.CometDevRegistry.NodeInfo>> byParent,
			int maxDepth, int depth)
		{
			var el = ToElementInfo(n);
			if (maxDepth > 0 && depth >= maxDepth)
				return el;

			if (byParent.TryGetValue(n.Id, out var kids))
			{
				el.Children = new List<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo>(kids.Count);
				foreach (var kid in kids)
					el.Children.Add(BuildTree(kid, byParent, maxDepth, depth + 1));
			}
			return el;
		}

		static Microsoft.Maui.DevFlow.Agent.Core.ElementInfo ToElementInfo(
			Comet.DevTools.CometDevRegistry.NodeInfo n)
		{
			var view = Comet.DevTools.CometDevRegistry.Find(n.Id);
			var frame = view?.Frame ?? Microsoft.Maui.Graphics.Rect.Zero;

			bool tappable = n.Props.ContainsKey("tappable");

			var el = new Microsoft.Maui.DevFlow.Agent.Core.ElementInfo
			{
				Id = n.Id.ToString(),
				ParentId = n.ParentId >= 0 ? n.ParentId.ToString() : null,
				Type = n.Type,
				FullType = "Comet." + n.Type,
				Framework = "comet",
				AutomationId = n.AutomationId,
				Text = n.Text,
				Value = n.Value,
				IsVisible = true,
				IsEnabled = n.Enabled,
				Bounds = new Microsoft.Maui.DevFlow.Agent.Core.BoundsInfo
				{
					X = frame.X, Y = frame.Y, Width = frame.Width, Height = frame.Height
				},
				WindowBounds = new Microsoft.Maui.DevFlow.Agent.Core.BoundsInfo
				{
					X = frame.X, Y = frame.Y, Width = frame.Width, Height = frame.Height
				},
			};
			if (tappable)
				el.Gestures = new List<string> { "tap" };
			return el;
		}

		protected override async Task<HttpResponse> HandleScreenshot(HttpRequest request)
		{
			try
			{
				var pngData = await CaptureFullScreenAsync();
				if (pngData != null)
					return HttpResponse.Png(pngData);
				return HttpResponse.Error("PixelCopy returned null");
			}
			catch (Exception ex)
			{
				return HttpResponse.Error($"Screenshot failed: {ex.Message}");
			}
		}

		Task<byte[]?> CaptureFullScreenAsync()
		{
			if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
				return Comet.Platform.Compose.ComposeDevAgentHost.CaptureScreenshotAsync(_activity);

			var tcs = new TaskCompletionSource<byte[]?>();

			// Preserve the debug helper's pre-API-26 software capture path.
			_activity.RunOnUiThread(() =>
			{
				try
				{
					var win = _activity.Window;
					var decorView = win?.DecorView;
					if (win == null || decorView == null || decorView.Width <= 0 || decorView.Height <= 0)
					{
						tcs.SetResult(null);
						return;
					}

					var bmp = Bitmap.CreateBitmap(decorView.Width, decorView.Height, Bitmap.Config.Argb8888!)!;

					try
					{
						decorView.Draw(new Canvas(bmp));
						using var ms = new MemoryStream();
						bmp.Compress(Bitmap.CompressFormat.Png!, 90, ms);
						tcs.SetResult(ms.ToArray());
					}
					catch (Exception ex)
					{
						Android.Util.Log.Warn("CometProbe", $"Canvas fallback failed: {ex.Message}");
						tcs.SetResult(null);
					}
					finally { bmp.Recycle(); }
				}
				catch (Exception ex)
				{
					tcs.TrySetResult(null);
					Android.Util.Log.Warn("CometProbe", $"CaptureFullScreen failed: {ex.Message}");
				}
			});

			return tcs.Task;
		}
	}

	sealed class ActivityDispatcher : IAgentDispatcher
	{
		readonly Handler _handler = new(Looper.MainLooper!);

		public bool IsDispatchRequired => Looper.MyLooper() != Looper.MainLooper;
		public bool Dispatch(Action action) { _handler.Post(action); return true; }
		public bool DispatchDelayed(TimeSpan delay, Action action)
		{
			_handler.PostDelayed(action, (long)delay.TotalMilliseconds);
			return true;
		}
	}
#endif
}

#pragma warning restore CA1416
