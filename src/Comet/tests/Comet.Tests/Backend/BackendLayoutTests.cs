#nullable enable
using Comet;
using Comet.Backend;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Proves the C# Yoga engine drives the backend node protocol: a materialized Comet tree
	/// is laid out by <see cref="CometBackendLayoutEngine"/>, with leaf intrinsic sizes coming
	/// from each node's <c>Measure</c> and the computed frames pushed via <c>Arrange</c>. This
	/// is the host-side proof that Yoga — not the native UI kit — positions the tree.
	/// </summary>
	public class BackendLayoutTests
	{
		static BackendLayoutTests() => ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		static FakeBackendNode Node(View v) => (FakeBackendNode)v.Node!;

		[Fact]
		public void VStack_StacksChildrenAlongYWithSpacing()
		{
			var a = new Text("a");
			var b = new Text("b");
			var root = new VStack(spacing: 10f) { a, b };
			Bridge(root);

			Node(a).MeasureResult = new Size(100, 20);
			Node(b).MeasureResult = new Size(100, 30);

			CometBackendLayoutEngine.Layout(root, new Size(200, 400));

			var fa = Node(a).ArrangedFrame!.Value;
			var fb = Node(b).ArrangedFrame!.Value;

			Assert.Equal(0, fa.Y, 3);
			Assert.Equal(20, fa.Height, 3);
			// Second child is offset by the first child's height + the 10pt gap.
			Assert.Equal(30, fb.Y, 3);
			Assert.Equal(30, fb.Height, 3);
		}

		[Fact]
		public void HStack_StacksChildrenAlongX()
		{
			var a = new Text("a");
			var b = new Text("b");
			var root = new HStack(spacing: 0f) { a, b };
			Bridge(root);

			Node(a).MeasureResult = new Size(40, 20);
			Node(b).MeasureResult = new Size(60, 20);

			CometBackendLayoutEngine.Layout(root, new Size(400, 200));

			var fa = Node(a).ArrangedFrame!.Value;
			var fb = Node(b).ArrangedFrame!.Value;

			Assert.Equal(0, fa.X, 3);
			Assert.Equal(40, fa.Width, 3);
			// Second child sits to the right of the first.
			Assert.Equal(40, fb.X, 3);
			Assert.Equal(60, fb.Width, 3);
			Assert.Equal(0, fb.Y, 3);
		}

		[Fact]
		public void NestedStacks_ComposeOffsets()
		{
			var leaf = new Text("x");
			var inner = new HStack(spacing: 0f) { leaf };
			var outer = new VStack(spacing: 0f) { new Text("header"), inner };
			Bridge(outer);

			Node(((IContainerView)outer).GetChildren()[0]).MeasureResult = new Size(100, 50); // header
			Node(leaf).MeasureResult = new Size(30, 30);

			CometBackendLayoutEngine.Layout(outer, new Size(300, 300));

			// The inner HStack is the second row of the outer VStack → offset down by the
			// header's height; the leaf is arranged relative to its HStack parent.
			var innerFrame = Node(inner).ArrangedFrame!.Value;
			Assert.Equal(50, innerFrame.Y, 3);

			var leafFrame = Node(leaf).ArrangedFrame!.Value;
			Assert.Equal(0, leafFrame.X, 3);
			Assert.Equal(30, leafFrame.Width, 3);
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
