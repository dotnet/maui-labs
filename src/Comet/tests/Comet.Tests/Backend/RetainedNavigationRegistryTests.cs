#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comet.Backend;
using Comet.DevTools;
using Comet.Reactive;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.Backend
{
	public class RetainedNavigationRegistryTests
	{
		static RetainedNavigationRegistryTests()
			=> ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
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

		sealed class OwnContentNode : CountingNode, IBackendManagesOwnContent
		{
			public OwnContentNode(string kind) : base(kind) { }
		}

		sealed class EditScreen : View
		{
			public Signal<bool> PickerOpen { get; } = new(false);

			[Body]
			View Body()
			{
				if (PickerOpen.Value)
				{
					return new Grid
					{
						new Text("Close").AutomationId("picker_close"),
						new VStack
						{
							new Text("Ristretto").AutomationId("picker_choice_ristretto"),
							new Text("Espresso").AutomationId("picker_choice_espresso"),
							new Text("Lungo").AutomationId("picker_choice_lungo"),
						},
					}.AutomationId("picker_drinktype");
				}

				return new Grid
				{
					new Text("Espresso").AutomationId("shot_tile_method"),
					new Text("Ristretto").AutomationId("shot_tile_drink_type"),
					new Text("Update").AutomationId("shot_save"),
				}.AutomationId("edit_drink_page");
			}
		}

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		[Fact]
		public void TransferredRenderedRoot_RemainsTheRegistryCleanupIdentity()
		{
			CometDevRegistry.Enabled = true;
			try
			{
				CometDevRegistry.Reset();
				var oldRoot = new Grid
				{
					new Text("Activity").AutomationId("activity_child"),
				}.AutomationId("activity_page");
				CometBackendBridge.Materialize(
					oldRoot,
					view => new FakeBackendNode(view.GetType().Name),
					Ctx);

				var currentRoot = new Grid
				{
					new Text("Activity").AutomationId("activity_child"),
				}.AutomationId("activity_page");
				currentRoot.UpdateFromOldView(oldRoot);

				Assert.Contains(
					CometDevRegistry.Snapshot(),
					node => node.AutomationId == "activity_page");

				CometDevRegistry.UnregisterSubtree(currentRoot, includeRoot: true);

				Assert.DoesNotContain(
					CometDevRegistry.Snapshot(),
					node => node.AutomationId is "activity_page" or "activity_child");
			}
			finally
			{
				CometDevRegistry.Reset();
				CometDevRegistry.Enabled = false;
			}
		}

		[Fact]
		public void PersistentNavigationViews_KeepIndependentLogicalStacks()
		{
			var activityRoot = new Text("Activity").AutomationId("activity_page");
			var activityDetail = new Text("Shot").AutomationId("shot_detail");
			var activity = new NavigationView { activityRoot };
			activity.SetPerformNavigate(_ => { });
			_ = activity.GetBackendNavigationStack();
			activity.Navigate(activityDetail);

			var settingsRoot = new Text("Settings").AutomationId("settings_page");
			var settingsDetail = new Text("Equipment").AutomationId("equipment_detail");
			var settings = new NavigationView { settingsRoot };
			settings.SetPerformNavigate(_ => { });
			_ = settings.GetBackendNavigationStack();
			settings.Navigate(settingsDetail);

			Assert.Equal(
				new[] { activityRoot, activityDetail },
				activity.GetBackendNavigationStack());
			Assert.Equal(
				new[] { settingsRoot, settingsDetail },
				settings.GetBackendNavigationStack());

			activity.SetBackendNavigationStack(new[] { activityRoot });

			Assert.Equal(new[] { activityRoot }, activity.GetBackendNavigationStack());
			Assert.Equal(
				new[] { settingsRoot, settingsDetail },
				settings.GetBackendNavigationStack());
		}

		[Fact]
		public void ClearedHostedGeneration_CanBeFullyRematerialized()
		{
			var content = new VStack
			{
				new Text("Setting").AutomationId("setting_item"),
			};
			var root = new ScrollView { content };

			CometBackendBridge.Materialize(
				root,
				view => new FakeBackendNode(view.GetType().Name),
				Ctx);
			Assert.NotNull(root.Node);
			Assert.NotNull(content.Node);

			CometBackendBridge.ClearMaterializedNodes(root);

			Assert.Null(root.Node);
			Assert.Null(content.Node);

			CometBackendBridge.Materialize(
				root,
				view => new FakeBackendNode(view.GetType().Name),
				Ctx);
			Assert.NotNull(root.Node);
			Assert.NotNull(content.Node);
		}

		[Fact]
		public void RootToDetail_OnlyActiveGenerationIsRegisteredAndPopRestoresRoot()
		{
			WithRegistry(() =>
			{
				var root = ShotScreen("new_drink_page", "Log Drink");
				var detail = ShotScreen("edit_drink_page", "Update");
				var navigation = new NavigationView { Content = root }
					.AutomationId("shot_navigation");
				var stack = new List<View> { root };
				ICometBackendNode Factory(View view)
				{
					CountingNode node = view is NavigationView
						? new OwnContentNode(view.GetType().Name)
						: new CountingNode(view.GetType().Name);
					return node;
				}

				CometBackendBridge.Materialize(navigation, Factory, Ctx);
				var active = new OwnedContentSlot<CountingNode>(
					navigation,
					Factory,
					Ctx);
				active.Materialize(root);

				AssertRegistryOwnsOnly(
					navigation,
					"new_drink_page",
					"edit_drink_page");

				stack.Add(detail);
				active.Materialize(detail);

				Assert.Same(detail, active.View);
				Assert.Null(root.Node);
				Assert.NotNull(detail.Node);
				Assert.All(EnumerateViews(root), view => Assert.Null(view.Node));
				AssertRegistryOwnsOnly(
					navigation,
					"edit_drink_page",
					"new_drink_page");
				AssertUniqueAutomationIds();

				Assert.True(NavigationStackLifecycle.TryPop(
					stack,
					(page, _) =>
					{
						if (active.IsActive(page))
							active.Clear();
					},
					out var popped));
				Assert.Same(detail, popped);
				active.Materialize(stack[^1]);

				Assert.Same(root, active.View);
				Assert.True(detail.IsDisposed);
				AssertRegistryOwnsOnly(
					navigation,
					"new_drink_page",
					"edit_drink_page");
				AssertUniqueAutomationIds();

				active.Dispose();
				navigation.Dispose();
			});
		}

		[Fact]
		public void ActiveDetail_InternalPickerReplacementUpdatesVisualAndRegistryAtomically()
		{
			WithRegistry(() =>
			{
				var root = ShotScreen("new_drink_page", "Log Drink");
				var detail = new EditScreen();
				var navigation = new NavigationView { Content = root }
					.AutomationId("shot_navigation");
				ICometBackendNode Factory(View view)
					=> view is NavigationView
						? new OwnContentNode(view.GetType().Name)
						: new CountingNode(view.GetType().Name);

				CometBackendBridge.Materialize(navigation, Factory, Ctx);
				var active = new OwnedContentSlot<CountingNode>(
					navigation,
					Factory,
					Ctx);
				active.Materialize(root);
				active.Materialize(detail);

				var editor = detail.GetView();
				var retainedRootNode = editor.Node;
				detail.PickerOpen.Value = true;
				ReactiveScheduler.FlushSync();
				var picker = detail.GetView();

				Assert.NotSame(editor, picker);
				Assert.Same(retainedRootNode, picker.Node);
				Assert.Same(detail, active.View);
				Assert.True(editor.IsDisposed);
				AssertRegistryOwnsOnly(
					navigation,
					"picker_drinktype",
					"edit_drink_page",
					"new_drink_page");
				var snapshot = CometDevRegistry.Snapshot();
				Assert.Single(snapshot, node => node.AutomationId == "picker_close");
				Assert.Equal(
					3,
					snapshot.Count(node =>
						node.AutomationId?.StartsWith(
							"picker_choice_",
							StringComparison.Ordinal) == true));
				AssertUniqueAutomationIds();

				var pickerChoiceNodes = EnumerateViews(picker)
					.Where(view =>
						view.AutomationId?.StartsWith(
							"picker_choice_",
							StringComparison.Ordinal) == true)
					.Select(view => Assert.IsType<CountingNode>(view.Node))
					.ToArray();
				active.Clear();
				Assert.All(
					pickerChoiceNodes,
					node => Assert.Equal(1, node.DisposeCount));
				Assert.All(
					EnumerateViews(picker),
					view => Assert.Null(view.Node));

				active.Dispose();
				detail.Dispose();
				navigation.Dispose();
			});
		}

		[Fact]
		public void ManagementRootToDetail_DeactivatesEveryBuriedActionRowAndPopsCleanly()
		{
			WithRegistry(() =>
			{
				var settings = PageWithActionRow("settings_section_root");
				var management = PageWithActionRow("bean_management_page");
				var detail = PageWithActionRow("bean_detail_page");
				var navigation = new NavigationView { Content = settings }
					.AutomationId("settings_navigation");
				var stack = new List<View> { settings };
				ICometBackendNode Factory(View view)
					=> view is NavigationView
						? new OwnContentNode(view.GetType().Name)
						: new CountingNode(view.GetType().Name);

				CometBackendBridge.Materialize(navigation, Factory, Ctx);
				var active = new OwnedContentSlot<CountingNode>(
					navigation,
					Factory,
					Ctx);
				active.Materialize(settings);

				stack.Add(management);
				active.Materialize(management);
				AssertRegistryOwnsOnly(
					navigation,
					"bean_management_page",
					"settings_section_root",
					"bean_detail_page");
				Assert.Single(
					CometDevRegistry.Snapshot(),
					node => node.AutomationId == "bottom_action_row");
				AssertUniqueAutomationIds();

				stack.Add(detail);
				active.Materialize(detail);
				AssertRegistryOwnsOnly(
					navigation,
					"bean_detail_page",
					"settings_section_root",
					"bean_management_page");
				Assert.Single(
					CometDevRegistry.Snapshot(),
					node => node.AutomationId == "bottom_action_row");
				AssertUniqueAutomationIds();

				PopAndExpose(stack, active);
				Assert.Same(management, active.View);
				Assert.True(detail.IsDisposed);
				AssertRegistryOwnsOnly(
					navigation,
					"bean_management_page",
					"settings_section_root",
					"bean_detail_page");
				Assert.Single(
					CometDevRegistry.Snapshot(),
					node => node.AutomationId == "bottom_action_row");

				PopAndExpose(stack, active);
				Assert.Same(settings, active.View);
				Assert.True(management.IsDisposed);
				AssertRegistryOwnsOnly(
					navigation,
					"settings_section_root",
					"bean_management_page",
					"bean_detail_page");
				Assert.Single(
					CometDevRegistry.Snapshot(),
					node => node.AutomationId == "bottom_action_row");
				AssertUniqueAutomationIds();

				active.Dispose();
				navigation.Dispose();
			});
		}

		[Fact]
		public void ProfileRoundTrip_RetainsNewDrinkGeometryAndResetsNativeNavigationChrome()
		{
			var newDrinkNavigation = new NavigationView();
			var settingsNavigation = new NavigationView();
			var newDrink = ShotLayout(out var firstRow, out var saveBand);
			var settings = PageWithActionRow("settings_section_root");
			var profiles = PageWithActionRow("profile_management_page");
			var settingsStack = new List<View> { settings };

			ICometBackendNode Factory(View view) => new CountingNode(view.GetType().Name);
			using var active = new OwnedContentSlot<CountingNode>(
				newDrinkNavigation,
				Factory,
				Ctx);

			active.Materialize(newDrink);
			CometBackendLayoutEngine.Layout(newDrink, new Microsoft.Maui.Graphics.Size(402, 815));
			var initialFirstRow = firstRow.Frame;
			var initialSaveBand = saveBand.Frame;

			active.TransferOwner(settingsNavigation);
			active.Materialize(settings);
			CometBackendLayoutEngine.Layout(settings, new Microsoft.Maui.Graphics.Size(402, 874));

			settingsStack.Add(profiles);
			active.Materialize(profiles);
			CometBackendLayoutEngine.Layout(profiles, new Microsoft.Maui.Graphics.Size(402, 874));
			Assert.True(NavigationStackLifecycle.TryPop(
				settingsStack,
				(page, _) =>
				{
					if (active.IsActive(page))
						active.Clear();
				},
				out _));
			active.Materialize(settingsStack[^1]);
			CometBackendLayoutEngine.Layout(settingsStack[^1], new Microsoft.Maui.Graphics.Size(402, 874));

			active.TransferOwner(newDrinkNavigation);
			active.Materialize(newDrink);
			CometBackendLayoutEngine.Layout(newDrink, new Microsoft.Maui.Graphics.Size(402, 815));

			Assert.Equal(initialFirstRow.Y, firstRow.Frame.Y, 3);
			Assert.Equal(initialFirstRow.Height, firstRow.Frame.Height, 3);
			Assert.Equal(initialSaveBand.Y, saveBand.Frame.Y, 3);
			Assert.Equal(initialSaveBand.Height, saveBand.Frame.Height, 3);

			var projectRoot = IOPath.GetFullPath(IOPath.Combine(
				AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var swiftSource = File.ReadAllText(IOPath.Combine(
				projectRoot,
				"src",
				"Comet.SwiftUI.Shim",
				"Sources",
				"CometSwiftUIShim",
				"CometSwiftUIShim.swift"));
			var rootIndex = swiftSource.IndexOf(
				"CometNodeView(node: root)",
				StringComparison.Ordinal);
			var destinationIndex = swiftSource.IndexOf(
				"CometNodeView(node: node.children[index])",
				StringComparison.Ordinal);
			Assert.True(rootIndex >= 0);
			Assert.True(destinationIndex > rootIndex);
			Assert.Equal(
				3,
				swiftSource.Split(
					".modifier(NavigationBackButtonModifier(node: node))",
					StringSplitOptions.None).Length - 1);
			Assert.Contains(".toolbar(.hidden, for: .navigationBar)", swiftSource);
			Assert.Contains("content.toolbar(.visible, for: .navigationBar)", swiftSource);
		}

		[Fact]
		public void PlatformNavigationNodes_UseOwnedGenerationAndSharedExposureSeams_CompileGuard()
		{
			var projectRoot = IOPath.GetFullPath(IOPath.Combine(
				AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
			var composePath = IOPath.Combine(
				projectRoot, "src", "Comet", "Platform", "Compose", "ComposeNavigationNode.cs");
			var swiftPath = IOPath.Combine(
				projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUINavigationNode.cs");
			var backendViewPath = IOPath.Combine(
				projectRoot, "src", "Comet", "Backend", "View.Backend.cs");

			foreach (var source in new[]
			{
				File.ReadAllText(composePath),
				File.ReadAllText(swiftPath),
			})
			{
				Assert.Contains("DeactivateRenderedGenerations();", source);
				Assert.Contains("GetBackendNavigationStack()", source);
				Assert.Contains("SetBackendNavigationStack(_stack)", source);
				Assert.Contains("OwnedContentSlot<", source);
				Assert.Contains("NavigationStackLifecycle.PrepareCurrentForExposure", source);
				Assert.DoesNotContain(
					"Dictionary<View, OwnedContentGeneration>",
					source);
				Assert.DoesNotContain("CometDevRegistry.UnregisterSubtree", source);
				Assert.DoesNotContain("_renderedScreens", source);
			}

			var backendView = File.ReadAllText(backendViewPath);
			var transfer = backendView.IndexOf(
				"CometDevRegistry.TransferIdentity(oldView, this);",
				StringComparison.Ordinal);
			var ownerChanged = backendView.IndexOf(
				"node.OnOwnerViewChanged(this, isHotReload);",
				StringComparison.Ordinal);
			Assert.True(
				transfer >= 0 && ownerChanged > transfer,
				"Registry identity must move before an own-content node activates its replacement generation.");
		}

		static Grid ShotScreen(string automationId, string action) => new Grid
		{
			new Text("Espresso").AutomationId("shot_tile_method"),
			new Text("Ristretto").AutomationId("shot_tile_drink_type"),
			new Text(action).AutomationId("shot_save"),
		}.AutomationId(automationId);

		static Grid ShotLayout(out Grid firstRow, out Grid saveBand)
		{
			firstRow = new Grid
			{
				new Text("METHOD"),
				new Text("Espresso"),
			}
			.Padding(new Microsoft.Maui.Thickness(16, 14))
			.MinimumHeight(120)
			.Frame(height: 120)
			.AutomationId("shot_tile_method");
			saveBand = new Grid
			{
				new Text("SAVE"),
				new Text("Log Drink"),
			}
			.Padding(new Microsoft.Maui.Thickness(16, 14))
			.Frame(height: 120)
			.AutomationId("shot_save");

			var grid = new Grid(
				columns: new object[] { "*" },
				rows: new object[] { "Auto", "*", "*", "*", "*", "*", "*" },
				rowSpacing: 1)
			{
				firstRow.Cell(row: 0),
				new Grid().Cell(row: 1),
				new Grid().Cell(row: 2),
				new Grid().Cell(row: 3),
				new Grid().Cell(row: 4),
				new Grid().Cell(row: 5),
				saveBand.Cell(row: 6),
			};
			return grid.Padding(1).AutomationId("new_drink_page");
		}

		static Grid PageWithActionRow(string automationId) => new Grid
		{
			new Text(automationId),
			new Grid
			{
				new Text("Action"),
			}.AutomationId("bottom_action_row"),
		}.AutomationId(automationId);

		static void PopAndExpose(
			List<View> stack,
			OwnedContentSlot<CountingNode> active)
		{
			Assert.True(NavigationStackLifecycle.TryPop(
				stack,
				(page, _) =>
				{
					if (active.IsActive(page))
						active.Clear();
				},
				out _));
			active.Materialize(stack[^1]);
		}

		static void AssertRegistryOwnsOnly(
			NavigationView navigation,
			string expectedSurface,
			params string[] absentSurfaces)
		{
			var snapshot = CometDevRegistry.Snapshot();
			var owner = Assert.Single(
				snapshot,
				node => node.AutomationId == navigation.AutomationId);
			var surface = Assert.Single(
				snapshot,
				node => node.AutomationId == expectedSurface);
			Assert.Equal(owner.Id, surface.ParentId);
			foreach (var absent in absentSurfaces)
				Assert.DoesNotContain(
					snapshot,
					node => node.AutomationId == absent);
		}

		static void AssertUniqueAutomationIds()
		{
			var duplicates = CometDevRegistry.Snapshot()
				.Where(node => !string.IsNullOrEmpty(node.AutomationId))
				.GroupBy(node => node.AutomationId, StringComparer.Ordinal)
				.Where(group => group.Count() > 1)
				.Select(group => group.Key)
				.ToArray();
			Assert.Empty(duplicates);
		}

		static IEnumerable<View> EnumerateViews(View root)
		{
			var rendered = root.GetView() ?? root;
			yield return rendered;
			if (rendered is not IContainerView container)
				yield break;

			foreach (var child in container.GetChildren())
			{
				if (child is null)
					continue;
				foreach (var descendant in EnumerateViews(child))
					yield return descendant;
			}
		}

		static void WithRegistry(Action test)
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = true;
			try
			{
				test();
			}
			finally
			{
				CometDevRegistry.Enabled = false;
				CometDevRegistry.Reset();
			}
		}
	}
}
