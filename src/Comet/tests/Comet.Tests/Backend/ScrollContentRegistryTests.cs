#nullable enable
using System.Collections.Generic;
using System.Linq;
using Comet;
using Comet.Backend;
using Comet.DevTools;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Regression tests for the scroll-content detached-root defect:
	/// <para>
	/// Own-content nodes (ComposeScrollNode, SwiftUIScrollNode) materialize their
	/// content view separately from the generic bridge walk. When the parent parameter
	/// is omitted the content registers as parentId = -1 — a detached root that
	/// <see cref="CometDevRegistry.UnregisterSubtree"/> cannot reach when the hosting
	/// screen is dismissed. This leaves ghost entries for SettingsPage, ActivityFilterView,
	/// and any other ScrollView-hosted content in the BaristaNotes flow.
	/// </para>
	/// </summary>
	public class ScrollContentRegistryTests
	{
		static ScrollContentRegistryTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		/// <summary>A fake node that implements <see cref="IBackendManagesOwnContent"/>
		/// so the generic bridge walk skips auto-materializing children — the node
		/// (simulating ComposeScrollNode) must do it itself.</summary>
		class FakeOwnContentNode : FakeBackendNode, IBackendManagesOwnContent
		{
			public FakeOwnContentNode() : base("scroll") { }
		}

		class CountingNode : FakeBackendNode
		{
			public CountingNode(string kind) : base(kind) { }

			public int DisposeCount { get; private set; }

			public override void Dispose()
			{
				DisposeCount++;
				base.Dispose();
			}
		}

		sealed class FakeOwnedScrollNode : CountingNode, IBackendReconcilesOwnContent, ICometBackendNode
		{
			ScrollView _owner;
			readonly OwnedContentSlot<CountingNode> _content;

			public FakeOwnedScrollNode(
				ScrollView owner,
				CometNodeFactory factory)
				: base("scroll")
			{
				_owner = owner;
				_content = new OwnedContentSlot<CountingNode>(owner, factory, Ctx);
			}

			public CountingNode? ContentNode { get; private set; }

			public void MaterializeContent()
			{
				var content = Assert.IsAssignableFrom<View>(_owner.Content);
				ContentNode = _content.Materialize(content);
			}

			void ICometBackendNode.OnOwnerViewChanged(View newView, bool isHotReload)
			{
				_owner = Assert.IsType<ScrollView>(newView);
				_content.TransferOwner(newView);
				var content = _owner.Content;
				var current = (content?.GetView() ?? content)?.Node as CountingNode;
				if (content is null ||
					current is null ||
					!_content.RetainTransferred(current, content))
				{
					_content.Clear();
					ContentNode = null;
				}
				else
				{
					ContentNode = current;
				}
			}

			public override void Dispose()
			{
				_content.Dispose();
				base.Dispose();
			}
		}

		sealed record LazyScrollFixture(
			ScrollView Scroll,
			FakeOwnedScrollNode ScrollNode,
			CountingNode ContentNode,
			string ContentAutomationId,
			OwnedContentGeneration NavigationGeneration);

		/// <summary>
		/// Proves the defect: when an own-content node materializes scroll content
		/// without passing a parent, the content appears as a detached root
		/// (parentId = -1) in the dev registry.
		/// </summary>
		[Fact]
		public void ScrollContent_MaterializedWithoutParent_IsDetachedRoot()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var content = new Text("Settings Content").AutomationId("content");

				// Materialize the content as a standalone root (no parent) —
				// this is what ComposeScrollNode.EnsureContent did BEFORE the fix.
				CometBackendBridge.Materialize(content,
					v => new FakeBackendNode(v.GetType().Name), Ctx);

				var snapshot = CometDevRegistry.Snapshot();
				var entry = snapshot.FirstOrDefault(n => n.AutomationId == "content");

				Assert.NotNull(entry);
				Assert.Equal(-1, entry!.ParentId); // detached — the defect
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		/// <summary>
		/// Proves the fix: when an own-content node materializes scroll content
		/// WITH the scroll view as parent, the content is registered under the
		/// scroll (parentId != -1), so UnregisterSubtree can reach it when the
		/// hosting screen is dismissed.
		/// </summary>
		[Fact]
		public void ScrollContent_MaterializedWithScrollParent_RegisteredUnderScroll()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var content = new VStack
				{
					new Text("Settings Content").AutomationId("settings_text"),
				};
				content.AutomationId("settings_body");
				var scroll = new ScrollView();
				scroll.Add(content);
				scroll.AutomationId("settings_scroll");

				// Factory: IBackendManagesOwnContent for ScrollView, plain fake for others.
				ICometBackendNode factory(View v) => v is ScrollView
					? (ICometBackendNode)new FakeOwnContentNode()
					: new FakeBackendNode(v.GetType().Name);

				// Materialize the scroll. The bridge sees IBackendManagesOwnContent and
				// skips auto-materializing children — the node manages them.
				CometBackendBridge.Materialize(scroll, factory, Ctx);

				// Simulate what ComposeScrollNode.EnsureContent does AFTER the fix:
				// materialize the content with the scroll as parent via MaterializeChild
				// (which recovers the factory/context from the scroll's registration).
				CometBackendBridge.MaterializeChild(content, scroll);

				var snapshot = CometDevRegistry.Snapshot();
				var scrollEntry = snapshot.FirstOrDefault(n => n.AutomationId == "settings_scroll");
				var bodyEntry = snapshot.FirstOrDefault(n => n.AutomationId == "settings_body");

				Assert.NotNull(scrollEntry);
				Assert.NotNull(bodyEntry);

				// Content is under the scroll, not detached.
				Assert.Equal(scrollEntry!.Id, bodyEntry!.ParentId);
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		/// <summary>
		/// When UnregisterSubtree is called on a page that contains a scroll view,
		/// scroll content registered under the scroll must also be removed — no ghost
		/// entries survive.
		/// </summary>
		[Fact]
		public void ScrollContent_UnregisterSubtree_RemovesScrollContent()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var content = new Text("Filter Items");
				content.AutomationId("filter_content");
				var scroll = new ScrollView();
				scroll.Add(content);
				scroll.AutomationId("filter_scroll");

				ICometBackendNode factory(View v) => v is ScrollView
					? (ICometBackendNode)new FakeOwnContentNode()
					: new FakeBackendNode(v.GetType().Name);

				CometBackendBridge.Materialize(scroll, factory, Ctx);
				CometBackendBridge.MaterializeChild(content, scroll);

				// Verify content exists before cleanup
				var before = CometDevRegistry.Snapshot();
				Assert.Contains(before, n => n.AutomationId == "filter_content");

				// Unregister the scroll subtree (simulates page dismissal)
				CometDevRegistry.UnregisterSubtree(scroll, includeRoot: true);

				var after = CometDevRegistry.Snapshot();
				Assert.DoesNotContain(after, n => n.AutomationId == "filter_scroll");
				Assert.DoesNotContain(after, n => n.AutomationId == "filter_content");
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void ScrollOwner_Dispose_RemovesRetainedContentSubtree()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var retainedContent = new VStack
				{
					new Text("Filter Option").AutomationId("retained_filter_option"),
				};
				retainedContent.AutomationId("retained_filter_content");
				var original = new ScrollView { retainedContent };
				original.AutomationId("activity_filter_content");
				FakeOwnedScrollNode? scrollNode = null;

				ICometBackendNode factory(View v)
				{
					if (v is ScrollView scroll)
						return scrollNode = new FakeOwnedScrollNode(scroll, factory);
					return new CountingNode(v.GetType().Name);
				}

				CometBackendBridge.Materialize(original, factory, Ctx);
				Assert.NotNull(scrollNode);
				scrollNode.MaterializeContent();
				var retainedNode = Assert.IsType<CountingNode>(scrollNode.ContentNode);

				// The content node transfers to the replacement logical owner while the
				// scroll node keeps ownership of the same materialized generation.
				var replacement = new ScrollView { retainedContent };
				replacement.AutomationId("activity_filter_content");
				replacement.UpdateFromOldView(original);

				var before = CometDevRegistry.Snapshot();
				Assert.Contains(before, n => n.AutomationId == "retained_filter_content");
				Assert.Contains(before, n => n.AutomationId == "retained_filter_option");

				replacement.Dispose();

				Assert.Equal(1, scrollNode.DisposeCount);
				Assert.Equal(1, retainedNode.DisposeCount);
				var after = CometDevRegistry.Snapshot();
				Assert.DoesNotContain(after, n => n.AutomationId == "activity_filter_content");
				Assert.DoesNotContain(after, n => n.AutomationId == "retained_filter_content");
				Assert.DoesNotContain(after, n => n.AutomationId == "retained_filter_option");
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void PlatformScrollNodes_DisposeRetainedContentSubtree()
		{
			var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
				System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var files = new[]
			{
				System.IO.Path.Combine(projectRoot, "src", "Comet", "Platform", "Compose", "ComposeScrollNode.cs"),
				System.IO.Path.Combine(projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUIScrollNode.cs"),
			};

			foreach (var file in files)
			{
				var source = System.IO.File.ReadAllText(file);
				Assert.Contains("OwnedContentSlot<", source);
				Assert.Contains("_content.Dispose();", source);
			}
		}

		[Fact]
		public void OwnedScrollContent_ReplacementDisposesOldGenerationAndRetainsTransferredNode()
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var original = CreateLazyScroll("replacement_old");
				var oldContentNode = original.ContentNode;
				var replacementContent = new VStack
				{
					new Text("replacement").AutomationId("replacement_child"),
				}.AutomationId("replacement_content");
				var replacement = new ScrollView { replacementContent }
					.AutomationId("replacement_scroll");

				replacement.Diff(original.Scroll, false);
				original.Scroll.Dispose();

				Assert.Equal(1, oldContentNode.DisposeCount);
				Assert.Null(original.ScrollNode.ContentNode);
				Assert.DoesNotContain(
					CometDevRegistry.Snapshot(),
					node => node.AutomationId == "replacement_old");

				original.ScrollNode.MaterializeContent();
				var replacementNode = Assert.IsType<CountingNode>(
					original.ScrollNode.ContentNode);
				Assert.Equal(0, replacementNode.DisposeCount);
				var replacementScroll = Assert.Single(
					CometDevRegistry.Snapshot(),
					node => node.AutomationId == "replacement_scroll");
				var replacementContentEntry = Assert.Single(
					CometDevRegistry.Snapshot(),
					node => node.AutomationId == "replacement_content");
				Assert.Equal(replacementScroll.Id, replacementContentEntry.ParentId);

				replacement.Dispose();
				Assert.Equal(1, replacementNode.DisposeCount);
				Assert.DoesNotContain(
					CometDevRegistry.Snapshot(),
					node => node.AutomationId is "replacement_content" or "replacement_child");
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}

		[Fact]
		public void LazyScrollContent_NavigationPop_DisposesChildOnceAndRemovesRegistryEntry()
		{
			AssertLazyScrollRelease(
				fixtures =>
				NavigationStackLifecycle.TryPop(
					new List<View> { new Text("root"), fixtures[0].Scroll },
					(page, _) => ReleaseNavigationGeneration(page, fixtures[0].NavigationGeneration),
					out _),
				count: 1);
		}

		[Fact]
		public void LazyScrollContent_NavigationPopToRoot_DisposesEveryChildOnceAndRemovesRegistryEntries()
		{
			AssertLazyScrollRelease(
				fixtures =>
				NavigationStackLifecycle.ResetToRoot(
					new List<View>
					{
						new Text("root"),
						fixtures[0].Scroll,
						fixtures[1].Scroll,
					},
					new Text("root"),
					(page, _) =>
					{
						var fixture = fixtures.Single(item => ReferenceEquals(item.Scroll, page));
						ReleaseNavigationGeneration(page, fixture.NavigationGeneration);
					}),
				count: 2);
		}

		[Fact]
		public void LazyScrollContent_TerminalDispose_DisposesChildOnceAndRemovesRegistryEntry()
		{
			AssertLazyScrollRelease(
				fixtures =>
				fixtures[0].Scroll.Dispose(),
				count: 1);
		}

		[Fact]
		public void PlatformScrollNodes_GuardDisposedStaleRendering()
		{
			var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
				System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var compose = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeScrollNode.cs"));
			var swiftui = System.IO.File.ReadAllText(System.IO.Path.Combine(
				projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUIScrollNode.cs"));

			Assert.Contains("OwnedContentSlot<ComposeNode>", compose);
			Assert.Contains("_content.Materialize(contentView)", compose);
			Assert.Contains("_content.Dispose();", compose);
			Assert.Contains("OwnedContentSlot<ICometBackendNode>", swiftui);
			Assert.Contains("_content.Materialize(contentView)", swiftui);
			Assert.Contains("_content.Dispose();", swiftui);
			Assert.Contains("if (_disposed)", swiftui);
		}

		static LazyScrollFixture CreateLazyScroll(string automationId)
		{
			var content = new Text(automationId).AutomationId(automationId);
			var scroll = new ScrollView { content }
				.AutomationId($"{automationId}_scroll");
			FakeOwnedScrollNode? scrollNode = null;
			var navigationGeneration = new OwnedContentGeneration(
				new ContentView(),
				factory,
				Ctx);

			ICometBackendNode factory(View view)
			{
				if (view is ScrollView owner)
					return scrollNode = new FakeOwnedScrollNode(owner, factory);
				return new CountingNode(view.GetType().Name);
			}

			navigationGeneration.Materialize(scroll);
			Assert.NotNull(scrollNode);
			scrollNode.MaterializeContent();
			var contentNode = Assert.IsType<CountingNode>(scrollNode.ContentNode);
			Assert.Single(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId == automationId);
			return new LazyScrollFixture(
				scroll,
				scrollNode,
				contentNode,
				automationId,
				navigationGeneration);
		}

		static void ReleaseNavigationGeneration(
			View page,
			OwnedContentGeneration generation)
		{
			generation.Dispose();
		}

		static void AssertLazyScrollRelease(
			System.Action<IReadOnlyList<LazyScrollFixture>> release,
			int count)
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				var fixtures = Enumerable.Range(0, count)
					.Select(index => CreateLazyScroll($"lazy_scroll_child_{index}"))
					.ToArray();

				release(fixtures);

				Assert.All(fixtures, fixture =>
				{
					Assert.Equal(1, fixture.ContentNode.DisposeCount);
					Assert.Equal(1, fixture.ScrollNode.DisposeCount);
					Assert.DoesNotContain(
						CometDevRegistry.Snapshot(),
						node => node.AutomationId == fixture.ContentAutomationId);
				});
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}
	}
}
