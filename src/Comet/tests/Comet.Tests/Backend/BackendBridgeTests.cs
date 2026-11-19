#nullable enable
using Comet;
using Comet.Backend;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Proves the View→backend-node materialization end-to-end, host-side: a real Comet
	/// view tree becomes an ICometBackendNode tree with the right structure and only the
	/// properties the views actually set. Node creation uses a recording fake; property
	/// emission exercises the production ApplyAllSetProperties path.
	/// </summary>
	public class BackendBridgeTests
	{
		static BackendBridgeTests()
		{
			// Fluent setters post env writes through ThreadHelper; in a host test there is
			// no platform main thread, so run inline. No MAUI handlers are registered —
			// materialization must work without them.
			ThreadHelper.SetFireOnMainThread(a => a?.Invoke());
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		[Fact]
		public void Materialize_BuildsContainerWithChildren()
		{
			var root = new VStack
			{
				new Text("Hello"),
				new Button("Tap", () => { }),
			};

			var node = Bridge(root);

			Assert.Equal("VStack", node.Kind);
			Assert.Equal(2, node.Children.Count);
			Assert.Equal("Text", node.Children[0].Kind);
			Assert.Equal("Button", node.Children[1].Kind);
		}

		[Fact]
		public void Materialize_EmitsTextValue()
		{
			var node = Bridge(new VStack { new Text("Hello") });
			var textNode = node.Children[0];
			Assert.Equal("Hello", textNode.Get(PropertyIds.Text_Value).AsString);
		}

		[Fact]
		public void Materialize_EmitsButtonText()
		{
			var node = Bridge(new VStack { new Button("Tap", () => { }) });
			Assert.Equal("Tap", node.Children[0].Get(PropertyIds.Button_Text).AsString);
		}

		[Fact]
		public void Materialize_DoesNotEmitDefaultCommonProperties()
		{
			// A plain Text with no opacity/transform set must not push those defaults.
			var node = Bridge(new VStack { new Text("x") });
			var textNode = node.Children[0];

			Assert.Equal(PropertyValueKind.None, textNode.Get(PropertyIds.Opacity).Kind);
			Assert.Equal(PropertyValueKind.None, textNode.Get(PropertyIds.ScaleX).Kind);
			Assert.Equal(PropertyValueKind.None, textNode.Get(PropertyIds.Rotation).Kind);
			Assert.Equal(PropertyValueKind.None, textNode.Get(PropertyIds.IsVisible).Kind);
		}

		[Fact]
		public void Materialize_EmitsSetCommonPropertiesOnly()
		{
			var node = Bridge(new VStack { new Text("x").Opacity(0.5) });
			var textNode = node.Children[0];

			Assert.Equal(0.5, textNode.Get(PropertyIds.Opacity).AsDouble, 10);
			// Scale was never set → still absent.
			Assert.Equal(PropertyValueKind.None, textNode.Get(PropertyIds.ScaleX).Kind);
		}

		[Fact]
		public void Materialize_EmitsTextColorWhenSet()
		{
			var node = Bridge(new VStack { new Text("x").Color(Colors.Red) });
			var textNode = node.Children[0];
			Assert.Equal(Colors.Red, textNode.Get(PropertyIds.Text_Color).AsColor);
		}

		[Fact]
		public void Materialize_EmitsBackgroundPaddingAndSpacing()
		{
			var node = Bridge(new VStack { new Text("x") }.Background(Colors.Blue).Padding(16));

			Assert.Equal(Colors.Blue, node.Get(PropertyIds.BackgroundColor).AsColor);
			Assert.IsType<Microsoft.Maui.Thickness>(node.Get(PropertyIds.Padding).AsObject);
			// VStack carries a default spacing, emitted for the Compose Column arrangement.
			Assert.NotEqual(PropertyValueKind.None, node.Get(PropertyIds.Stack_Spacing).Kind);
		}

		[Fact]
		public void Materialize_ResolvesComponentBodyToConcreteTree()
		{
			var node = Bridge(new BodyComponent());
			// BodyComponent renders to a VStack { Text } — the component itself is transparent.
			Assert.Equal("VStack", node.Kind);
			Assert.Single(node.Children);
			Assert.Equal("Text", node.Children[0].Kind);
			Assert.Equal("from-body", node.Children[0].Get(PropertyIds.Text_Value).AsString);
		}

		sealed class BodyComponent : View
		{
			public BodyComponent() => Body = () => new VStack { new Text("from-body") };
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
