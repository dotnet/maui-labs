#nullable enable
using System.Collections.Generic;
using System.Globalization;
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
		const int MaxDragDurationMs = 10_000;

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
				case ("GET", "/api/v1/ui/inspection"):
					return RunOnMain(InspectionJson);

				case ("POST", "/api/v1/ui/actions/tap"):
					return RunOnMain(() =>
					{
						var id = ResolveElementId(body);
						if (CometDevRegistry.TryInvokeSemanticAction(id))
							return ActionOk();
						var view = Resolve(id);
						// Tapping a text input focuses it (keyboard + first responder) —
						// the same behaviour a real finger tap produces in SwiftUI.
						if (view is TextField || view is TextEditor)
						{
							view.Node?.ApplyProperty(PropertyIds.TextField_FocusRequested, PropertyValue.From(true));
							return ActionOk();
						}
						var target = view;
						for (int depth = 0; target is not null && !HasTap(target) && depth < 32; depth++)
							target = target.Parent as View;
						target ??= view;
						target.OnBackendEvent(EventIds.Clicked);
						target.OnBackendGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default));
						return ActionOk();
					});

				case ("POST", "/api/v1/ui/actions/longpress"):
					return RunOnMain(() =>
					{
						// Long-press twin of tap: bubble to the nearest long-press-bearing
						// ancestor (a Text inside a selectable card) and fire the gesture.
						var view = ResolveElement(body);
						var target = view;
						for (int depth = 0; target is not null && !HasLongPress(target) && depth < 32; depth++)
							target = target.Parent as View;
						target ??= view;
						target.OnBackendGesture(GestureKind.LongPress, new GestureData(GestureState.Ended, default));
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
					// Drive the native @FocusState through the node protocol: set the
					// TextField_FocusRequested property so the shim sets fieldFocused = true,
					// which shows the keyboard and fires the onFocused callback. Falls back to
					// the semantic event for non-text controls.
					return RunOnMain(() =>
					{
						var view = ResolveElement(body);
						if (view is TextField || view is TextEditor)
							view.Node?.ApplyProperty(PropertyIds.TextField_FocusRequested, PropertyValue.From(true));
						else
							view.OnBackendEvent(EventIds.Focused);
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
						if (RequestBack())
							return ActionOk();
						return "{\"success\":false,\"error\":\"no NavigationView found in the view registry\"}";
					});

				case ("POST", "/api/v1/ui/actions/drag"):
					// Real input injection (not a semantic event): body {x1,y1,x2,y2,durationMs?}
					// in physical px. One generic verb covers pull-to-refresh, pager swipes,
					// swipe-to-dismiss, drawer drags, and flings (velocity falls out of duration).
					// NOT RunOnMain — the injector blocks this worker thread while it marshals the
					// individual motion events to the UI thread over the gesture's duration.
					return DragAction(body);

				case ("POST", "/api/v1/ui/actions/set-property"):
				case ("POST", "/api/v1/ui/actions/set_property"):
					return RunOnMain(() =>
					{
						var view = ResolveElement(body);
						var propName = GetString(body, "property") ?? GetString(body, "name") ?? "";
						var propValue = GetString(body, "value") ?? "";
						return DispatchSetProperty(view, propName, propValue);
					});

				default:
					if (method == "GET" && TryParsePropertyRoute(path, out var propertyElementId, out var propertyName))
						return RunOnMain(() => ElementPropertyJson(propertyElementId, propertyName));
					if (method == "GET" && TryParseElementRoute(path, out var elementId))
						return RunOnMain(() => ElementJson(elementId));
					// Canonical dynamic PUT: /api/v1/ui/elements/{id}/properties/{name}
					// (the route AgentClient.SetPropertyAsync sends). Parsed here because
					// the switch above handles only literal paths.
					if (method == "PUT" && TryParsePropertyRoute(path, out var elemId, out var propN))
					{
						return RunOnMain(() =>
						{
							var view = CometDevRegistry.Find(elemId)
								?? throw new System.InvalidOperationException($"no element with id {elemId}");
							var val = GetString(body, "value") ?? "";
							return DispatchSetProperty(view, propN, val);
						});
					}
					// 200 + success:false keeps the CLI from hanging on unimplemented routes.
					return "{\"success\":false,\"error\":\"unimplemented\"}";
			}
		}

		/// <summary>Parses <c>/api/v1/ui/elements/{id}/properties/{name}</c>.</summary>
		static bool TryParsePropertyRoute(string path, out int elementId, out string propertyName)
		{
			elementId = 0;
			propertyName = "";
			const string prefix = "/api/v1/ui/elements/";
			if (!path.StartsWith(prefix, System.StringComparison.Ordinal))
				return false;
			var rest = path.Substring(prefix.Length);
			var slash = rest.IndexOf('/');
			if (slash < 0) return false;
			if (!int.TryParse(rest.Substring(0, slash), out elementId))
				return false;
			const string mid = "/properties/";
			if (!rest.Substring(slash).StartsWith(mid, System.StringComparison.Ordinal))
				return false;
			propertyName = System.Uri.UnescapeDataString(rest.Substring(slash + mid.Length));
			return propertyName.Length > 0;
		}

		static bool TryParseElementRoute(string path, out int elementId)
		{
			elementId = 0;
			const string prefix = "/api/v1/ui/elements/";
			if (!path.StartsWith(prefix, System.StringComparison.Ordinal))
				return false;
			var rest = path.Substring(prefix.Length);
			return rest.Length > 0 && rest.IndexOf('/') < 0 && int.TryParse(rest, out elementId);
		}

		internal static string DragAction(string body)
		{
			var inject = CometDevRegistry.DragInjector;
			if (inject is null)
				return "{\"success\":false,\"error\":\"drag is not supported on this platform (no injector registered)\"}";

			var x1Value = GetDouble(body, "x1");
			var y1Value = GetDouble(body, "y1");
			var x2Value = GetDouble(body, "x2");
			var y2Value = GetDouble(body, "y2");
			if (!double.IsFinite(x1Value) || !double.IsFinite(y1Value) ||
				!double.IsFinite(x2Value) || !double.IsFinite(y2Value) ||
				x1Value < -float.MaxValue || x1Value > float.MaxValue ||
				y1Value < -float.MaxValue || y1Value > float.MaxValue ||
				x2Value < -float.MaxValue || x2Value > float.MaxValue ||
				y2Value < -float.MaxValue || y2Value > float.MaxValue)
			{
				return "{\"success\":false,\"error\":\"drag coordinates must be finite single-precision values\"}";
			}

			var durationValue = 300d;
			using (var document = JsonDocument.Parse(body))
			{
				if (document.RootElement.TryGetProperty("durationMs", out var durationElement) &&
					(durationElement.ValueKind != JsonValueKind.Number ||
						!durationElement.TryGetDouble(out durationValue)))
				{
					return $"{{\"success\":false,\"error\":\"durationMs must be an integer from 1 to {MaxDragDurationMs}\"}}";
				}
			}
			if (!double.IsFinite(durationValue) || durationValue != System.Math.Truncate(durationValue) ||
				durationValue < 1 || durationValue > MaxDragDurationMs)
			{
				return $"{{\"success\":false,\"error\":\"durationMs must be an integer from 1 to {MaxDragDurationMs}\"}}";
			}

			var x1 = (float)x1Value;
			var y1 = (float)y1Value;
			var x2 = (float)x2Value;
			var y2 = (float)y2Value;
			var durationMs = (int)durationValue;

			return inject(x1, y1, x2, y2, durationMs)
				? ActionOk()
				: "{\"success\":false,\"error\":\"drag injection failed\"}";
		}

		static View ResolveElement(string body)
			=> Resolve(ResolveElementId(body));

		static int ResolveElementId(string body)
		{
			var idStr = GetString(body, "elementId");
			if (idStr is null || !int.TryParse(idStr, out var id))
				throw new System.InvalidOperationException("elementId is required");
			return id;
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

		static bool HasLongPress(View view)
		{
			var gestures = view.Gestures;
			if (gestures is null)
				return false;
			for (int i = 0; i < gestures.Count; i++)
				if (gestures[i] is LongPressGesture)
					return true;
			return false;
		}

		/// <summary>Dispatches a semantic tap on the element with the given registry id.
		/// Bubbles to the nearest tap-bearing ancestor. Public so the Android probe's
		/// DevFlowAgentService subclass can route DevFlow tap requests through the same path.</summary>
		public static void DispatchTap(int elementId)
		{
			if (CometDevRegistry.TryInvokeSemanticAction(elementId))
				return;
			var view = Resolve(elementId);
			var target = view;
			for (int depth = 0; target is not null && !HasTap(target) && depth < 32; depth++)
				target = target.Parent as View;
			target ??= view;
			target.OnBackendEvent(EventIds.Clicked);
			target.OnBackendGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default));
		}

		static string ActionOk() => "{\"success\":true}";

		/// <summary>Dispatches a set-property request to the Comet backend event system.
		/// Maps known property names to the correct backend event:
		/// <c>isOn</c> → <see cref="EventIds.Toggled"/> (bool), <c>sliderValue</c>/<c>value</c>
		/// on a Slider → <see cref="EventIds.ValueChanged"/> (double), and
		/// <c>selectedIndex</c> on a TabView → native tab selection. Public static so the
		/// Android probe's DevFlowAgentService subclass can also call it.</summary>
		public static string DispatchSetProperty(View view, string propertyName, string value)
		{
			switch (propertyName.ToLowerInvariant())
			{
				case "ison" when view is Toggle:
					var boolVal = value == "1" || value.Equals("true", System.StringComparison.OrdinalIgnoreCase);
					view.OnBackendEvent(EventIds.Toggled, boolVal);
					return ActionOk();

				case "slidervalue" or "value" when view is Slider:
					if (double.TryParse(value, System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture, out var dblVal))
					{
						view.OnBackendEvent(EventIds.ValueChanged, dblVal);
						return ActionOk();
					}
					return "{\"success\":false,\"error\":\"invalid numeric value\"}";

				case "selectedindex" when view is TabView tabView:
					if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
						System.Globalization.CultureInfo.InvariantCulture, out var index) &&
						index >= 0 && index < tabView.Tabs.Count)
					{
						tabView.SelectItem(index);
						return ActionOk();
					}
					return "{\"success\":false,\"error\":\"selectedIndex must identify an existing tab\"}";

				default:
					return "{\"success\":false,\"error\":\"property not supported for this control\"}";
			}
		}

		// Drives the frontmost scrollable native UIScrollView (the view backing the SwiftUI
		// ScrollView/List) by the requested delta. The CLI contract (DevFlowCommands.cs:731) defines
		// negative dy = scroll down (toward end). Native scroll offsets (UIKit ContentOffset.Y,
		// Compose ScrollState.Value) INCREASE when scrolling down, so we negate the CLI delta:
		// `--dy -400` → native offset += 400.
		static string ScrollAction(string body)
		{
			double dy = 0, dx = 0;
			int elemId = -1;

			// AgentClient.ScrollAsync serializes "deltaX"/"deltaY" (canonical).
			// Accept both canonical names and short aliases for direct curl/CLI use.
			dy = TryGetDoubleAny(body, "deltaY", "dy");
			dx = TryGetDoubleAny(body, "deltaX", "dx");
			var elemIdStr = GetString(body, "elementId");
			if (elemIdStr is not null) int.TryParse(elemIdStr, out elemId);

			// Negate vertical only: CLI negative dy = down, native offset positive = down.
			// Horizontal dx is passed through unchanged (CLI and native agree on sign).
			double nativeDy = -dy;
			double nativeDx = dx;

			// Platform-agnostic: try the registered ScrollInjector (Compose ScrollState, etc.)
			if (CometDevRegistry.ScrollInjector is { } inject && elemId > 0)
			{
				if (inject(elemId, nativeDx, nativeDy))
					return ActionOk();
			}

#if IOS
			// If an element ID is given, find the nearest UIScrollView to that element's
			// position; otherwise fall back to the global largest-scrollable search.
			UIKit.UIScrollView? sv = null;
			if (elemId > 0)
			{
				var view = CometDevRegistry.Find(elemId);
				if (view is not null)
					sv = FindScrollViewForElement(view);
			}
			sv ??= FindScrollableView();

			if (sv is not null)
			{
				var o = sv.ContentOffset;
				var inset = sv.AdjustedContentInset;
				double minY = -(double)inset.Top;
				double maxY = System.Math.Max(minY, (double)(sv.ContentSize.Height - sv.Bounds.Height + inset.Bottom));
				double newY = System.Math.Clamp((double)o.Y + nativeDy, minY, maxY);
				if (System.Math.Abs(newY - (double)o.Y) < 0.5 && System.Math.Abs(nativeDx) < 0.5)
					return "{\"success\":false,\"error\":\"scroll view content does not overflow its bounds\"}";
				sv.SetContentOffset(new CoreGraphics.CGPoint((double)o.X + nativeDx, newY), animated: false);
				sv.LayoutIfNeeded();
				return ActionOk();
			}
			return "{\"success\":false,\"error\":\"no scrollable view found\"}";
#else
			return "{\"success\":false,\"error\":\"scroll not supported on this platform (no injector registered)\"}";
#endif
		}

		/// <summary>Reads a double from JSON body, trying the canonical name first then the alias.
		/// Returns 0 when neither key is present.</summary>
		static double TryGetDoubleAny(string body, string canonical, string alias)
		{
			using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
			if (doc.RootElement.TryGetProperty(canonical, out var el) && el.TryGetDouble(out var v))
				return v;
			if (doc.RootElement.TryGetProperty(alias, out el) && el.TryGetDouble(out v))
				return v;
			return 0;
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

		/// <summary>
		/// Finds the UIScrollView backing a specific Comet ScrollView element. The Comet
		/// ScrollView (or its ancestor) renders as a SwiftUI ScrollView which UIKit hosts as
		/// a UIScrollView. We walk the element's ancestors to find the Comet.ScrollView, then
		/// match by finding the UIScrollView whose frame overlaps the element's layout position.
		/// Unlike <see cref="FindScrollableView"/>, this does NOT require the content to
		/// overflow — the caller explicitly targeted this view.
		/// </summary>
		static UIKit.UIScrollView? FindScrollViewForElement(View view)
		{
			// Walk up to find the owning Comet.ScrollView (the element itself might be a child)
			View? current = view;
			Comet.ScrollView? scrollOwner = view as Comet.ScrollView;
			while (scrollOwner is null && current?.Parent is View parent)
			{
				current = parent;
				scrollOwner = current as Comet.ScrollView;
			}

			// Get all UIScrollViews in the window, without the content-overflow filter
			UIKit.UIWindow? window = null;
			var windows = UIKit.UIApplication.SharedApplication.Windows;
			foreach (var w in windows)
				if (w.IsKeyWindow) { window = w; break; }
			if (window is null && windows.Length > 0) window = windows[0];
			if (window is null) return null;

			var candidates = new List<UIKit.UIScrollView>();
			var stack = new Stack<UIKit.UIView>();
			stack.Push(window);
			while (stack.Count > 0)
			{
				var v = stack.Pop();
				if (v is UIKit.UIScrollView sv && !sv.Hidden && sv.Alpha > 0.01)
					candidates.Add(sv);
				foreach (var sub in v.Subviews) stack.Push(sub);
			}

			if (candidates.Count == 0) return null;
			if (candidates.Count == 1) return candidates[0];

			// Prefer the scroll view whose content overflows (the real page scroll, not a
			// navigation container). Fall back to the largest by area if none overflows.
			UIKit.UIScrollView? overflowing = null;
			double overflowingArea = 0;
			UIKit.UIScrollView? largest = null;
			double largestArea = 0;
			foreach (var sv in candidates)
			{
				double area = (double)(sv.Bounds.Width * sv.Bounds.Height);
				if (sv.ContentSize.Height > sv.Bounds.Height + 1)
				{
					if (area > overflowingArea) { overflowingArea = area; overflowing = sv; }
				}
				if (area > largestArea) { largestArea = area; largest = sv; }
			}
			return overflowing ?? largest;
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
			"\"ui.tree\":{\"version\":1,\"features\":[\"type\",\"text\",\"accessibility-id\",\"parent-relative-bounds\"]}," +
			"\"ui.inspection\":{\"version\":1,\"features\":[\"logical-window\",\"safe-area\",\"native-window\",\"parent-relative-frames\"]}," +
			"\"ui.actions\":{\"version\":1,\"features\":[\"tap\",\"fill\",\"clear\",\"back\",\"scroll\",\"focus\",\"set-property\"" +
			(CometDevRegistry.DragInjector is not null ? ",\"drag\"" : "") + "]}}}";

		static string InspectionJson()
		{
			var logicalSize = CometWindowMetrics.Shared.SizeDp.Peek();
			var safeArea = CometWindowMetrics.Shared.SafeAreaDp.Peek();
			var native = CometDevRegistry.NativeWindowMetricsProvider?.Invoke();
			var nodes = CometDevRegistry.Snapshot();
			var nodeIds = new HashSet<int>();
			foreach (var node in nodes)
				nodeIds.Add(node.Id);

			var sb = new StringBuilder();
			sb.Append("{\"success\":true,\"framework\":\"comet\"");
			sb.Append(",\"logicalWindow\":{");
			sb.Append("\"available\":").Append(
				logicalSize.Width > 0 && logicalSize.Height > 0 ? "true" : "false");
			sb.Append(",\"size\":");
			WriteSize(sb, logicalSize);
			sb.Append(",\"units\":\"logicalPixels\"");
			sb.Append(",\"platformConvention\":\"dp on Android; points on Apple\"");
			sb.Append(",\"coordinateSpace\":\"window\"");
			sb.Append(",\"source\":\"CometWindowMetrics.Shared.SizeDp\"}");

			sb.Append(",\"safeAreaDp\":{");
			sb.Append("\"insets\":");
			WriteThickness(sb, safeArea);
			sb.Append(",\"units\":\"logicalPixels\"");
			sb.Append(",\"platformConvention\":\"dp on Android; points on Apple\"");
			sb.Append(",\"coordinateSpace\":\"windowInsets\"");
			sb.Append(",\"source\":\"CometWindowMetrics.Shared.SafeAreaDp\"}");

			sb.Append(",\"nativeWindow\":");
			WriteNativeWindow(sb, native);

			sb.Append(",\"elementGeometry\":{");
			sb.Append("\"frameField\":\"frame\"");
			sb.Append(",\"coordinateSpace\":\"parent\"");
			sb.Append(",\"units\":\"logicalPixels\"");
			sb.Append(",\"platformConvention\":\"dp on Android; points on Apple\"");
			sb.Append(",\"kind\":\"layoutAllocation\"");
			sb.Append(",\"nativeElementBounds\":{");
			sb.Append("\"available\":false");
			sb.Append(",\"reason\":\"No native-element frame mapping is registered; View.Frame is not a native or window-relative bound.\"}}");

			sb.Append(",\"elements\":[");
			for (int i = 0; i < nodes.Count; i++)
			{
				if (i > 0) sb.Append(',');
				WriteInspectionElement(sb, nodes[i], nodeIds);
			}
			sb.Append("]}");
			return sb.ToString();
		}

		static void WriteNativeWindow(StringBuilder sb, CometDevRegistry.NativeWindowMetrics? native)
		{
			if (native is null)
			{
				sb.Append("{\"available\":false");
				sb.Append(",\"reason\":\"Native window metrics provider is not registered or has no current window.\"}");
				return;
			}

			sb.Append("{\"available\":true");
			sb.Append(",\"size\":");
			WriteSize(sb, native.Size);
			sb.Append(",\"safeAreaInsets\":");
			if (native.SafeAreaInsets is { } insets)
			{
				sb.Append("{\"available\":true,\"value\":");
				WriteThickness(sb, insets);
				sb.Append('}');
			}
			else
			{
				sb.Append("{\"available\":false,\"reason\":\"The native platform has not delivered current insets.\"}");
			}
			sb.Append(",\"safeAreaLayoutFrame\":");
			if (native.SafeAreaLayoutFrame is { } guideFrame)
			{
				sb.Append("{\"available\":true,\"value\":");
				WriteRect(sb, guideFrame);
				sb.Append('}');
			}
			else
			{
				sb.Append("{\"available\":false,\"reason\":\"The native platform does not expose a safe-area layout guide frame.\"}");
			}
			sb.Append(",\"units\":").Append(JsonEncode(native.Units));
			sb.Append(",\"coordinateSpace\":\"nativeWindow\"");
			if (native.Scale is { } scale)
				sb.Append(",\"physicalPixelsPerLogicalUnit\":").Append(JsonNumber(scale));
			sb.Append(",\"source\":").Append(JsonEncode(native.Source));
			sb.Append('}');
		}

		static void WriteInspectionElement(StringBuilder sb, CometDevRegistry.NodeInfo node, HashSet<int> nodeIds)
		{
			sb.Append('{');
			sb.Append("\"id\":").Append(JsonEncode(node.Id.ToString(CultureInfo.InvariantCulture)));
			sb.Append(",\"parentId\":").Append(
				node.ParentId >= 0 && nodeIds.Contains(node.ParentId)
					? JsonEncode(node.ParentId.ToString(CultureInfo.InvariantCulture))
					: "null");
			sb.Append(",\"type\":").Append(JsonEncode(node.Type));
			if (node.AutomationId is not null)
				sb.Append(",\"automationId\":").Append(JsonEncode(node.AutomationId));
			if (node.HasLayoutFrame)
			{
				sb.Append(",\"frame\":");
				WriteRect(sb, node.Frame);
			}
			else
			{
				sb.Append(",\"frame\":null");
				sb.Append(",\"frameAvailable\":false");
				sb.Append(",\"geometryKind\":\"semanticActionProxy\"");
				sb.Append(",\"geometryUnavailableReason\":")
					.Append(JsonEncode(node.GeometryUnavailableReason));
			}
			sb.Append('}');
		}

		static string ElementJson(int id)
		{
			var nodes = CometDevRegistry.Snapshot();
			foreach (var node in nodes)
			{
				if (node.Id != id)
					continue;
				NormalizeInspectionParent(node, nodes);
				var sb = new StringBuilder();
				WriteElement(sb, node, new Dictionary<int, List<CometDevRegistry.NodeInfo>>());
				return sb.ToString();
			}
			throw new InspectionNotFoundException("element not found");
		}

		sealed class InspectionNotFoundException(string message) : Exception(message);

		static void NormalizeInspectionParent(
			CometDevRegistry.NodeInfo node, List<CometDevRegistry.NodeInfo> nodes)
		{
			if (node.ParentId >= 0 && !ExistsInSnapshot(nodes, node.ParentId))
				node.ParentId = -1;
		}

		static string ElementPropertyJson(int id, string propertyName)
		{
			var nodes = CometDevRegistry.Snapshot();
			CometDevRegistry.NodeInfo? node = null;
			foreach (var candidate in nodes)
			{
				if (candidate.Id == id)
				{
					node = candidate;
					break;
				}
			}
			if (node is null)
				throw new InspectionNotFoundException("element not found");

			NormalizeInspectionParent(node, nodes);
			if (string.Equals(propertyName, "ParentId", System.StringComparison.OrdinalIgnoreCase))
			{
				var parent = node.ParentId >= 0
					? node.ParentId.ToString(CultureInfo.InvariantCulture)
					: null;
				return "{\"success\":true,\"value\":" + (parent is null ? "null" : JsonEncode(parent)) + "}";
			}

			string? value = propertyName.ToLowerInvariant() switch
			{
				"frame" or "bounds" when node.HasLayoutFrame => RectString(node.Frame),
				"frameavailable" or "boundsavailable" => node.HasLayoutFrame ? "true" : "false",
				"geometrykind" when node.IsSemanticActionProxy => "semanticActionProxy",
				"geometryunavailablereason" when !node.HasLayoutFrame => node.GeometryUnavailableReason,
				"automationid" => node.AutomationId,
				"type" => node.Type,
				"boundscoordinatespace" or "framecoordinatespace" when node.HasLayoutFrame => "parent",
				"boundsunits" or "frameunits" when node.HasLayoutFrame => "logicalPixels",
				"boundskind" or "framekind" => node.IsSemanticActionProxy
					? "semanticActionProxy"
					: "layoutAllocation",
				"nativeboundsavailable" => "false",
				"nativeboundsunavailablereason" =>
					node.GeometryUnavailableReason ??
					"No native-element frame mapping is registered; View.Frame is not a native or window-relative bound.",
				_ => FindProperty(node, propertyName),
			};

			return value is null
				? throw new InspectionNotFoundException("property not found")
				: "{\"success\":true,\"value\":" + JsonEncode(value) + "}";
		}

		static string? FindProperty(CometDevRegistry.NodeInfo node, string propertyName)
		{
			foreach (var property in node.Props)
				if (string.Equals(property.Key, propertyName, System.StringComparison.OrdinalIgnoreCase))
					return property.Value;
			return null;
		}

		static string RectString(Microsoft.Maui.Graphics.Rect rect)
		{
			var sb = new StringBuilder();
			WriteRect(sb, rect);
			return sb.ToString();
		}

		static void WriteRect(StringBuilder sb, Microsoft.Maui.Graphics.Rect rect)
		{
			sb.Append("{\"x\":").Append(JsonNumber(rect.X));
			sb.Append(",\"y\":").Append(JsonNumber(rect.Y));
			sb.Append(",\"width\":").Append(JsonNumber(rect.Width));
			sb.Append(",\"height\":").Append(JsonNumber(rect.Height)).Append('}');
		}

		static void WriteSize(StringBuilder sb, Microsoft.Maui.Graphics.Size size)
		{
			sb.Append("{\"width\":").Append(JsonNumber(size.Width));
			sb.Append(",\"height\":").Append(JsonNumber(size.Height)).Append('}');
		}

		static void WriteThickness(StringBuilder sb, Microsoft.Maui.Thickness thickness)
		{
			sb.Append("{\"left\":").Append(JsonNumber(thickness.Left));
			sb.Append(",\"top\":").Append(JsonNumber(thickness.Top));
			sb.Append(",\"right\":").Append(JsonNumber(thickness.Right));
			sb.Append(",\"bottom\":").Append(JsonNumber(thickness.Bottom)).Append('}');
		}

		static string JsonNumber(double value) =>
			double.IsFinite(value)
				? value.ToString("R", CultureInfo.InvariantCulture)
				: "null";

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
			string role = n.Type == "Button" || n.IsSemanticActionProxy || tappable ? "button"
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
			if (n.HasLayoutFrame)
			{
				sb.Append(",\"bounds\":");
				WriteRect(sb, n.Frame);
				sb.Append(",\"boundsQuality\":\"comet-parent-relative-layout-allocation\"");
			}
			sb.Append(",\"frameworkProperties\":{");
			if (n.HasLayoutFrame)
			{
				sb.Append("\"boundsCoordinateSpace\":\"parent\"");
				sb.Append(",\"boundsUnits\":\"logicalPixels\"");
				sb.Append(",\"boundsKind\":\"layoutAllocation\"");
			}
			else
			{
				sb.Append("\"boundsAvailable\":\"false\"");
				sb.Append(",\"boundsKind\":\"semanticActionProxy\"");
				sb.Append(",\"boundsUnavailableReason\":")
					.Append(JsonEncode(n.GeometryUnavailableReason));
			}
			sb.Append(",\"nativeBoundsAvailable\":\"false\"");
			sb.Append(",\"nativeBoundsUnavailableReason\":").Append(JsonEncode(
				n.GeometryUnavailableReason ??
				"No native-element frame mapping is registered; View.Frame is not a native or window-relative bound."));
			sb.Append('}');

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

		/// <summary>
		/// User-back: routes through <see cref="NavigationView.RequestBack"/> which honours
		/// <c>BackButtonBehavior</c> (the dirty-guard command on the ValueRangeEditorPage).
		/// Returns false if no NavigationView is found in the registry.
		/// </summary>
		static bool RequestBack()
		{
			// The registry can retain navigation roots that are no longer attached to the
			// rendered shell. Prefer the backend-owned navigation before falling back to
			// the legacy first-match behavior used by headless/test hosts.
			var nodes = CometDevRegistry.Snapshot();
			foreach (var n in nodes)
			{
				if (n.Type == "NavigationView"
					&& CometDevRegistry.Find(n.Id) is NavigationView nav
					&& nav.HasActiveBackendCallbacks)
				{
					nav.RequestBack();
					return true;
				}
			}

			foreach (var n in nodes)
			{
				if (n.Type == "NavigationView" && CometDevRegistry.Find(n.Id) is NavigationView nav)
				{
					nav.RequestBack();
					return true;
				}
			}
			return false;
		}
	}
}
