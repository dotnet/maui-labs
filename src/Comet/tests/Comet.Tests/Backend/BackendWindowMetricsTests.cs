#nullable enable
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Locks the per-root reactive window contract adaptive samples (Reply's list-detail /
	/// navigation-suite chrome) program against: a root-size update re-renders exactly the
	/// views that read the metrics, size classes follow the M3 breakpoints, and resolution
	/// prefers an environment-installed instance over the shared default.
	/// </summary>
	public class BackendWindowMetricsTests
	{
		static BackendWindowMetricsTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		[Theory]
		[InlineData(0, WindowWidthClass.Compact)]
		[InlineData(411, WindowWidthClass.Compact)]
		[InlineData(599.9, WindowWidthClass.Compact)]
		[InlineData(600, WindowWidthClass.Medium)]
		[InlineData(839.9, WindowWidthClass.Medium)]
		[InlineData(840, WindowWidthClass.Expanded)]
		[InlineData(1280, WindowWidthClass.Expanded)]
		public void WidthClass_FollowsM3Breakpoints(double dp, WindowWidthClass expected)
			=> Assert.Equal(expected, CometWindowMetrics.ClassifyWidth(dp));

		[Theory]
		[InlineData(0, WindowHeightClass.Compact)]
		[InlineData(479.9, WindowHeightClass.Compact)]
		[InlineData(480, WindowHeightClass.Medium)]
		[InlineData(899.9, WindowHeightClass.Medium)]
		[InlineData(900, WindowHeightClass.Expanded)]
		public void HeightClass_FollowsM3Breakpoints(double dp, WindowHeightClass expected)
			=> Assert.Equal(expected, CometWindowMetrics.ClassifyHeight(dp));

		[Fact]
		public void ResizeUpdate_ReRendersViewThatReadsWidthClass()
		{
			// The adaptive-chrome pattern: a bound Text (any reactive binding) switches on
			// the width class; crossing a breakpoint re-emits, a same-class resize does not.
			var metrics = new CometWindowMetrics();
			var root = new VStack
			{
				new Text(() => metrics.WidthClass == WindowWidthClass.Expanded
					? "two-pane" : "single-pane"),
			};
			root.WindowMetrics(metrics);

			var node = Bridge(root);
			var textNode = node.Children[0];
			Assert.Equal("single-pane", textNode.Get(PropertyIds.Text_Value).AsString);

			metrics.Update(new Size(1024, 800));   // phone → tablet width
			ReactiveScheduler.FlushSync();
			Assert.Equal("two-pane", textNode.Get(PropertyIds.Text_Value).AsString);

			metrics.Update(new Size(900, 800));    // still Expanded — value re-derives equal
			ReactiveScheduler.FlushSync();
			Assert.Equal("two-pane", textNode.Get(PropertyIds.Text_Value).AsString);

			metrics.Update(new Size(400, 800));    // back under the breakpoint
			ReactiveScheduler.FlushSync();
			Assert.Equal("single-pane", textNode.Get(PropertyIds.Text_Value).AsString);
		}

		[Fact]
		public void EqualSizeUpdate_DoesNotNotify()
		{
			// Per-frame layout callbacks forward sizes unconditionally; the Signal's equality
			// gate must absorb them.
			var metrics = new CometWindowMetrics();
			metrics.Update(new Size(400, 800));

			int notifications = 0;
			metrics.SizeDp.PropertyChanged += (_, __) => notifications++;

			metrics.Update(new Size(400, 800));
			metrics.Update(new Size(400, 800));
			Assert.Equal(0, notifications);

			metrics.Update(new Size(401, 800));
			Assert.Equal(1, notifications);
		}

		[Fact]
		public void GetWindowMetrics_PrefersEnvironmentInstance_FallsBackToShared()
		{
			var perRoot = new CometWindowMetrics();
			var root = new VStack();
			var child = new Text("x");
			root.Add(child);
			root.WindowMetrics(perRoot);
			Bridge(root);

			Assert.Same(perRoot, root.GetWindowMetrics());
			Assert.Same(perRoot, child.GetWindowMetrics());

			var orphan = new Text("y");
			Assert.Same(CometWindowMetrics.Shared, orphan.GetWindowMetrics());
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
