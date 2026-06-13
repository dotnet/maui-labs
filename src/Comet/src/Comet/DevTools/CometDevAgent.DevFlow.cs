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
						view.OnBackendEvent(EventIds.Clicked);
						view.OnBackendGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default));
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
				case ("POST", "/api/v1/ui/actions/scroll"):
					// SwiftUI/Compose own scrolling + focus; accept as a no-op so the CLI succeeds.
					return ActionOk();

				case ("POST", "/api/v1/ui/actions/back"):
					return RunOnMain(() =>
					{
						PopNavigation();
						return ActionOk();
					});

				default:
					// 200 + success:false keeps the CLI from hanging on unimplemented routes.
					return "{\"success\":false,\"error\":\"unimplemented\"}";
			}
		}

		static View ResolveElement(string body)
		{
			var idStr = GetString(body, "elementId");
			if (idStr is null || !int.TryParse(idStr, out var id))
				throw new System.InvalidOperationException("elementId is required");
			return Resolve(id);
		}

		static string ActionOk() => "{\"success\":true}";

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
			"\"ui.actions\":{\"version\":1,\"features\":[\"tap\",\"fill\",\"clear\",\"back\"]}}}";

		// Builds the nested ElementInfo tree the CLI expects (single root + children[]),
		// resolving the registry's flat parentId list into a hierarchy.
		static string DevFlowTreeJson()
		{
			var nodes = CometDevRegistry.Snapshot();
			var byParent = new Dictionary<int, List<CometDevRegistry.NodeInfo>>();
			CometDevRegistry.NodeInfo? root = null;
			foreach (var n in nodes)
			{
				if (n.ParentId < 0 && root is null)
					root = n;
				if (!byParent.TryGetValue(n.ParentId, out var list))
					byParent[n.ParentId] = list = new();
				list.Add(n);
			}

			// The CLI deserializes the tree as List<ElementInfo> (one root per window), so the
			// response is a JSON array.
			var sb = new StringBuilder();
			sb.Append('[');
			if (root is not null)
				WriteElement(sb, root, byParent);
			sb.Append(']');
			return sb.ToString();
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
