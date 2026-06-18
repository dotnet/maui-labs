#nullable enable
using System;
using Microsoft.Maui.Graphics;

namespace Comet.Backend
{
	/// <summary>
	/// A dense identifier for an event a backend node raises back into the Comet
	/// view (clicked, text changed, …). Mirrors <see cref="PropertyId"/> so the same
	/// generated-switch dispatch pattern applies.
	/// </summary>
	public readonly struct EventId : IEquatable<EventId>
	{
		public ushort Value { get; }

		public EventId(ushort value) => Value = value;

		public bool Equals(EventId other) => Value == other.Value;
		public override bool Equals(object? obj) => obj is EventId other && Equals(other);
		public override int GetHashCode() => Value;
		public override string ToString() => $"EventId({Value})";

		public static bool operator ==(EventId a, EventId b) => a.Value == b.Value;
		public static bool operator !=(EventId a, EventId b) => a.Value != b.Value;
	}

	/// <summary>Stable registry of <see cref="EventId"/> constants.</summary>
	public static class EventIds
	{
		public static readonly EventId Clicked = new(1);
		public static readonly EventId TextChanged = new(2);     // payload: string
		public static readonly EventId ValueChanged = new(3);    // payload: double
		public static readonly EventId Toggled = new(4);         // payload: bool
		public static readonly EventId Completed = new(5);
		public static readonly EventId SelectionChanged = new(6);// payload: int
		public static readonly EventId Appeared = new(7);
		public static readonly EventId Disappeared = new(8);
		public static readonly EventId Focused = new(9);
		public static readonly EventId Unfocused = new(10);
		public static readonly EventId DrawerClosed = new(11);   // user dismissed the drawer
		public static readonly EventId DialogDismissed = new(12);// user dismissed an AlertDialog
		public static readonly EventId SelectorPanelDismissed = new(13); // user back-dismissed an input-selector panel
	}

	/// <summary>The category of a recognized gesture flowing back from a backend node.</summary>
	public enum GestureKind : byte
	{
		Tap = 0,
		DoubleTap,
		LongPress,
		Pan,
		Pinch,
		Swipe,
		Pointer,
	}

	/// <summary>The phase of a continuous gesture (pan/pinch/pointer).</summary>
	public enum GestureState : byte
	{
		Began = 0,
		Changed,
		Ended,
		Cancelled,
	}

	/// <summary>
	/// Payload accompanying a gesture callback. A small <c>readonly struct</c> passed
	/// by <c>in</c> so continuous gestures don't allocate per frame.
	/// </summary>
	public readonly struct GestureData
	{
		public GestureState State { get; }
		public Point Position { get; }
		public Point Delta { get; }
		public float Scale { get; }

		public GestureData(GestureState state, Point position, Point delta = default, float scale = 1f)
		{
			State = state;
			Position = position;
			Delta = delta;
			Scale = scale;
		}
	}

	/// <summary>
	/// Receives events and gestures a backend node raises. The Comet view implements
	/// (or adapts to) this so native interactions drive reactive state.
	/// </summary>
	public interface ICometEventSink
	{
		void OnEvent(EventId id);
		void OnEvent<T>(EventId id, T payload);
		void OnGesture(GestureKind kind, in GestureData data);
	}
}
