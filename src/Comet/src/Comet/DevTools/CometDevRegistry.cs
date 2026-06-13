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

		static readonly object _gate = new();
		static readonly Dictionary<int, Entry> _byId = new();
		static readonly ConditionalWeakTable<View, StrongBox<int>> _viewIds = new();
		static int _next = 1;

		/// <summary>When false, <see cref="Register"/> is a no-op (production default).</summary>
		public static bool Enabled { get; set; }

		/// <summary>Drops all tracked nodes (e.g. before a fresh root mount).</summary>
		public static void Reset()
		{
			lock (_gate)
			{
				_byId.Clear();
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
			public Dictionary<string, string> Props = new();
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
					: null;
				info.Value = info.Props.TryGetValue("isOn", out var on) ? on
					: info.Props.TryGetValue("sliderValue", out var sv) ? sv
					: info.Props.TryGetValue("text", out var tv) ? tv
					: null;

				list.Add(info);
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
			if (v == PropertyIds.Button_Text.Value) return "buttonText";
			if (v == PropertyIds.TextField_Text.Value) return "text";
			if (v == PropertyIds.TextField_Placeholder.Value) return "placeholder";
			if (v == PropertyIds.Toggle_IsOn.Value) return "isOn";
			if (v == PropertyIds.Slider_Value.Value) return "sliderValue";
			if (v == PropertyIds.BackgroundColor.Value) return "background";
			if (v == PropertyIds.HasTapGesture.Value) return "tappable";
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
