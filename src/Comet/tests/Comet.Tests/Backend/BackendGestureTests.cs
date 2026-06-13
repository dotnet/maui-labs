#nullable enable
using Comet;
using Comet.Backend;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Locks tap-gesture wiring: a view with a tap gesture emits the HasTapGesture flag
	/// (so the backend applies a clickable modifier) and a tap routed back through the
	/// sink invokes the Comet gesture handler.
	/// </summary>
	public class BackendGestureTests
	{
		static BackendGestureTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		[Fact]
		public void TapGesture_EmitsFlagAndRoutesToHandler()
		{
			var taps = 0;
			var row = new HStack { new Text("Row") }.OnTap(_ => taps++);

			var node = (FakeBackendNode)CometBackendBridge.Materialize(
				row, v => new FakeBackendNode(v.GetType().Name), Ctx);

			Assert.True(node.Get(PropertyIds.HasTapGesture).AsBool);

			node.Sink!.OnGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default));
			node.Sink!.OnGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default));

			Assert.Equal(2, taps);
		}

		[Fact]
		public void NoTapGesture_DoesNotEmitFlag()
		{
			var node = (FakeBackendNode)CometBackendBridge.Materialize(
				new HStack { new Text("plain") }, v => new FakeBackendNode(v.GetType().Name), Ctx);

			Assert.Equal(PropertyValueKind.None, node.Get(PropertyIds.HasTapGesture).Kind);
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
