#nullable enable
using System.Collections.Generic;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// A host-side <see cref="ICometBackendNode"/> that records the patch stream it
	/// receives, so the diff→backend contract can be asserted without any platform.
	/// This replaces the role the legacy MAUI mapper tests played.
	/// </summary>
	public sealed class FakeBackendNode : ICometBackendNode
	{
		public string Kind { get; }
		public FakeBackendNode(string kind = "node") => Kind = kind;

		/// <summary>Last applied value per property id (the node's current state).</summary>
		public readonly Dictionary<ushort, PropertyValue> Properties = new();

		/// <summary>Ordered child list, mirroring InsertChild/RemoveChildAt/MoveChild.</summary>
		public readonly List<FakeBackendNode> Children = new();

		/// <summary>Full ordered log of every mutation, for ordering/dedup assertions.</summary>
		public readonly List<string> Log = new();

		public ICometEventSink? Sink { get; private set; }
		public Rect? ArrangedFrame { get; private set; }
		public Size MeasureResult { get; set; } = Size.Zero;
		public bool Disposed { get; private set; }

		/// <summary>Count of ApplyProperty calls (including no-op re-applies, if any reach us).</summary>
		public int ApplyCount { get; private set; }

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			ApplyCount++;
			Properties[id.Value] = value;
			Log.Add($"apply {id.Value}={value}");
		}

		public void InsertChild(int index, ICometBackendNode child)
		{
			var fake = (FakeBackendNode)child;
			Children.Insert(index, fake);
			Log.Add($"insert@{index} {fake.Kind}");
		}

		public void RemoveChildAt(int index)
		{
			var removed = Children[index];
			Children.RemoveAt(index);
			Log.Add($"remove@{index} {removed.Kind}");
		}

		public void MoveChild(int fromIndex, int toIndex)
		{
			var node = Children[fromIndex];
			Children.RemoveAt(fromIndex);
			Children.Insert(toIndex, node);
			Log.Add($"move {fromIndex}->{toIndex} {node.Kind}");
		}

		public Size Measure(double widthConstraint, double heightConstraint) => MeasureResult;

		public void Arrange(Rect frame)
		{
			ArrangedFrame = frame;
			Log.Add($"arrange {frame}");
		}

		public void SetEventSink(ICometEventSink? sink)
		{
			Sink = sink;
			Log.Add(sink is null ? "sink=null" : "sink=set");
		}

		public void Dispose()
		{
			Disposed = true;
			Log.Add("dispose");
		}

		/// <summary>Convenience: the current value of a property id, or None if unset.</summary>
		public PropertyValue Get(PropertyId id)
			=> Properties.TryGetValue(id.Value, out var v) ? v : PropertyValue.None;
	}
}
