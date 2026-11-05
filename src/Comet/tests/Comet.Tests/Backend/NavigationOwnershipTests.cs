#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Backend;
using Xunit;

namespace Comet.Tests.Backend;

public class NavigationOwnershipTests
{
	static NavigationOwnershipTests()
		=> ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

	sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}

	sealed class NavigationTransferNode
		: FakeBackendNode, IBackendRetainsLogicalContentOnOwnerTransfer, ICometBackendNode
	{
		readonly List<View> _stack;
		readonly Dictionary<NavigationView, int> _navigateCalls = new();
		readonly Dictionary<NavigationView, int> _popCalls = new();
		NavigationView _owner;
		object? _activeNativeGeneration;

		public NavigationTransferNode(NavigationView owner) : base("navigation")
		{
			_owner = owner;
			_stack = owner.GetBackendNavigationStack().ToList();
			_activeNativeGeneration = _stack.Count == 0 ? null : new object();
			Attach(owner);
		}

		public bool ReboundBeforeDetach { get; private set; }
		public NavigationOwnerTransferAction? LastTransferAction { get; private set; }
		public IReadOnlyList<View> Stack => _stack;
		public object? ActiveNativeGeneration => _activeNativeGeneration;

		public int NavigateCalls(NavigationView owner)
			=> _navigateCalls.GetValueOrDefault(owner);

		public int PopCalls(NavigationView owner)
			=> _popCalls.GetValueOrDefault(owner);

		void Attach(NavigationView owner)
		{
			owner.SetPerformNavigate(view =>
			{
				_navigateCalls[owner] = NavigateCalls(owner) + 1;
				_stack.Add(view);
				_activeNativeGeneration = new object();
			});
			owner.SetPerformPop(() =>
			{
				_popCalls[owner] = PopCalls(owner) + 1;
				NavigationStackLifecycle.TryPop(
					_stack,
					(_, _) => { },
					out _);
				owner.SetBackendNavigationStack(_stack);
				_activeNativeGeneration = _stack.Count == 0 ? null : new object();
			});
		}

		void ICometBackendNode.OnOwnerViewChanged(View newView, bool isHotReload)
		{
			var replacement = Assert.IsType<NavigationView>(newView);
			var transfer = NavigationStackLifecycle.DetermineOwnerTransfer(
				_owner,
				_stack,
				replacement,
				isHotReload);
			LastTransferAction = transfer;
			_owner.SetRetainsLogicalStackAfterOwnerTransfer(
				transfer == NavigationOwnerTransferAction.SwitchStack);
			replacement.SetRetainsLogicalStackAfterOwnerTransfer(false);

			if (transfer == NavigationOwnerTransferAction.PreserveStack)
			{
				if (_stack.Count > 0 && replacement.Content is { } replacementRoot)
					_stack[0] = replacementRoot;
			}
			else
			{
				if (transfer == NavigationOwnerTransferAction.SwitchStack)
					_owner.SetBackendNavigationStack(_stack);
				_stack.Clear();
				if (!isHotReload)
					_stack.AddRange(replacement.GetBackendNavigationStack());
				if (_stack.Count == 0 && replacement.Content is { } replacementRoot)
					_stack.Add(replacementRoot);
				_activeNativeGeneration = _stack.Count == 0 ? null : new object();
			}

			replacement.SetBackendNavigationStack(_stack);
			ReboundBeforeDetach = _stack.All(page =>
				ReferenceEquals(page.Navigation, replacement));

			_owner.DetachBackendCallbacks();
			_owner = replacement;
			Attach(replacement);
			base.OnOwnerViewChanged(newView, isHotReload);
		}
	}

	sealed class UnkeyedNavigationRerenderComponent : View
	{
		public string RootText { get; set; } = "old";

		[Body]
		View Body()
			=> new NavigationView
			{
				Content = new ContentView
				{
					Content = new Text(RootText),
				},
			};
	}

	sealed class PersistentSectionNavigationComponent : View
	{
		public PersistentSectionNavigationComponent()
		{
			ActivityRoot = new ContentView
			{
				Content = new Text("activity"),
			};
			Activity = new NavigationView { Content = ActivityRoot };
			SettingsRoot = new ContentView
			{
				Content = new Text("settings"),
			};
			Settings = new NavigationView { Content = SettingsRoot };
		}

		public NavigationView Activity { get; }
		public View ActivityRoot { get; }
		public NavigationView Settings { get; }
		public View SettingsRoot { get; }
		public bool ShowSettings { get; set; }

		[Body]
		View Body()
			=> new ContentView
			{
				Content = ShowSettings ? Settings : Activity,
			};
	}

	sealed class NestedPersistentSectionHost : View
	{
		public PersistentSectionNavigationComponent? Sections { get; private set; }

		[Body]
		View Body()
			=> Sections ??= new PersistentSectionNavigationComponent();
	}

	[Fact]
	public void BackChrome_IsOwnedByVisiblePage_AndResetsWhenReturningToRoot()
	{
		var root = new Text("root");
		var hiddenDetail = new Text("detail").BackButtonBehavior(new BackButtonBehavior
		{
			IsVisible = false,
			IsEnabled = false,
			Title = "Cancel",
		});
		var stack = new List<View> { root, hiddenDetail };

		var hidden = NavigationBackChromeState.Resolve(stack);
		Assert.False(hidden.IsVisible);
		Assert.False(hidden.IsEnabled);
		Assert.Equal("Cancel", hidden.Title);

		stack.RemoveAt(1);
		var resetRoot = NavigationBackChromeState.Resolve(stack);
		Assert.False(resetRoot.IsVisible);
		Assert.True(resetRoot.IsEnabled);
		Assert.Equal(string.Empty, resetRoot.Title);

		stack.Add(new Text("standard detail"));
		var standardDetail = NavigationBackChromeState.Resolve(stack);
		Assert.True(standardDetail.IsVisible);
		Assert.True(standardDetail.IsEnabled);
		Assert.Equal(string.Empty, standardDetail.Title);
	}

	[Fact]
	public void SystemBackHandler_IsRegisteredForRootPageGuard()
	{
		var unguardedRoot = new Text("root");
		var guardedRoot = new Text("guarded").BackButtonBehavior(new BackButtonBehavior
		{
			IsVisible = false,
			Command = new TestCommand(),
		});
		var dormantRoot = new Text("dormant").BackButtonBehavior(new BackButtonBehavior
		{
			IsVisible = false,
			Command = new TestCommand(canExecute: false),
		});

		Assert.False(NavigationStackLifecycle.ShouldRegisterSystemBackHandler(
			new View[] { unguardedRoot }));
		Assert.True(NavigationStackLifecycle.ShouldRegisterSystemBackHandler(
			new View[] { guardedRoot }));
		Assert.False(NavigationStackLifecycle.ShouldRegisterSystemBackHandler(
			new View[] { dormantRoot }));
		Assert.True(NavigationStackLifecycle.ShouldRegisterSystemBackHandler(
			new View[] { unguardedRoot, new Text("detail") }));
	}

	sealed class TestCommand(bool canExecute = true) : System.Windows.Input.ICommand
	{
		public event EventHandler? CanExecuteChanged;
		public bool CanExecute(object? parameter) => canExecute;
		public void Execute(object? parameter) { }
	}

	sealed class SiblingNavigationRerenderComponent : View
	{
		public int Version { get; set; }

		[Body]
		View Body()
			=> new Grid
			{
				new NavigationView
				{
					Content = new ContentView
					{
						Content = new Text($"left {Version}"),
					},
				},
				new NavigationView
				{
					Content = new ContentView
					{
						Content = new Text($"right {Version}"),
					},
				},
			};
	}

	sealed class FirstNestedNavigationOwner : View
	{
		[Body]
		View Body()
			=> new NavigationView
			{
				Content = new ContentView
				{
					Content = new Text("first nested owner"),
				},
			};
	}

	sealed class SecondNestedNavigationOwner : View
	{
		[Body]
		View Body()
			=> new NavigationView
			{
				Content = new ContentView
				{
					Content = new Text("second nested owner"),
				},
			};
	}

	sealed class ReplacingNestedNavigationHost : View
	{
		public bool ShowSecond { get; set; }

		[Body]
		View Body()
			=> ShowSecond
				? new SecondNestedNavigationOwner()
				: new FirstNestedNavigationOwner();
	}

	sealed class CountingContentPage : ContentView
	{
		public int DisposeCount { get; private set; }

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				DisposeCount++;
			base.Dispose(disposing);
		}
	}

	static readonly BackendContext Ctx = new(new EmptyServiceProvider());

	[Fact]
	public void KeyedOwnerTransfer_RebindsPreservedDetailToReplacementExactlyOnce()
	{
		var oldRoot = new ContentView { Content = new Text("old root") }.Key("root");
		var oldNavigation = new NavigationView { Content = oldRoot }.Key("navigation");
		var transferNode = new NavigationTransferNode(oldNavigation);
		CometBackendBridge.Materialize(
			oldNavigation,
			view => ReferenceEquals(view, oldNavigation)
				? transferNode
				: new FakeBackendNode(view.GetType().Name),
			Ctx);

		var detail = new ContentView { Content = new Text("detail") };
		oldNavigation.Navigate(detail);

		var replacementRoot = new ContentView { Content = new Text("new root") }.Key("root");
		var replacement = new NavigationView { Content = replacementRoot }.Key("navigation");
		replacement.Diff(oldNavigation, false);
		oldNavigation.Dispose();

		Assert.True(transferNode.ReboundBeforeDetach);
		Assert.Same(replacement, detail.Navigation);
		Assert.Same(replacement, detail.Content.Navigation);

		detail.Navigation!.Navigate(new Text("next"));
		detail.Navigation.Pop();

		Assert.Equal(1, transferNode.NavigateCalls(oldNavigation));
		Assert.Equal(0, transferNode.PopCalls(oldNavigation));
		Assert.Equal(1, transferNode.NavigateCalls(replacement));
		Assert.Equal(1, transferNode.PopCalls(replacement));
	}

	[Fact]
	public void UnkeyedComponentRerender_PreservesPushedDetailAndActiveNativeGeneration()
	{
		var component = new UnkeyedNavigationRerenderComponent();
		NavigationTransferNode? transferNode = null;
		CometBackendBridge.Materialize(
			component,
			view => view is NavigationView navigation
				? transferNode ??= new NavigationTransferNode(navigation)
				: new FakeBackendNode(view.GetType().Name),
			Ctx);

		var oldNavigation = Assert.IsType<NavigationView>(component.GetView());
		var detail = new CountingContentPage
		{
			Content = new Text("unsaved detail"),
		};
		oldNavigation.Navigate(detail);
		var detailGeneration = transferNode!.ActiveNativeGeneration;

		component.RootText = "updated";
		component.Reload();

		var replacement = Assert.IsType<NavigationView>(component.GetView());
		Assert.NotSame(oldNavigation, replacement);
		Assert.Equal(
			NavigationOwnerTransferAction.PreserveStack,
			transferNode.LastTransferAction);
		Assert.Equal(2, transferNode.Stack.Count);
		Assert.Same(replacement.Content, transferNode.Stack[0]);
		Assert.Same(detail, transferNode.Stack[1]);
		Assert.Same(detailGeneration, transferNode.ActiveNativeGeneration);
		Assert.Same(transferNode, replacement.Node);
		Assert.Same(replacement, detail.Navigation);
		Assert.Same(replacement, detail.Content.Navigation);
		Assert.True(oldNavigation.IsDisposed);
		Assert.Equal(0, detail.DisposeCount);

		component.Dispose();
		Assert.Equal(1, detail.DisposeCount);
	}

	[Fact]
	public void PersistentUnkeyedSameRootTypeSectionSwitch_RestoresEachIndependentStackRepeatedly()
	{
		var component = new PersistentSectionNavigationComponent();
		NavigationTransferNode? transferNode = null;
		CometBackendBridge.Materialize(
			component,
			view => view is NavigationView navigation
				? transferNode ??= new NavigationTransferNode(navigation)
				: new FakeBackendNode(view.GetType().Name),
			Ctx);

		var activityDetail = new CountingContentPage
		{
			Content = new Text("unsaved activity"),
		};
		component.Activity.Navigate(activityDetail);
		var activityGeneration = transferNode!.ActiveNativeGeneration;

		component.ShowSettings = true;
		component.Reload();

		Assert.Equal(
			NavigationOwnerTransferAction.SwitchStack,
			transferNode.LastTransferAction);
		Assert.Equal(new View[] { component.SettingsRoot }, transferNode.Stack);
		Assert.NotSame(activityGeneration, transferNode.ActiveNativeGeneration);
		Assert.Equal(
			new View[] { component.ActivityRoot, activityDetail },
			component.Activity.GetBackendNavigationStack());
		Assert.False(component.Activity.IsDisposed);

		var settingsDetail = new CountingContentPage
		{
			Content = new Text("unsaved settings"),
		};
		component.Settings.Navigate(settingsDetail);

		for (var cycle = 0; cycle < 3; cycle++)
		{
			component.ShowSettings = false;
			component.Reload();

			Assert.Equal(
				NavigationOwnerTransferAction.SwitchStack,
				transferNode.LastTransferAction);
			Assert.Equal(
				new View[] { component.ActivityRoot, activityDetail },
				transferNode.Stack);
			Assert.Same(component.Activity, activityDetail.Navigation);
			Assert.Same(component.Activity, activityDetail.Content.Navigation);
			Assert.Equal(
				new View[] { component.SettingsRoot, settingsDetail },
				component.Settings.GetBackendNavigationStack());
			Assert.False(component.Settings.IsDisposed);
			Assert.False(activityDetail.IsDisposed);
			Assert.False(settingsDetail.IsDisposed);
			Assert.Equal(0, activityDetail.DisposeCount);
			Assert.Equal(0, settingsDetail.DisposeCount);

			component.ShowSettings = true;
			component.Reload();

			Assert.Equal(
				NavigationOwnerTransferAction.SwitchStack,
				transferNode.LastTransferAction);
			Assert.Equal(
				new View[] { component.SettingsRoot, settingsDetail },
				transferNode.Stack);
			Assert.Same(component.Settings, settingsDetail.Navigation);
			Assert.Same(component.Settings, settingsDetail.Content.Navigation);
			Assert.Equal(
				new View[] { component.ActivityRoot, activityDetail },
				component.Activity.GetBackendNavigationStack());
			Assert.False(component.Activity.IsDisposed);
			Assert.False(activityDetail.IsDisposed);
			Assert.False(settingsDetail.IsDisposed);
			Assert.Equal(0, activityDetail.DisposeCount);
			Assert.Equal(0, settingsDetail.DisposeCount);
		}

		component.Dispose();
		Assert.Equal(1, activityDetail.DisposeCount);
		Assert.Equal(1, settingsDetail.DisposeCount);
	}

	[Fact]
	public void NestedConstructorOwnedPersistentSections_KeepIndependentStacks()
	{
		var host = new NestedPersistentSectionHost();
		NavigationTransferNode? transferNode = null;
		CometBackendBridge.Materialize(
			host,
			view => view is NavigationView navigation
				? transferNode ??= new NavigationTransferNode(navigation)
				: new FakeBackendNode(view.GetType().Name),
			Ctx);

		var sections = Assert.IsType<PersistentSectionNavigationComponent>(
			host.Sections);
		var activityDetail = new CountingContentPage
		{
			Content = new Text("nested activity detail"),
		};
		sections.Activity.Navigate(activityDetail);

		sections.ShowSettings = true;
		sections.Reload();

		Assert.Equal(
			NavigationOwnerTransferAction.SwitchStack,
			transferNode!.LastTransferAction);
		Assert.Equal(new View[] { sections.SettingsRoot }, transferNode.Stack);
		Assert.Equal(
			new View[] { sections.ActivityRoot, activityDetail },
			sections.Activity.GetBackendNavigationStack());
		Assert.Same(sections.Activity, activityDetail.Navigation);
		Assert.Equal(0, activityDetail.DisposeCount);

		var settingsDetail = new CountingContentPage
		{
			Content = new Text("nested settings detail"),
		};
		sections.Settings.Navigate(settingsDetail);

		sections.ShowSettings = false;
		sections.Reload();

		Assert.Equal(
			NavigationOwnerTransferAction.SwitchStack,
			transferNode.LastTransferAction);
		Assert.Equal(
			new View[] { sections.ActivityRoot, activityDetail },
			transferNode.Stack);
		Assert.Equal(
			new View[] { sections.SettingsRoot, settingsDetail },
			sections.Settings.GetBackendNavigationStack());
		Assert.Same(sections.Settings, settingsDetail.Navigation);
		Assert.Equal(0, activityDetail.DisposeCount);
		Assert.Equal(0, settingsDetail.DisposeCount);

		host.Dispose();
		Assert.Equal(1, activityDetail.DisposeCount);
		Assert.Equal(1, settingsDetail.DisposeCount);
	}

	[Fact]
	public void DistinctNestedBodyOwners_AtSameParentPosition_DoNotShareStack()
	{
		var host = new ReplacingNestedNavigationHost();
		NavigationTransferNode? transferNode = null;
		CometBackendBridge.Materialize(
			host,
			view => view is NavigationView navigation
				? transferNode ??= new NavigationTransferNode(navigation)
				: new FakeBackendNode(view.GetType().Name),
			Ctx);

		var firstNavigation = Assert.IsType<NavigationView>(host.GetView());
		var detail = new CountingContentPage
		{
			Content = new Text("first owner detail"),
		};
		firstNavigation.Navigate(detail);

		host.ShowSecond = true;
		host.Reload();

		var secondNavigation = Assert.IsType<NavigationView>(host.GetView());
		Assert.Equal(
			NavigationOwnerTransferAction.SwitchStack,
			transferNode!.LastTransferAction);
		Assert.Equal(
			new View[] { secondNavigation.Content! },
			transferNode.Stack);
		Assert.DoesNotContain(detail, transferNode.Stack);
		Assert.Equal(0, detail.DisposeCount);

		host.Dispose();
		Assert.Equal(1, detail.DisposeCount);
		Assert.True(secondNavigation.IsDisposed);
	}

	[Fact]
	public void FreshSiblingNavigationPositions_PreserveOnlyTheirOwnStacks()
	{
		var component = new SiblingNavigationRerenderComponent();
		var transferNodes = new List<NavigationTransferNode>();
		CometBackendBridge.Materialize(
			component,
			view =>
			{
				if (view is NavigationView navigation)
				{
					var node = new NavigationTransferNode(navigation);
					transferNodes.Add(node);
					return node;
				}

				return new FakeBackendNode(view.GetType().Name);
			},
			Ctx);

		var original = Assert.IsType<Grid>(component.GetView());
		var originalLeft = Assert.IsType<NavigationView>(original[0]);
		var originalRight = Assert.IsType<NavigationView>(original[1]);
		var leftDetail = new CountingContentPage
		{
			Content = new Text("left detail"),
		};
		var rightDetail = new CountingContentPage
		{
			Content = new Text("right detail"),
		};
		originalLeft.Navigate(leftDetail);
		originalRight.Navigate(rightDetail);

		component.Version++;
		component.Reload();

		var replacement = Assert.IsType<Grid>(component.GetView());
		var replacementLeft = Assert.IsType<NavigationView>(replacement[0]);
		var replacementRight = Assert.IsType<NavigationView>(replacement[1]);

		Assert.Equal(2, transferNodes.Count);
		Assert.All(
			transferNodes,
			node => Assert.Equal(
				NavigationOwnerTransferAction.PreserveStack,
				node.LastTransferAction));
		Assert.Equal(
			new View[] { replacementLeft.Content!, leftDetail },
			transferNodes[0].Stack);
		Assert.Equal(
			new View[] { replacementRight.Content!, rightDetail },
			transferNodes[1].Stack);
		Assert.Same(replacementLeft, leftDetail.Navigation);
		Assert.Same(replacementRight, rightDetail.Navigation);
		Assert.Equal(0, leftDetail.DisposeCount);
		Assert.Equal(0, rightDetail.DisposeCount);

		component.Dispose();
		Assert.Equal(1, leftDetail.DisposeCount);
		Assert.Equal(1, rightDetail.DisposeCount);
	}

	[Fact]
	public void DirectContent_AssignsNavigationOwnerInitiallyAndAfterReplacement()
	{
		var root = new ContentView { Content = new Text("root child") };
		var navigation = new NavigationView { Content = root }.Key("navigation");
		var initialNavigateCalls = 0;
		navigation.SetPerformNavigate(_ => initialNavigateCalls++);

		root.Navigation!.Navigate(new Text("initial destination"));

		Assert.Same(navigation, root.Navigation);
		Assert.Same(navigation, root.Content.Navigation);
		Assert.Equal(1, initialNavigateCalls);

		var replacementRoot = new ContentView { Content = new Text("replacement child") };
		var replacement = new NavigationView { Content = replacementRoot }.Key("navigation");
		replacement.SetBackendNavigationStack(new[] { replacementRoot });
		var replacementNavigateCalls = 0;
		replacement.SetPerformNavigate(_ => replacementNavigateCalls++);

		replacementRoot.Navigation!.Navigate(new Text("replacement destination"));

		Assert.Same(replacement, replacementRoot.Navigation);
		Assert.Same(replacement, replacementRoot.Content.Navigation);
		Assert.Equal(1, replacementNavigateCalls);
	}

	[Fact]
	public void NestedNavigationViews_RetainIndependentOwnership()
	{
		var nestedRoot = new ContentView { Content = new Text("nested root child") };
		var nestedDetail = new ContentView { Content = new Text("nested detail child") };
		var nestedNavigation = new NavigationView { Content = nestedRoot };
		nestedNavigation.SetBackendNavigationStack(new[] { nestedRoot, nestedDetail });

		var outerDetail = new VStack
		{
			new Text("outer detail"),
			nestedNavigation,
		};
		var outerRoot = new Text("outer root").Key("root");
		var replacement = new NavigationView { Content = outerRoot }.Key("outer");
		replacement.SetBackendNavigationStack(new View[] { outerRoot, outerDetail });

		Assert.Same(replacement, outerDetail.Navigation);
		Assert.Same(replacement, nestedNavigation.Navigation);
		Assert.Same(nestedNavigation, nestedRoot.Navigation);
		Assert.Same(nestedNavigation, nestedRoot.Content.Navigation);
		Assert.Same(nestedNavigation, nestedDetail.Navigation);
		Assert.Same(nestedNavigation, nestedDetail.Content.Navigation);

		var outerPopCalls = 0;
		var nestedPopCalls = 0;
		replacement.SetPerformPop(() => outerPopCalls++);
		nestedNavigation.SetPerformPop(() => nestedPopCalls++);

		nestedDetail.Navigation!.Pop();

		Assert.Equal(0, outerPopCalls);
		Assert.Equal(1, nestedPopCalls);
	}

	[Fact]
	public void PersistentUnkeyedSections_KeepIndependentStacksAcrossReactivation()
	{
		var activityRoot = new Text("activity");
		var activityDetail = new ContentView { Content = new Text("shot") };
		var activity = new NavigationView { Content = activityRoot };
		activity.SetBackendNavigationStack(new View[] { activityRoot, activityDetail });
		var activityStack = activity.GetBackendNavigationStack().ToArray();
		activity.DetachBackendCallbacks();

		var settingsRoot = new Text("settings");
		var settingsDetail = new ContentView { Content = new Text("equipment") };
		var settings = new NavigationView { Content = settingsRoot };
		settings.SetBackendNavigationStack(new View[] { settingsRoot, settingsDetail });
		var settingsStack = settings.GetBackendNavigationStack().ToArray();
		settings.DetachBackendCallbacks();

		activity.SetBackendNavigationStack(activityStack);
		var activityPopCalls = 0;
		var activityNavigateCalls = 0;
		activity.SetPerformPop(() => activityPopCalls++);
		activity.SetPerformNavigate(_ => activityNavigateCalls++);

		activityDetail.Navigation!.Pop();
		activityDetail.Navigation.Navigate(new Text("activity next"));

		Assert.Equal(1, activityPopCalls);
		Assert.Equal(1, activityNavigateCalls);
		Assert.Equal(settingsStack, settings.GetBackendNavigationStack());
		Assert.Same(settings, settingsDetail.Navigation);
		Assert.Same(settings, settingsDetail.Content.Navigation);
	}

	[Fact]
	public void NullRootAndPopToRoot_PreserveRootOwnership()
	{
		var empty = new NavigationView();
		empty.SetBackendNavigationStack(Array.Empty<View>());
		empty.PopToRoot();

		Assert.Null(empty.Content);
		Assert.Empty(empty.GetBackendNavigationStack());

		var root = new ContentView { Content = new Text("root child") };
		var detail = new Text("detail");
		var navigation = new NavigationView { Content = root };
		navigation.SetBackendNavigationStack(new View[] { root, detail });

		navigation.PopToRoot();

		Assert.Equal(new View[] { root }, navigation.GetBackendNavigationStack());
		Assert.Same(navigation, root.Navigation);
		Assert.Same(navigation, root.Content.Navigation);
	}
}
