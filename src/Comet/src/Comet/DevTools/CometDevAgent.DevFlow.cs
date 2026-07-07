#nullable enable
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Comet.Backend;

namespace Comet.DevTools
{
	/// <summary>
	/// DevFlow/ailoha CLI wire-compatible surface for <see cref="CometDevAgent"/>. Serves the
	/// subset of <c>/api/v1/*</c> routes the <c>maui devflow ui</c> / <c>ailoha</c> CLI calls,
	/// translating Comet's own <see cref="ICometBackendNode"/>/<see cref="View"/> tree into the
	/// protocol's <c>ElementInfo</c> model and mapping element-id actions onto the same event
	/// sink a native interaction uses. On the iOS simulator the CLI connects straight to
	/// <c>localhost:9223</c> (shared loopback, no port-forward), so running the agent on that
	/// port makes a standalone Comet app drivable by the stock CLI — no MAUI host required.
	/// </summary>
	public sealed partial class CometDevAgent
	{
		/// <summary>The port the DevFlow CLI defaults to (AgentClient host=localhost port=9223).</summary>
		public const int DevFlowPort = 9223;

		string RouteDevFlow(string method, string rawPath, string body)
		{
			// Split path and query (the CLI resolves selectors via ?type=&text=&automationId=).
			string path = rawPath;
			string query = "";
			var qi = rawPath.IndexOf('?');
			if (qi >= 0) { path = rawPath.Substring(0, qi); query = rawPath.Substring(qi + 1); }

			switch (method, path)
			{
				case ("GET", "/api/v1/agent/status"):
					return RunOnMain(StatusJson);
				case ("GET", "/api/v1/agent/capabilities"):
					return CapabilitiesJson();
				case ("GET", "/api/v1/ui/tree"):
					return RunOnMain(DevFlowTreeJson);
				case ("GET", "/api/v1/ui/elements"):
					return RunOnMain(() => ElementsJson(query));

				case ("POST", "/api/v1/ui/actions/tap"):
					return RunOnMain(() =>
					{
						var view = ResolveElement(body);
						// Bubble to the nearest tap-bearing ancestor — the semantic counterpart
						// of a hit-test targeting the gesture's owner (a Text inside a tappable
						// card). Without this, taps on leaf elements silently no-op (the iOS
						// drawer-row bug; Android smoke masked it with coordinate taps).
						var target = view;
						for (int depth = 0; target is not null && !HasTap(target) && depth < 32; depth++)
							target = target.Parent as View;
						target ??= view;
						target.OnBackendEvent(EventIds.Clicked);
						target.OnBackendGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default));
						return ActionOk();
					});

				case ("POST", "/api/v1/ui/actions/fill"):
					return RunOnMain(() =>
					{
						var view = ResolveElement(body);
						view.OnBackendEvent(EventIds.TextChanged, GetString(body, "text") ?? "");
						return ActionOk();
					});

				case ("POST", "/api/v1/ui/actions/clear"):
					return RunOnMain(() =>
					{
						ResolveElement(body).OnBackendEvent(EventIds.TextChanged, "");
						return ActionOk();
					});

				case ("POST", "/api/v1/ui/actions/focus"):
					// Fire the element's Focused event — the same endpoint the native @FocusState
					// callback routes to (shim onFocused -> sink OnEvent(Focused) -> View.OnBackendEvent),
					// so focus-reactive UI runs exactly as for a real focus (e.g. focusing the input
					// dismisses the active selector panel). Mirrors tap/fill driving Comet backend events.
					return RunOnMain(() =>
					{
						ResolveElement(body).OnBackendEvent(EventIds.Focused);
						return ActionOk();
					});

				case ("POST", "/api/v1/ui/actions/scroll"):
					// Drive the underlying native scroll view so the shim's scroll-offset detection
					// (GeometryReader/onScroll on iOS) fires exactly as it does for a finger — which is
					// what reactive scroll-driven UI (JumpToBottom show/hide, profile FAB contract) needs.
					return RunOnMain(() => ScrollAction(body));

				case ("POST", "/api/v1/ui/actions/back"):
					return RunOnMain(() =>
					{
						PopNavigation();
						return ActionOk();
					});

				case ("POST", "/api/v1/ui/actions/drag"):
					// Real input injection (not a semantic event): body {x1,y1,x2,y2,durationMs?}
					// in physical px. One generic verb covers pull-to-refresh, pager swipes,
					// swipe-to-dismiss, drawer drags, and flings (velocity falls out of duration).
					// NOT RunOnMain — the injector blocks this worker thread while it marshals the
					// individual motion events to the UI thread over the gesture's duration.
					return DragAction(body);

				default:
					// 200 + success:false keeps the CLI from hanging on unimplemented routes.
					return "{\"success\":false,\"error\":\"unimplemented\"}";
			}
		}

		static string DragAction(string body)
		{
			var inject = CometDevRegistry.DragInjector;
			if (inject is null)
				return "{\"success\":false,\"error\":\"drag is not supported on this platform (no injector registered)\"}";

			float x1 = (float)GetDouble(body, "x1");
			float y1 = (float)GetDouble(body, "y1");
			float x2 = (float)GetDouble(body, "x2");
			float y2 = (float)GetDouble(body, "y2");
			int durationMs = 300;
			try { durationMs = (int)GetDouble(body, "durationMs"); } catch { /* optional */ }
			if (durationMs < 1) durationMs = 1;

			return inject(x1, y1, x2, y2, durationMs)
				? ActionOk()
				: "{\"success\":false,\"error\":\"drag injection failed\"}";
		}

		static View ResolveElement(string body)
		{
			var idStr = GetString(body, "elementId");
			if (idStr is null || !int.TryParse(idStr, out var id))
				throw new System.InvalidOperationException("elementId is required");
			return Resolve(id);
		}

		static bool HasTap(View view)
		{
			var gestures = view.Gestures;
			if (gestures is null)
				return false;
			for (int i = 0; i < gestures.Count; i++)
				if (gestures[i] is TapGesture)
					return true;
			return false;
		}

		static string ActionOk() => "{\"success\":true}";

		// Drives the frontmost scrollable native UIScrollView (the view backing the SwiftUI
		// ScrollView/List) by the requested delta. A real content-offset change makes SwiftUI
		// republish its scroll geometry, so the shim's onScroll preference fires and Comet's
		// AtTop/ScrolledAway signals update — the same path a finger drives. dy>0 scrolls toward the
		// end (content moves up). No-op off iOS (Compose/Android owns its own scrolling).
		static string ScrollAction(string body)
		{
#if IOS
			var sv = FindScrollableView();
			if (sv is not null)
			{
				double dy = TryGetDouble(body, "dy");
				double dx = TryGetDouble(body, "dx");
				var o = sv.ContentOffset;
				var inset = sv.AdjustedContentInset;
				double minY = -(double)inset.Top;
				double maxY = System.Math.Max(minY, (double)(sv.ContentSize.Height - sv.Bounds.Height + inset.Bottom));
				double ny = System.Math.Clamp((double)o.Y + dy, minY, maxY);
				sv.SetContentOffset(new CoreGraphics.CGPoint((double)o.X + dx, ny), animated: false);
				sv.LayoutIfNeeded();
			}
#endif
			return ActionOk();
		}

#if IOS
		static double TryGetDouble(string body, string key)
		{
			try { return GetDouble(body, key); } catch { return 0; }
		}

		// The largest visible UIScrollView in the key window whose content overflows its bounds (the
		// on-screen scroll). SwiftUI ScrollView and List both render through a UIScrollView, so this
		// resolves either of Comet's scroll/list nodes without an id mapping.
		static UIKit.UIScrollView? FindScrollableView()
		{
			UIKit.UIWindow? window = null;
			var windows = UIKit.UIApplication.SharedApplication.Windows;
			foreach (var w in windows)
				if (w.IsKeyWindow) { window = w; break; }
			if (window is null && windows.Length > 0) window = windows[0];
			if (window is null) return null;

			UIKit.UIScrollView? best = null;
			double bestArea = 0;
			var stack = new Stack<UIKit.UIView>();
			stack.Push(window);
			while (stack.Count > 0)
			{
				var v = stack.Pop();
				if (v is UIKit.UIScrollView sv && !sv.Hidden && sv.Alpha > 0.01
					&& sv.ContentSize.Height > sv.Bounds.Height + 1)
				{
					double area = (double)(sv.Bounds.Width * sv.Bounds.Height);
					if (area > bestArea) { bestArea = area; best = sv; }
				}
				foreach (var sub in v.Subviews) stack.Push(sub);
			}
			return best;
		}
#endif

		// Resolves the CLI's selector query (?type=&text=&automationId=) to matching elements.
		// Returns a flat List<ElementInfo> JSON array; the CLI takes the id(s) and acts by id.
		static string ElementsJson(string query)
		{
			var ps = ParseQuery(query);
			ps.TryGetValue("type", out var type);
			ps.TryGetValue("text", out var text);
			ps.TryGetValue("automationId", out var automationId);

			var nodes = CometDevRegistry.Snapshot();
			var byId = new Dictionary<int, CometDevRegistry.NodeInfo>();
			foreach (var n in nodes) byId[n.Id] = n;

			var matches = new List<CometDevRegistry.NodeInfo>();
			foreach (var n in nodes)
			{
				if (type is not null && !string.Equals(StripGeneric(n.Type), type, System.StringComparison.OrdinalIgnoreCase))
					continue;
				if (automationId is not null && !string.Equals(n.AutomationId, automationId, System.StringComparison.OrdinalIgnoreCase))
					continue;
				if (text is not null && !TextMatches(n.Text, text))
					continue;
				matches.Add(n);
			}

			var sb = new StringBuilder();
			sb.Append('[');
			var empty = new Dictionary<int, List<CometDevRegistry.NodeInfo>>();
			for (int i = 0; i < matches.Count; i++)
			{
				if (i > 0) sb.Append(',');
				WriteElement(sb, matches[i], empty); // flat: no children needed for resolution
			}
			sb.Append(']');
			return sb.ToString();
		}

		static bool TextMatches(string? actual, string wanted)
		{
			if (actual is null) return false;
			return string.Equals(actual, wanted, System.StringComparison.OrdinalIgnoreCase)
				|| actual.IndexOf(wanted, System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		static string StripGeneric(string type)
		{
			var tick = type.IndexOf('`');
			return tick >= 0 ? type.Substring(0, tick) : type;
		}

		static Dictionary<string, string> ParseQuery(string query)
		{
			var result = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
			foreach (var pair in query.Split('&'))
			{
				if (pair.Length == 0) continue;
				var eq = pair.IndexOf('=');
				if (eq < 0) continue;
				var key = System.Uri.UnescapeDataString(pair.Substring(0, eq));
				var val = System.Uri.UnescapeDataString(pair.Substring(eq + 1));
				result[key] = val;
			}
			return result;
		}

		static string StatusJson() =>
			"{\"timestamp\":\"" + System.DateTimeOffset.UtcNow.ToString("O") + "\"," +
			"\"running\":true," +
			"\"agent\":{\"name\":\"Comet.DevTools.CometDevAgent\",\"version\":\"1\"," +
			"\"framework\":\"comet\",\"frameworkVersion\":\"net11.0\"}," +
			"\"device\":{\"platform\":\"" + PlatformName() + "\",\"deviceType\":\"simulator\"," +
			"\"windowCount\":1}}";

		static string PlatformName()
		{
#if IOS
			return "iOS";
#elif ANDROID
			return "Android";
#else
			return "Unknown";
#endif
		}

		static string CapabilitiesJson() =>
			"{\"agent\":{\"name\":\"Comet.DevTools.CometDevAgent\",\"version\":\"1\",\"framework\":\"comet\"}," +
			"\"capabilities\":{" +
			"\"ui.tree\":{\"version\":1,\"features\":[\"type\",\"text\",\"accessibility-id\"]}," +
			"\"ui.actions\":{\"version\":1,\"features\":[\"tap\",\"fill\",\"clear\",\"back\",\"scroll\",\"focus\"" +
			(CometDevRegistry.DragInjector is not null ? ",\"drag\"" : "") + "]}}}";

		// Builds the nested ElementInfo tree the CLI expects, resolving the registry's flat
		// parentId list into a hierarchy. The CLI deserializes a List<ElementInfo>, and a Comet
		// app can track MULTIPLE parentless views (e.g. a Drawer plus content materialized
		// under a different owner), so EVERY root is emitted — dropping all but the first
		// hides the entire content tree.
		static string DevFlowTreeJson()
		{
			var nodes = CometDevRegistry.Snapshot();
			var byParent = new Dictionary<int, List<CometDevRegistry.NodeInfo>>();
			var roots = new List<CometDevRegistry.NodeInfo>();
			foreach (var n in nodes)
			{
				if (n.ParentId < 0 || !ExistsInSnapshot(nodes, n.ParentId))
					roots.Add(n);
				if (!byParent.TryGetValue(n.ParentId, out var list))
					byParent[n.ParentId] = list = new();
				list.Add(n);
			}

			var sb = new StringBuilder();
			sb.Append('[');
			for (int i = 0; i < roots.Count; i++)
			{
				if (i > 0) sb.Append(',');
				WriteElement(sb, roots[i], byParent);
			}
			sb.Append(']');
			return sb.ToString();
		}

		// A node whose recorded parent has been unregistered (its subtree owner was replaced)
		// is still live UI — treat it as a root rather than orphaning it out of the response.
		static bool ExistsInSnapshot(List<CometDevRegistry.NodeInfo> nodes, int id)
		{
			foreach (var n in nodes)
				if (n.Id == id)
					return true;
			return false;
		}

		static void WriteElement(StringBuilder sb, CometDevRegistry.NodeInfo n,
			Dictionary<int, List<CometDevRegistry.NodeInfo>> byParent)
		{
			bool tappable = n.Props.ContainsKey("tappable");
			string role = n.Type == "Button" || tappable ? "button"
				: n.Type == "TextField" ? "textbox"
				: n.Type == "Toggle" ? "checkbox"
				: n.Type == "ListView`1" || n.Type == "ListView" ? "list"
				: n.Type == "Text" ? "text"
				: "none";

			sb.Append('{');
			sb.Append("\"id\":").Append(JsonEncode(n.Id.ToString()));
			sb.Append(",\"parentId\":").Append(n.ParentId < 0 ? "null" : JsonEncode(n.ParentId.ToString()));
			sb.Append(",\"type\":").Append(JsonEncode(n.Type));
			sb.Append(",\"fullType\":").Append(JsonEncode("Comet." + n.Type));
			sb.Append(",\"framework\":\"comet\"");
			sb.Append(",\"role\":").Append(JsonEncode(role));
			if (n.AutomationId is not null) sb.Append(",\"automationId\":").Append(JsonEncode(n.AutomationId));
			if (n.Text is not null) sb.Append(",\"text\":").Append(JsonEncode(n.Text));
			if (n.Value is not null) sb.Append(",\"value\":").Append(JsonEncode(n.Value));
			sb.Append(",\"isVisible\":true");
			sb.Append(",\"isEnabled\":").Append(n.Enabled ? "true" : "false");
			if (tappable) sb.Append(",\"gestures\":[\"tap\"]");

			sb.Append(",\"children\":[");
			if (byParent.TryGetValue(n.Id, out var kids))
			{
				for (int i = 0; i < kids.Count; i++)
				{
					if (i > 0) sb.Append(',');
					WriteElement(sb, kids[i], byParent);
				}
			}
			sb.Append("]}");
		}

		// Finds a NavigationView among tracked views and pops it (for `ui actions back`).
		static void PopNavigation()
		{
			foreach (var n in CometDevRegistry.Snapshot())
			{
				if (n.Type == "NavigationView" && CometDevRegistry.Find(n.Id) is NavigationView nav)
				{
					nav.Pop();
					return;
				}
			}
		}
	}
}
