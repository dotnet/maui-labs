#nullable enable
using Comet.Backend;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Locks the backend protocol contract: PropertyValue's no-box union semantics and
	/// the FakeBackendNode patch-stream recorder (child insert/remove/move, property
	/// apply, arrange, sink wiring). Platform-agnostic — runs host-side.
	/// </summary>
	public class BackendProtocolTests
	{
		// --- PropertyValue ---

		[Fact]
		public void PropertyValue_RoundTripsPrimitivesWithoutBoxing()
		{
			Assert.Equal(true, PropertyValue.From(true).AsBool);
			Assert.Equal(42, PropertyValue.From(42).AsInt);
			Assert.Equal(9_000_000_000L, PropertyValue.From(9_000_000_000L).AsLong);
			Assert.Equal(1.5f, PropertyValue.From(1.5f).AsSingle);
			Assert.Equal(3.14159, PropertyValue.From(3.14159).AsDouble, 10);
		}

		[Fact]
		public void PropertyValue_RoundTripsReferenceTypes()
		{
			Assert.Equal("hello", PropertyValue.From("hello").AsString);
			var red = Colors.Red;
			Assert.Same(red, PropertyValue.From(red).AsColor);
			var boxed = new object();
			Assert.Same(boxed, PropertyValue.FromObject(boxed).AsObject);
		}

		[Fact]
		public void PropertyValue_CarriesKindDiscriminator()
		{
			Assert.Equal(PropertyValueKind.Double, PropertyValue.From(1.0).Kind);
			Assert.Equal(PropertyValueKind.String, PropertyValue.From("x").Kind);
			Assert.Equal(PropertyValueKind.None, PropertyValue.None.Kind);
		}

		[Fact]
		public void PropertyValue_EqualityIsValueBased()
		{
			Assert.Equal(PropertyValue.From(5), PropertyValue.From(5));
			Assert.NotEqual(PropertyValue.From(5), PropertyValue.From(6));
			// Same numeric bits, different kind must not be equal.
			Assert.NotEqual(PropertyValue.From(1), PropertyValue.From(1L));
			Assert.Equal(PropertyValue.From("a"), PropertyValue.From("a"));
			Assert.NotEqual(PropertyValue.From("a"), PropertyValue.From("b"));
		}

		// --- PropertyId / EventId ---

		[Fact]
		public void PropertyIds_AreUniqueAndStable()
		{
			Assert.NotEqual(PropertyIds.Opacity, PropertyIds.IsVisible);
			Assert.Equal(PropertyIds.Text_Value, new PropertyId(64));
			Assert.True(PropertyIds.Opacity == new PropertyId(1));
			Assert.True(PropertyIds.Opacity != PropertyIds.BackgroundColor);
		}

		// --- FakeBackendNode patch stream ---

		[Fact]
		public void ApplyProperty_RecordsLatestValue()
		{
			var node = new FakeBackendNode("text");
			node.ApplyProperty(PropertyIds.Text_Value, PropertyValue.From("hi"));
			node.ApplyProperty(PropertyIds.Opacity, PropertyValue.From(0.5));
			node.ApplyProperty(PropertyIds.Text_Value, PropertyValue.From("bye"));

			Assert.Equal("bye", node.Get(PropertyIds.Text_Value).AsString);
			Assert.Equal(0.5, node.Get(PropertyIds.Opacity).AsDouble, 10);
			Assert.Equal(3, node.ApplyCount);
		}

		[Fact]
		public void InsertRemoveMove_MaintainChildOrder()
		{
			var parent = new FakeBackendNode("stack");
			var a = new FakeBackendNode("a");
			var b = new FakeBackendNode("b");
			var c = new FakeBackendNode("c");

			parent.InsertChild(0, a);
			parent.InsertChild(1, c);
			parent.InsertChild(1, b); // a, b, c
			Assert.Equal(new[] { "a", "b", "c" }, ChildKinds(parent));

			parent.MoveChild(0, 2); // b, c, a
			Assert.Equal(new[] { "b", "c", "a" }, ChildKinds(parent));

			parent.RemoveChildAt(1); // b, a
			Assert.Equal(new[] { "b", "a" }, ChildKinds(parent));
		}

		[Fact]
		public void Log_PreservesMutationOrder()
		{
			var parent = new FakeBackendNode("stack");
			var child = new FakeBackendNode("child");
			parent.InsertChild(0, child);
			parent.ApplyProperty(PropertyIds.Opacity, PropertyValue.From(1.0));
			parent.Arrange(new Rect(0, 0, 100, 50));
			parent.RemoveChildAt(0);

			Assert.Equal(
				new[] { "insert@0 child", "apply 1=Double(1)", "arrange {X=0 Y=0 Width=100 Height=50}", "remove@0 child" },
				parent.Log);
		}

		[Fact]
		public void SetEventSink_WiresAndClears()
		{
			var node = new FakeBackendNode();
			var sink = new RecordingSink();
			node.SetEventSink(sink);
			Assert.Same(sink, node.Sink);
			node.SetEventSink(null);
			Assert.Null(node.Sink);
		}

		[Fact]
		public void EventSink_DeliversTypedPayloads()
		{
			var sink = new RecordingSink();
			sink.OnEvent(EventIds.Clicked);
			sink.OnEvent(EventIds.TextChanged, "typed");
			sink.OnGesture(GestureKind.Tap, new GestureData(GestureState.Ended, new Point(3, 4)));

			Assert.Equal(EventIds.Clicked, sink.Events[0].id);
			Assert.Equal("typed", sink.Events[1].payload);
			Assert.Equal(GestureKind.Tap, sink.Gestures[0].kind);
			Assert.Equal(new Point(3, 4), sink.Gestures[0].data.Position);
		}

		static string[] ChildKinds(FakeBackendNode n)
		{
			var result = new string[n.Children.Count];
			for (int i = 0; i < n.Children.Count; i++)
				result[i] = n.Children[i].Kind;
			return result;
		}

		sealed class RecordingSink : ICometEventSink
		{
			public readonly System.Collections.Generic.List<(EventId id, object? payload)> Events = new();
			public readonly System.Collections.Generic.List<(GestureKind kind, GestureData data)> Gestures = new();

			public void OnEvent(EventId id) => Events.Add((id, null));
			public void OnEvent<T>(EventId id, T payload) => Events.Add((id, payload));
			public void OnGesture(GestureKind kind, in GestureData data) => Gestures.Add((kind, data));
		}
	}
}
