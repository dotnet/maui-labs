#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.DevTools
{
	/// <summary>
	/// Opt-in inspection registry for the in-process dev agent (<see cref="CometDevAgent"/>).
	/// Mirrors the ailoha/DevFlow model: external tooling needs a stable, queryable view of
	/// the live UI plus a way to drive semantic actions. Because the new Comet renders through
	/// its own <see cref="ICometBackendNode"/> tree (not MAUI handlers), the old
	/// <c>CometViewResolver</c> tree-walk no longer applies — this registry is the seam instead.
	/// </summary>
	/// <remarks>
	/// Registration is gated on <see cref="Enabled"/> so production pays only a single bool
	/// check per materialized node. When enabled, each materialized <see cref="View"/> is
	/// assigned a stable integer id and linked to its parent, so the agent can present a tree
	/// and resolve an <c>elementId</c> back to the owning view to synthesize an event through
	/// the exact same <see cref="ViewEventSink"/> path a real native interaction would take.
	/// </remarks>
	public static class CometDevRegistry
	{
		sealed class Entry
		{
			public int Id;
			public int ParentId = -1;
			public WeakReference<View> View = null!;
		}

		sealed class SemanticActionEntry
		{
			public int Id;
			public int ParentId = -1;
			public object Token = null!;
			public WeakReference<View> Owner = null!;
			public string Text = "";
			public string? AutomationId;
			public Action Invoke = null!;
		}

		static readonly object _gate = new();
		static readonly Dictionary<int, Entry> _byId = new();
		static readonly Dictionary<int, SemanticActionEntry> _semanticActions = new();
		static readonly ConditionalWeakTable<View, StrongBox<int>> _viewIds = new();
		static int _next = 1;

		/// <summary>When false, <see cref="Register"/> is a no-op (production default).</summary>
		public static bool Enabled { get; set; }

		/// <summary>
		/// Optional platform hook that renders the current UI to a PNG (DevFlow-style in-app
		/// screenshot). Set by the backend (e.g. the SwiftUI host); invoked on the UI thread by
		/// the agent's <c>/ui/screenshot</c> endpoint. Null when the platform can't snapshot.
		/// </summary>
		public static Func<byte[]?>? ScreenshotProvider { get; set; }

		/// <summary>
		/// Optional platform hook for the native window geometry currently hosting Comet.
		/// The values are the platform's direct window measurements, not derived element
		/// bounds. Invoked on the UI thread by the inspection endpoint.
		/// </summary>
		public static Func<NativeWindowMetrics?>? NativeWindowMetricsProvider { get; set; }

		/// <summary>
		/// Optional platform hook that injects a REAL coordinate drag through the platform's
		/// input pipeline — <c>(x1, y1, x2, y2, durationMs) → success</c>, coordinates in
		/// physical pixels. Unlike the semantic actions, this exercises the native gesture
		/// machinery itself (pull-to-refresh, pager swipes, swipe-to-dismiss, flings — the
		/// gesture's velocity comes from the event timing, so a short duration flings).
		/// Called from the agent's worker thread and expected to BLOCK until the gesture
		/// completes (implementations marshal individual events to the UI thread). Null when
		/// the platform can't inject (the agent reports drag as unsupported).
		/// </summary>
		public static Func<float, float, float, float, int, bool>? DragInjector { get; set; }

		/// <summary>
		/// Optional platform hook for programmatic scrolling. Given (elementId, dx, dy) in
		/// device-independent pixels, scrolls the scroll view backing the element (or the
		/// nearest scrollable ancestor). Returns true if handled, false otherwise.
		/// Set by the Compose host (which has access to ScrollState) and the iOS host (UIScrollView).
		/// </summary>
		public static Func<int, double, double, bool>? ScrollInjector { get; set; }

		/// <summary>
		/// Optional main-thread enqueue hook. Schedules an action on the next main-queue
		/// turn (iOS: <c>DispatchQueue.MainQueue.DispatchAsync</c>). Used by semantic alert
		/// actions to let SwiftUI process a dismiss binding update before the action fires
		/// (which may pop the page and orphan the alert). Set by <see cref="CometDevAgent"/>.
		/// When null, actions run synchronously (test/non-UI environments).
		/// </summary>
		public static Action<Action>? MainThreadEnqueue { get; set; }

		/// <summary>Drops all tracked nodes (e.g. before a fresh root mount).</summary>
		public static void Reset()
		{
			lock (_gate)
			{
				_byId.Clear();
				_semanticActions.Clear();
				_viewIds.Clear();
				_next = 1;
			}
		}

		/// <summary>
		/// Records <paramref name="view"/> (and its backend node, via the view) under a stable
		/// id, linked to <paramref name="parent"/>. Idempotent per view instance.
		/// </summary>
		internal static void Register(View view, ICometBackendNode node, View? parent)
		{
			if (!Enabled || view is null)
				return;

			lock (_gate)
			{
				int parentId = -1;
				if (parent is not null && _viewIds.TryGetValue(parent, out var pBox))
					parentId = pBox.Value;

				if (_viewIds.TryGetValue(view, out var existing))
				{
					if (_byId.TryGetValue(existing.Value, out var e))
						e.ParentId = parentId;
					return;
				}

				int id = _next++;
				_viewIds.Add(view, new StrongBox<int>(id));
				_byId[id] = new Entry { Id = id, ParentId = parentId, View = new WeakReference<View>(view) };
			}
		}

		/// <summary>Stops tracking a single view (e.g. when its backend node is disposed).</summary>
		internal static void Unregister(View view)
		{
			if (!Enabled || view is null)
				return;
			lock (_gate)
			{
				if (_viewIds.TryGetValue(view, out var box))
				{
					_byId.Remove(box.Value);
					_viewIds.Remove(view);
				}
			}
		}

		internal sealed class SemanticActionRegistration : IDisposable
		{
			readonly object _token;
			bool _disposed;

			internal SemanticActionRegistration(int id, object token)
			{
				Id = id;
				_token = token;
			}

			public int Id { get; }
			public bool IsActive => IsSemanticActionRegistered(Id, _token);

			public void Dispose()
			{
				if (_disposed)
					return;
				_disposed = true;
				UnregisterSemanticAction(Id, _token);
			}
		}

		/// <summary>Registers a lifecycle-bound semantic proxy for a native action that has no
		/// materialized Comet view, such as a button flattened into a SwiftUI alert.</summary>
		internal static SemanticActionRegistration? RegisterSemanticAction(
			View owner,
			string text,
			string? automationId,
			Action invoke)
		{
			if (!Enabled || owner is null || invoke is null)
				return null;

			lock (_gate)
			{
				var token = new object();
				var id = _next++;
				var parentId = _viewIds.TryGetValue(owner, out var ownerBox)
					? ownerBox.Value
					: -1;
				_semanticActions[id] = new SemanticActionEntry
				{
					Id = id,
					ParentId = parentId,
					Token = token,
					Owner = new WeakReference<View>(owner),
					Text = text,
					AutomationId = string.IsNullOrEmpty(automationId) ? null : automationId,
					Invoke = invoke,
				};
				return new SemanticActionRegistration(id, token);
			}
		}

		static void UnregisterSemanticAction(int id, object token)
		{
			lock (_gate)
			{
				if (_semanticActions.TryGetValue(id, out var entry) &&
					ReferenceEquals(entry.Token, token))
					_semanticActions.Remove(id);
			}
		}

		static bool IsSemanticActionRegistered(int id, object token)
		{
			lock (_gate)
				return _semanticActions.TryGetValue(id, out var entry) &&
					ReferenceEquals(entry.Token, token) &&
					entry.Owner.TryGetTarget(out _);
		}

		/// <summary>Invokes an active semantic action proxy. Returns false for ordinary view
		/// IDs and stale/closed/disposed action IDs.</summary>
		internal static bool TryInvokeSemanticAction(int id)
		{
			Action? invoke = null;
			lock (_gate)
			{
				if (_semanticActions.TryGetValue(id, out var entry) &&
					entry.Owner.TryGetTarget(out _))
					invoke = entry.Invoke;
				else
					_semanticActions.Remove(id);
			}

			if (invoke is null)
				return false;
			invoke();
			return true;
		}

		/// <summary>Transfers the registry identity (stable ID + parent link) from
		/// <paramref name="oldView"/> to <paramref name="newView"/>, so the diff's
		/// view replacement doesn't orphan the entry. Called during
		/// <c>TransferBackendNodeFrom</c>.</summary>
		internal static void TransferIdentity(View oldView, View newView)
		{
			if (!Enabled || oldView is null || newView is null)
				return;
			lock (_gate)
			{
				if (!_viewIds.TryGetValue(oldView, out var box))
					return;
				int id = box.Value;
				_viewIds.Remove(oldView);
				// A replacement may already have been materialized by a retained owner.
				// Remove that obsolete identity and all entries parented beneath it before
				// assigning the retained identity; otherwise the old entry becomes
				// unreachable through _viewIds and survives as a duplicate/detached root.
				if (_viewIds.TryGetValue(newView, out var replaced))
				{
					RemoveSubtreeLocked(replaced.Value, includeRoot: true);
					_viewIds.Remove(newView);
				}
				_viewIds.Add(newView, box);
				if (_byId.TryGetValue(id, out var entry))
					entry.View = new WeakReference<View>(newView);
			}
		}

		/// <summary>
		/// Stops tracking <paramref name="root"/> and everything materialized beneath it
		/// (resolved through the parent links), so a replaced navigation screen or list row set
		/// drops out of the tree deterministically rather than waiting for GC.
		/// </summary>
		internal static void UnregisterSubtree(View root, bool includeRoot)
		{
			if (!Enabled || root is null)
				return;
			lock (_gate)
			{
				if (!_viewIds.TryGetValue(root, out var rootBox))
					return;
				RemoveSubtreeLocked(rootBox.Value, includeRoot);
			}
		}

		static void RemoveSubtreeLocked(int rootId, bool includeRoot)
		{
			// Collect view identities first. Semantic action proxies are lifecycle-bound to
			// their native owner, not materialized view children: includeRoot:false is used
			// to clean a retained own-content generation and must not remove the owner's
			// active native alert actions.
			var viewIds = new HashSet<int> { rootId };
			bool grew = true;
			while (grew)
			{
				grew = false;
				foreach (var e in _byId.Values)
					if (viewIds.Contains(e.ParentId) && viewIds.Add(e.Id))
						grew = true;
			}

			if (!includeRoot)
				viewIds.Remove(rootId);

			var semanticIds = new List<int>();
			foreach (var action in _semanticActions.Values)
				if (viewIds.Contains(action.ParentId))
					semanticIds.Add(action.Id);

			foreach (var id in viewIds)
			{
				if (_byId.TryGetValue(id, out var e))
				{
					if (e.View.TryGetTarget(out var v))
						_viewIds.Remove(v);
					_byId.Remove(id);
				}
			}
			foreach (var id in semanticIds)
				_semanticActions.Remove(id);
		}

		/// <summary>Resolves a tracked id back to its live view, or null if collected.</summary>
		public static View? Find(int id)
		{
			lock (_gate)
			{
				if (_byId.TryGetValue(id, out var e) && e.View.TryGetTarget(out var v))
					return v;
				return null;
			}
		}

		/// <summary>A flattened snapshot of one tracked node for the agent's tree response.</summary>
		public sealed class NodeInfo
		{
			public int Id;
			public int ParentId;
			public string Type = "";
			public string? AutomationId;
			public string? Text;
			public string? Value;
			public bool Enabled = true;
			public bool HasLayoutFrame = true;
			public bool IsSemanticActionProxy;
			public string? GeometryUnavailableReason;
			public Rect Frame;
			public Dictionary<string, string> Props = new();
		}

		/// <summary>Direct native window measurements supplied by a platform root.</summary>
		public sealed class NativeWindowMetrics
		{
			/// <summary>Native window or root-view size in <see cref="Units"/>.</summary>
			public Size Size { get; init; }

			/// <summary>Native safe-area/system-bar insets in <see cref="Units"/>, or null
			/// until the platform has delivered an inset update.</summary>
			public Microsoft.Maui.Thickness? SafeAreaInsets { get; init; }

			/// <summary>The platform's actual safe-area layout guide frame in native-window
			/// coordinates, or null when that platform does not expose one.</summary>
			public Rect? SafeAreaLayoutFrame { get; init; }

			/// <summary>Platform-native unit name, such as physicalPixels or points.</summary>
			public string Units { get; init; } = "";

			/// <summary>Physical pixels per logical unit when known.</summary>
			public double? Scale { get; init; }

			/// <summary>The native APIs from which the values were read.</summary>
			public string Source { get; init; } = "";
		}

		/// <summary>
		/// Builds a snapshot of all live tracked nodes. Touches view state (reads each view's
		/// set properties) so callers must invoke it on the UI thread.
		/// </summary>
		public static List<NodeInfo> Snapshot()
		{
			var list = new List<NodeInfo>();
			List<Entry> entries;
			lock (_gate)
				entries = new List<Entry>(_byId.Values);

			foreach (var e in entries)
			{
				if (!e.View.TryGetTarget(out var view))
					continue;

				var props = ReadProps(view);
				var info = new NodeInfo
				{
					Id = e.Id,
					ParentId = e.ParentId,
					Type = view.GetType().Name,
					AutomationId = string.IsNullOrEmpty(view.AutomationId) ? null : view.AutomationId,
					Enabled = view.IsEnabled,
					Frame = view.Frame,
				};

				// Friendly, queryable subset.
				foreach (var (id, value) in props)
				{
					var name = FriendlyName(id);
					if (name is null)
						continue;
					info.Props[name] = Stringify(value);
				}

				info.Text = info.Props.TryGetValue("text", out var t) ? t
					: info.Props.TryGetValue("buttonText", out var bt) ? bt
					// An Icon whose symbol resolved through a registered icon font emits
					// Icon_Glyph (a PUA char), not Icon_Symbol — expose the symbol name
					// directly so icons stay queryable by name either way.
					: view is Icon icon && !string.IsNullOrEmpty(icon.Symbol) ? icon.Symbol
					: null;
				info.Value = info.Props.TryGetValue("isOn", out var on) ? on
					: info.Props.TryGetValue("sliderValue", out var sv) ? sv
					: info.Props.TryGetValue("selectedIndex", out var selected) ? selected
					: info.Props.TryGetValue("text", out var tv) ? tv
					: null;

				list.Add(info);
			}

			List<SemanticActionEntry> semanticActions;
			lock (_gate)
				semanticActions = new List<SemanticActionEntry>(_semanticActions.Values);
			foreach (var action in semanticActions)
			{
				if (!action.Owner.TryGetTarget(out _))
					continue;
				list.Add(new NodeInfo
				{
					Id = action.Id,
					ParentId = action.ParentId,
					Type = "AlertDialogAction",
					AutomationId = action.AutomationId,
					Text = action.Text,
					Enabled = true,
					HasLayoutFrame = false,
					IsSemanticActionProxy = true,
					GeometryUnavailableReason =
						"Native alert action proxy; no Comet layout allocation or native coordinate frame is exposed.",
					Props = new Dictionary<string, string>
					{
						["tappable"] = "true",
						["semanticActionProxy"] = "true",
					},
				});
			}

			list.Sort((a, b) => a.Id.CompareTo(b.Id));
			return list;
		}

		/// <summary>Replays a view's set-only emission into a recorder to read current props
		/// with zero per-control code (the same shape the backend node receives).</summary>
		static Dictionary<PropertyId, PropertyValue> ReadProps(View view)
		{
			var rec = new RecordingNode();
			try { view.ApplyAllSetProperties(rec); }
			catch { /* a partially-built view may throw; return what we captured */ }
			return rec.Captured;
		}

		static string? FriendlyName(PropertyId id)
		{
			var v = id.Value;
			if (v == PropertyIds.Text_Value.Value) return "text";
			if (v == PropertyIds.Icon_Symbol.Value) return "text";   // Icon("arrow_back") queryable by symbol
			if (v == PropertyIds.Button_Text.Value) return "buttonText";
			if (v == PropertyIds.TextField_Text.Value) return "text";
			if (v == PropertyIds.TextField_Placeholder.Value) return "placeholder";
			if (v == PropertyIds.Toggle_IsOn.Value) return "isOn";
			if (v == PropertyIds.Slider_Value.Value) return "sliderValue";
			if (v == PropertyIds.Nav_SelectedIndex.Value) return "selectedIndex";
			if (v == PropertyIds.BackgroundColor.Value) return "background";
			if (v == PropertyIds.HasTapGesture.Value) return "tappable";
			if (v == PropertyIds.Opacity.Value) return "opacity";
			return null;
		}

		static string Stringify(in PropertyValue value) => value.Kind switch
		{
			PropertyValueKind.Bool => value.AsBool ? "true" : "false",
			PropertyValueKind.Int => value.AsInt.ToString(),
			PropertyValueKind.Long => value.AsLong.ToString(),
			PropertyValueKind.Single => value.AsSingle.ToString("0.###"),
			PropertyValueKind.Double => value.AsDouble.ToString("0.###"),
			PropertyValueKind.Color => value.AsColor?.ToHex() ?? "",
			PropertyValueKind.String => value.AsString ?? "",
			_ => value.AsObject?.ToString() ?? "",
		};

		/// <summary>A throwaway backend node that captures applied properties for inspection.</summary>
		sealed class RecordingNode : ICometBackendNode
		{
			public readonly Dictionary<PropertyId, PropertyValue> Captured = new();
			public void ApplyProperty(PropertyId id, in PropertyValue value) => Captured[id] = value;
			public void InsertChild(int index, ICometBackendNode child) { }
			public void RemoveChildAt(int index) { }
			public void MoveChild(int fromIndex, int toIndex) { }
			public Size Measure(double w, double h) => Size.Zero;
			public void Arrange(Rect frame) { }
			public void SetEventSink(ICometEventSink? sink) { }
			public void Dispose() { }
		}
	}
}
