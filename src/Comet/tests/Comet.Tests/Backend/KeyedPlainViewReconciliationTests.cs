#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comet.Backend;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend;

public class KeyedPlainViewReconciliationTests
{
	static KeyedPlainViewReconciliationTests()
		=> ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

	sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}

	sealed class InspectingOwnContentNode : FakeBackendNode, IBackendManagesOwnContent, ICometBackendNode
	{
		readonly Action<View>? _ownerChanged;

		public InspectingOwnContentNode(Action<View>? ownerChanged = null) : base("owned")
			=> _ownerChanged = ownerChanged;

		public View? Owner { get; private set; }

		void ICometBackendNode.OnOwnerViewChanged(View newView, bool isHotReload)
		{
			Owner = newView;
			_ownerChanged?.Invoke(newView);
			base.OnOwnerViewChanged(newView, isHotReload);
		}
	}

	sealed class InspectingReconciledContentNode : FakeBackendNode, IBackendReconcilesOwnContent, ICometBackendNode
	{
		readonly Action<View>? _ownerChanged;

		public InspectingReconciledContentNode(Action<View>? ownerChanged = null) : base("scroll")
			=> _ownerChanged = ownerChanged;

		public View? Owner { get; private set; }

		void ICometBackendNode.OnOwnerViewChanged(View newView, bool isHotReload)
		{
			Owner = newView;
			_ownerChanged?.Invoke(newView);
			base.OnOwnerViewChanged(newView, isHotReload);
		}
	}

	sealed class InspectingRetainedContentNode : FakeBackendNode, IBackendRetainsLogicalContentOnOwnerTransfer, ICometBackendNode
	{
		readonly Action<View>? _ownerChanged;

		public InspectingRetainedContentNode(Action<View>? ownerChanged = null) : base("retained-owned")
			=> _ownerChanged = ownerChanged;

		public View? Owner { get; private set; }

		void ICometBackendNode.OnOwnerViewChanged(View newView, bool isHotReload)
		{
			Owner = newView;
			_ownerChanged?.Invoke(newView);
			base.OnOwnerViewChanged(newView, isHotReload);
		}
	}

	sealed class TrackingText : Text
	{
		public TrackingText(string value) : base(value) { }

		public int DisposeCount { get; private set; }

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				DisposeCount++;
			base.Dispose(disposing);
		}
	}

	sealed class NullableBackgroundView : View
	{
		PropertySubscription<Color?>? _color;

		public PropertySubscription<Color?>? Color
		{
			get => _color;
			set => this.SetPropertySubscription(ref _color, value);
		}

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			if (Color?.CurrentValue is { } color)
				node.ApplyProperty(PropertyIds.BackgroundColor, PropertyValue.From(color));
		}
	}

	static readonly BackendContext Ctx = new(new EmptyServiceProvider());

	[Fact]
	public void KeyedText_RetainsNativeIdentityWhileReplacingDeclaration()
	{
		var oldTapCount = 0;
		var newTapCount = 0;
		var oldText = new Text("old")
			.AutomationId("old-id")
			.Color(Colors.Red)
			.OnTap(_ => oldTapCount++)
			.Key("stable");
		var oldRoot = new VStack { oldText };
		var rootNode = (FakeBackendNode)CometBackendBridge.Materialize(
			oldRoot,
			view => new FakeBackendNode(view.GetType().Name),
			Ctx);
		var retainedNode = Assert.IsType<FakeBackendNode>(oldText.Node);

		var replacementText = new Text("new")
			.Color(Colors.Blue)
			.OnTap(_ => newTapCount++)
			.Key("stable");
		var replacementRoot = new VStack { replacementText };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		var retainedText = Assert.IsType<Text>(replacementRoot[0]);
		Assert.Same(replacementText, retainedText);
		Assert.Same(retainedNode, retainedText.Node);
		Assert.Same(retainedNode, rootNode.Children[0]);
		Assert.Equal("stable", retainedText.GetKey());
		Assert.Equal("new", retainedText.Value.CurrentValue);
		Assert.Null(retainedText.AutomationId);
		Assert.Equal(Colors.Blue, retainedText.GetEnvironment<Color>(EnvironmentKeys.Colors.Color));
		Assert.Equal("new", retainedNode.Get(PropertyIds.Text_Value).AsString);
		Assert.Equal(string.Empty, retainedNode.Get(PropertyIds.AutomationId).AsString);
		Assert.Equal(Colors.Blue, retainedNode.Get(PropertyIds.Text_Color).AsColor);
		Assert.True(oldText.IsDisposed);
		Assert.False(replacementText.IsDisposed);

		retainedNode.Sink!.OnGesture(
			GestureKind.Tap,
			new GestureData(GestureState.Ended, default));
		Assert.Equal(0, oldTapCount);
		Assert.Equal(1, newTapCount);

		replacementRoot.Dispose();
	}

	[Fact]
	public void KeyedOwnedContainer_TransfersSlotsWithoutGenericChildDiff()
	{
		var oldOpen = new Signal<bool>(true);
		var newOpen = new Signal<bool>(true);
		var oldText = new TrackingText("old");
		var oldConfirm = new TrackingText("old-confirm");
		var oldDialog = new AlertDialog(oldOpen, oldText, oldConfirm).Key("dialog");
		var oldRoot = new VStack { oldDialog };
		var created = new List<FakeBackendNode>();
		AlertDialog? ownerObservedByNode = null;
		ICometBackendNode Factory(View view)
		{
			FakeBackendNode node = view is AlertDialog
				? new InspectingOwnContentNode(owner =>
				{
					var dialog = Assert.IsType<AlertDialog>(owner);
					Assert.Equal("new", Assert.IsAssignableFrom<Text>(dialog.Text).Value.CurrentValue);
					Assert.Equal("new-confirm", Assert.IsAssignableFrom<Text>(dialog.ConfirmButton).Value.CurrentValue);
					ownerObservedByNode = dialog;
				})
				: new FakeBackendNode(view.GetType().Name);
			created.Add(node);
			return node;
		}

		CometBackendBridge.Materialize(oldRoot, Factory, Ctx);
		var retainedNode = Assert.IsType<InspectingOwnContentNode>(oldDialog.Node);
		Assert.Equal(2, created.Count);

		var newText = new TrackingText("new");
		var newConfirm = new TrackingText("new-confirm");
		var replacementDialog = new AlertDialog(newOpen, newText, newConfirm).Key("dialog");
		var replacementRoot = new VStack { replacementDialog };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		Assert.Same(replacementDialog, replacementRoot[0]);
		Assert.Same(retainedNode, replacementDialog.Node);
		Assert.Same(replacementDialog, ownerObservedByNode);
		Assert.Same(newText, replacementDialog.Text);
		Assert.Same(newConfirm, replacementDialog.ConfirmButton);
		Assert.True(oldDialog.IsDisposed);
		Assert.True(oldText.IsDisposed);
		Assert.True(oldConfirm.IsDisposed);
		Assert.False(newText.IsDisposed);
		Assert.False(newConfirm.IsDisposed);
		Assert.False(replacementDialog.IsDisposed);
		Assert.Equal(1, retainedNode.OwnerChangedCount);
		Assert.Equal(2, created.Count);
		Assert.Equal(0, PropertyChangedSubscriberCount(oldOpen));
		Assert.Equal(1, PropertyChangedSubscriberCount(newOpen));

		replacementRoot.Dispose();
		Assert.Equal(1, newText.DisposeCount);
		Assert.Equal(1, newConfirm.DisposeCount);
		Assert.Equal(0, PropertyChangedSubscriberCount(newOpen));
	}

	[Fact]
	public void KeyedNavigationView_ReconcilesRootBeforeTransferringOwner()
	{
		var oldContent = new TrackingText("old");
		var oldNavigation = new NavigationView { Content = oldContent }.Key("navigation");
		var oldRoot = new VStack { oldNavigation };
		NavigationView? ownerObservedByNode = null;
		var retainedNode = new InspectingRetainedContentNode(owner =>
		{
			var navigation = Assert.IsType<NavigationView>(owner);
			Assert.Equal("new", Assert.IsAssignableFrom<Text>(navigation.Content).Value.CurrentValue);
			Assert.NotNull(navigation.Content.Node);
			ownerObservedByNode = navigation;
		});
		CometBackendBridge.Materialize(
			oldRoot,
			view => view is NavigationView
				? retainedNode
				: new FakeBackendNode(view.GetType().Name),
			Ctx);
		CometBackendBridge.MaterializeChild(oldContent, oldNavigation);
		var retainedContentNode = Assert.IsType<FakeBackendNode>(oldContent.Node);
		var replacementContent = new TrackingText("new");
		var replacementNavigation = new NavigationView { Content = replacementContent }
			.Key("navigation");
		var replacementRoot = new VStack { replacementNavigation };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		Assert.Same(replacementNavigation, replacementRoot[0]);
		Assert.Same(replacementNavigation, ownerObservedByNode);
		Assert.Same(retainedNode, replacementNavigation.Node);
		Assert.Same(retainedContentNode, replacementContent.Node);
		Assert.True(oldNavigation.IsDisposed);
		Assert.Equal(1, oldContent.DisposeCount);
		Assert.False(replacementContent.IsDisposed);
		Assert.Equal(1, retainedNode.OwnerChangedCount);

		replacementRoot.Dispose();
		Assert.Equal(1, replacementContent.DisposeCount);
	}

	[Fact]
	public void KeyedTabView_TransfersNodeToCompleteReplacementPayload()
	{
		var oldTab = new TabView();
		var oldFirst = new TrackingText("old");
		oldTab.AddTab("Old", oldFirst, "old-icon");
		oldTab.Key("tabs");
		var oldRoot = new VStack { oldTab };
		TabView? ownerObservedByNode = null;
		var retainedNode = new InspectingOwnContentNode(owner =>
		{
			var tab = Assert.IsType<TabView>(owner);
			Assert.Equal(new[] { "New first", "New second" }, tab.Tabs.Select(item => item.Title));
			Assert.Equal(2, tab.Count);
			Assert.Equal(1, tab.SelectedIndex);
			ownerObservedByNode = tab;
		});
		CometBackendBridge.Materialize(
			oldRoot,
			view => view is TabView
				? retainedNode
				: new FakeBackendNode(view.GetType().Name),
			Ctx);
		var oldSelected = oldTab.SelectedSignal;
		Assert.Equal(1, PropertyChangedSubscriberCount(oldSelected));

		var replacementTab = new TabView();
		var newFirst = new TrackingText("new-first");
		var newSecond = new TrackingText("new-second");
		replacementTab.AddTab("New first", newFirst, "first-icon");
		replacementTab.AddTab("New second", newSecond, "second-icon");
		replacementTab.SelectedIndex = 1;
		replacementTab.Key("tabs");
		var replacementRoot = new VStack { replacementTab };
		var newSelected = replacementTab.SelectedSignal;

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		Assert.Same(replacementTab, replacementRoot[0]);
		Assert.Same(replacementTab, ownerObservedByNode);
		Assert.Same(replacementTab, retainedNode.Owner);
		Assert.Same(retainedNode, replacementTab.Node);
		Assert.True(oldTab.IsDisposed);
		Assert.True(oldFirst.IsDisposed);
		Assert.False(newFirst.IsDisposed);
		Assert.False(newSecond.IsDisposed);
		Assert.Equal(0, PropertyChangedSubscriberCount(oldSelected));
		Assert.Equal(1, PropertyChangedSubscriberCount(newSelected));

		replacementRoot.Dispose();
		Assert.Equal(1, newFirst.DisposeCount);
		Assert.Equal(1, newSecond.DisposeCount);
		Assert.Equal(0, PropertyChangedSubscriberCount(newSelected));
	}

	[Fact]
	public void KeyedTabView_DoesNotDisposeContentClaimedByReplacement()
	{
		var sharedContent = new TrackingText("shared");
		var oldTab = new TabView();
		oldTab.AddTab("Old", sharedContent);
		oldTab.Key("tabs");
		var oldRoot = new VStack { oldTab };
		CometBackendBridge.Materialize(
			oldRoot,
			view => view is TabView
				? new InspectingOwnContentNode()
				: new FakeBackendNode(view.GetType().Name),
			Ctx);

		var replacementTab = new TabView();
		replacementTab.AddTab("New", sharedContent);
		replacementTab.Key("tabs");
		var replacementRoot = new VStack { replacementTab };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		Assert.False(sharedContent.IsDisposed);
		Assert.Equal(0, sharedContent.DisposeCount);
		Assert.Same(replacementTab, sharedContent.Parent);

		replacementRoot.Dispose();
		Assert.Equal(1, sharedContent.DisposeCount);
	}

	[Fact]
	public void KeyedScrollView_ReconcilesContentOnceBeforeOwnerTransfer()
	{
		var oldContent = new TrackingText("old");
		var oldScroll = new ScrollView { oldContent }.Key("scroll");
		var oldRoot = new VStack { oldScroll };
		ScrollView? ownerObservedByNode = null;
		var retainedNode = new InspectingReconciledContentNode(owner =>
		{
			var scroll = Assert.IsType<ScrollView>(owner);
			Assert.Equal("new", Assert.IsAssignableFrom<Text>(scroll.Content).Value.CurrentValue);
			Assert.NotNull(scroll.Content.Node);
			ownerObservedByNode = scroll;
		});
		CometBackendBridge.Materialize(
			oldRoot,
			view => view is ScrollView
				? retainedNode
				: new FakeBackendNode(view.GetType().Name),
			Ctx);
		CometBackendBridge.MaterializeChild(oldContent, oldScroll);
		var retainedContentNode = Assert.IsType<FakeBackendNode>(oldContent.Node);
		var replacementContent = new TrackingText("new");
		var replacementScroll = new ScrollView { replacementContent }.Key("scroll");
		var replacementRoot = new VStack { replacementScroll };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		Assert.Same(replacementScroll, replacementRoot[0]);
		Assert.Same(replacementScroll, ownerObservedByNode);
		Assert.Same(retainedNode, replacementScroll.Node);
		Assert.Same(retainedContentNode, replacementContent.Node);
		Assert.True(oldScroll.IsDisposed);
		Assert.Equal(1, oldContent.DisposeCount);
		Assert.False(replacementContent.IsDisposed);
		Assert.Equal(1, retainedNode.OwnerChangedCount);

		replacementRoot.Dispose();
		Assert.Equal(1, replacementContent.DisposeCount);
	}

	[Fact]
	public void KeyedListView_TransfersNodeToReplacementDataAndDisposesOldSubscription()
	{
		var oldItems = new Signal<IReadOnlyList<int>>(new[] { 1 });
		var newItems = new Signal<IReadOnlyList<int>>(new[] { 2, 3 });
		var oldList = new ListView<int>(PropertySubscription<IReadOnlyList<int>>.FromSignal(oldItems))
		{
			ViewFor = value => new Text($"old-{value}"),
			Horizontal = false,
		}.Key("list");
		var oldRoot = new VStack { oldList };
		var retainedNode = new InspectingOwnContentNode(owner =>
		{
			var list = Assert.IsAssignableFrom<IListView>(owner);
			Assert.Equal(2, list.Rows(0));
			Assert.True(list.Horizontal);
			Assert.Equal("new-2", Assert.IsType<Text>(list.ViewFor(0, 0)).Value.CurrentValue);
		});
		CometBackendBridge.Materialize(
			oldRoot,
			view => view is IListView
				? retainedNode
				: new FakeBackendNode(view.GetType().Name),
			Ctx);
		Assert.Equal(1, ReactiveSubscriberCount(oldItems));

		var replacementList = new ListView<int>(
			PropertySubscription<IReadOnlyList<int>>.FromSignal(newItems))
		{
			ViewFor = value => new Text($"new-{value}"),
			Horizontal = true,
		}.Key("list");
		var replacementRoot = new VStack { replacementList };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		Assert.Same(replacementList, replacementRoot[0]);
		Assert.Same(replacementList, retainedNode.Owner);
		Assert.Same(retainedNode, replacementList.Node);
		Assert.True(oldList.IsDisposed);
		Assert.Equal(0, ReactiveSubscriberCount(oldItems));
		Assert.Equal(1, ReactiveSubscriberCount(newItems));

		replacementRoot.Dispose();
		Assert.Equal(0, ReactiveSubscriberCount(newItems));
	}

	[Fact]
	public void KeyedPicker_UsesReplacementSubscriptionAndDisposesOldSubscription()
	{
		var oldSelection = new Signal<int>(0);
		var newSelection = new Signal<int>(1);
		var oldPicker = new Picker
		{
			Items = new List<string> { "Old" },
			SelectedIndex = PropertySubscription<int>.FromSignal(oldSelection),
			Title = "Old title",
		}.Key("picker");
		var oldRoot = new VStack { oldPicker };
		CometBackendBridge.Materialize(
			oldRoot,
			view => new FakeBackendNode(view.GetType().Name),
			Ctx);
		var replacementPicker = new Picker
		{
			Items = new List<string> { "New A", "New B" },
			SelectedIndex = PropertySubscription<int>.FromSignal(newSelection),
			Title = "New title",
		}.Key("picker");
		var replacementRoot = new VStack { replacementPicker };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		var retainedPicker = Assert.IsType<Picker>(replacementRoot[0]);
		Assert.Same(replacementPicker, retainedPicker);
		Assert.True(oldPicker.IsDisposed);
		Assert.False(replacementPicker.IsDisposed);
		Assert.Equal(new[] { "New A", "New B" }, retainedPicker.Items.CurrentValue);
		Assert.Equal("New title", retainedPicker.Title.CurrentValue);
		Assert.Equal(1, retainedPicker.SelectedIndex.CurrentValue);
		Assert.Equal(0, ReactiveSubscriberCount(oldSelection));
		Assert.Equal(1, ReactiveSubscriberCount(newSelection));

		newSelection.Value = 0;
		ReactiveScheduler.FlushSync();
		Assert.Equal(0, retainedPicker.SelectedIndex.CurrentValue);

		replacementRoot.Dispose();
		Assert.Equal(0, ReactiveSubscriberCount(newSelection));
	}

	[Fact]
	public void KeyedBoxView_UsesReplacementSubscriptionAndDisposesOldSubscription()
	{
		var oldColor = new Signal<Color>(Colors.Red);
		var newColor = new Signal<Color>(Colors.Blue);
		var oldBox = new BoxView
		{
			Color = PropertySubscription<Color>.FromSignal(oldColor),
		}.Key("box");
		var oldRoot = new VStack { oldBox };
		CometBackendBridge.Materialize(
			oldRoot,
			view => new FakeBackendNode(view.GetType().Name),
			Ctx);
		var replacementBox = new BoxView
		{
			Color = PropertySubscription<Color>.FromSignal(newColor),
		}.Key("box");
		var replacementRoot = new VStack { replacementBox };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		var retainedBox = Assert.IsType<BoxView>(replacementRoot[0]);
		Assert.Same(replacementBox, retainedBox);
		Assert.True(oldBox.IsDisposed);
		Assert.False(replacementBox.IsDisposed);
		Assert.Equal(Colors.Blue, retainedBox.Color.CurrentValue);
		Assert.Equal(0, ReactiveSubscriberCount(oldColor));
		Assert.Equal(1, ReactiveSubscriberCount(newColor));

		newColor.Value = Colors.Green;
		ReactiveScheduler.FlushSync();
		Assert.Equal(Colors.Green, retainedBox.Color.CurrentValue);

		replacementRoot.Dispose();
		Assert.Equal(0, ReactiveSubscriberCount(newColor));
	}

	[Fact]
	public void DeclarativeAdoption_MapsUnderscoreBackedPickerSubscriptions()
	{
		var oldSelection = new Signal<int>(0);
		var newSelection = new Signal<int>(1);
		var retained = new Picker
		{
			Items = new List<string> { "Old" },
			SelectedIndex = PropertySubscription<int>.FromSignal(oldSelection),
			Title = "Old title",
		};
		var replacement = new Picker
		{
			Items = new List<string> { "New A", "New B" },
			SelectedIndex = PropertySubscription<int>.FromSignal(newSelection),
			Title = "New title",
		};

		retained.AdoptRetainedIdentityStateFrom(replacement);
		replacement.Dispose();

		Assert.Equal(new[] { "New A", "New B" }, retained.Items.CurrentValue);
		Assert.Equal("New title", retained.Title.CurrentValue);
		Assert.Equal(1, retained.SelectedIndex.CurrentValue);
		Assert.Equal(0, ReactiveSubscriberCount(oldSelection));
		Assert.Equal(1, ReactiveSubscriberCount(newSelection));

		newSelection.Value = 0;
		ReactiveScheduler.FlushSync();
		Assert.Equal(0, retained.SelectedIndex.CurrentValue);

		retained.Dispose();
		Assert.Equal(0, ReactiveSubscriberCount(newSelection));
	}

	[Fact]
	public void DeclarativeAdoption_MapsUnderscoreBackedBoxViewSubscription()
	{
		var oldColor = new Signal<Color>(Colors.Red);
		var newColor = new Signal<Color>(Colors.Blue);
		var retained = new BoxView
		{
			Color = PropertySubscription<Color>.FromSignal(oldColor),
		};
		var replacement = new BoxView
		{
			Color = PropertySubscription<Color>.FromSignal(newColor),
		};

		retained.AdoptRetainedIdentityStateFrom(replacement);
		replacement.Dispose();

		Assert.Equal(Colors.Blue, retained.Color.CurrentValue);
		Assert.Equal(0, ReactiveSubscriberCount(oldColor));
		Assert.Equal(1, ReactiveSubscriberCount(newColor));

		retained.Dispose();
		Assert.Equal(0, ReactiveSubscriberCount(newColor));
	}

	[Fact]
	public void KeyedText_RemovingExplicitProperties_ResetsBackendDefaults()
	{
		var oldText = new Text("old")
			.Color(Colors.Red)
			.Background(Colors.Blue)
			.Padding(new Thickness(1, 2, 3, 4))
			.Margin(8)
			.Frame(width: 120, height: 44)
			.CornerRadius(12)
			.Elevation(6)
			.Border(2, Colors.Green)
			.FontSize(24)
			.FontFamily("Manrope")
			.FontWeight(FontWeight.Bold)
			.FontSlant(FontSlant.Italic)
			.LineHeight(30)
			.CharacterSpacing(2)
			.MaxLines(2)
			.Opacity(0.4)
			.TranslationX(9)
			.TranslationY(11)
			.ScaleX(1.5)
			.ScaleY(0.75)
			.Rotation(20)
			.RotationX(5)
			.RotationY(7)
			.IsVisible(false)
			.IsEnabled(false)
			.AutomationId("old-id")
			.OnTap(_ => { })
			.Key("text");
		var oldRoot = new VStack { oldText };
		CometBackendBridge.Materialize(
			oldRoot,
			view => new FakeBackendNode(view.GetType().Name),
			Ctx);
		var retainedNode = Assert.IsType<FakeBackendNode>(oldText.Node);
		var replacementText = new Text("new").Key("text");
		var replacementRoot = new VStack { replacementText };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		var retainedText = Assert.IsType<Text>(replacementRoot[0]);
		Assert.Same(replacementText, retainedText);
		Assert.Equal(Thickness.Zero, retainedText.GetMargin());
		Assert.Null(retainedText.GetFrameConstraints());
		Assert.Equal("new", retainedNode.Get(PropertyIds.Text_Value).AsString);
		Assert.Equal(PropertyValueKind.Color, retainedNode.Get(PropertyIds.Text_Color).Kind);
		Assert.Null(retainedNode.Get(PropertyIds.Text_Color).AsColor);
		Assert.Equal(PropertyValueKind.Color, retainedNode.Get(PropertyIds.BackgroundColor).Kind);
		Assert.Null(retainedNode.Get(PropertyIds.BackgroundColor).AsColor);
		Assert.Equal(PropertyValueKind.Object, retainedNode.Get(PropertyIds.Padding).Kind);
		Assert.Equal(default(Thickness), retainedNode.Get(PropertyIds.Padding).AsObject);
		Assert.Equal(PropertyValueKind.Object, retainedNode.Get(PropertyIds.Border).Kind);
		Assert.Null(retainedNode.Get(PropertyIds.Border).AsObject);
		Assert.Equal(0d, retainedNode.Get(PropertyIds.Shadow).AsDouble);
		Assert.Equal(default(CornerRadii), retainedNode.Get(PropertyIds.CornerRadius).AsObject);
		Assert.Equal(1d, retainedNode.Get(PropertyIds.Opacity).AsDouble);
		Assert.Equal(0d, retainedNode.Get(PropertyIds.TranslationX).AsDouble);
		Assert.Equal(0d, retainedNode.Get(PropertyIds.TranslationY).AsDouble);
		Assert.Equal(1d, retainedNode.Get(PropertyIds.ScaleX).AsDouble);
		Assert.Equal(1d, retainedNode.Get(PropertyIds.ScaleY).AsDouble);
		Assert.Equal(0d, retainedNode.Get(PropertyIds.Rotation).AsDouble);
		Assert.Equal(0d, retainedNode.Get(PropertyIds.RotationX).AsDouble);
		Assert.Equal(0d, retainedNode.Get(PropertyIds.RotationY).AsDouble);
		Assert.True(retainedNode.Get(PropertyIds.IsVisible).AsBool);
		Assert.True(retainedNode.Get(PropertyIds.IsEnabled).AsBool);
		Assert.Equal(string.Empty, retainedNode.Get(PropertyIds.AutomationId).AsString);
		Assert.False(retainedNode.Get(PropertyIds.HasTapGesture).AsBool);
		Assert.Equal(0d, retainedNode.Get(PropertyIds.Text_FontSize).AsDouble);
		Assert.Equal(string.Empty, retainedNode.Get(PropertyIds.Text_FontFamily).AsString);
		Assert.Equal(0, retainedNode.Get(PropertyIds.Text_FontWeight).AsInt);
		Assert.False(retainedNode.Get(PropertyIds.Text_Italic).AsBool);
		Assert.Equal(0d, retainedNode.Get(PropertyIds.Text_LineHeight).AsDouble);
		Assert.Equal(0d, retainedNode.Get(PropertyIds.Text_CharacterSpacing).AsDouble);
		Assert.Equal(0, retainedNode.Get(PropertyIds.Text_MaxLines).AsInt);

		replacementRoot.Dispose();
	}

	[Fact]
	public void KeyedButton_RemovingControlSpecificProperties_ResetsDefaults()
	{
		var oldButton = new Button("old", () => { })
			.Color(Colors.Red)
			.Outlined()
			.TextButton()
			.Key("button");
		var oldRoot = new VStack { oldButton };
		CometBackendBridge.Materialize(
			oldRoot,
			view => new FakeBackendNode(view.GetType().Name),
			Ctx);
		var retainedNode = Assert.IsType<FakeBackendNode>(oldButton.Node);
		var replacementRoot = new VStack
		{
			new Button("new", () => { }).Key("button"),
		};

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		Assert.Equal("new", retainedNode.Get(PropertyIds.Button_Text).AsString);
		Assert.Equal(PropertyValueKind.Color, retainedNode.Get(PropertyIds.Button_TextColor).Kind);
		Assert.Null(retainedNode.Get(PropertyIds.Button_TextColor).AsColor);
		Assert.False(retainedNode.Get(PropertyIds.Button_Outlined).AsBool);
		Assert.False(retainedNode.Get(PropertyIds.Button_TextButton).AsBool);

		replacementRoot.Dispose();
	}

	[Fact]
	public void SignalBackedNullableProperty_RemovalClearsRetainedBackendState()
	{
		var color = new Signal<Color?>(Colors.Red);
		var view = new NullableBackgroundView
		{
			Color = PropertySubscription<Color?>.FromSignal(color),
		};
		var node = Assert.IsType<FakeBackendNode>(CometBackendBridge.Materialize(
			view,
			owner => new FakeBackendNode(owner.GetType().Name),
			Ctx));
		Assert.Equal(Colors.Red, node.Get(PropertyIds.BackgroundColor).AsColor);

		color.Value = null;
		ReactiveScheduler.FlushSync();

		var cleared = node.Get(PropertyIds.BackgroundColor);
		Assert.Equal(PropertyValueKind.Color, cleared.Kind);
		Assert.Null(cleared.AsColor);

		view.Dispose();
		Assert.Equal(0, ReactiveSubscriberCount(color));
	}

	[Fact]
	public void KeyedReplacement_PreservesUntrackedNativeState()
	{
		var oldText = new Text("old").Key("stateful");
		var oldRoot = new VStack { oldText };
		CometBackendBridge.Materialize(
			oldRoot,
			view => new FakeBackendNode(view.GetType().Name),
			Ctx);
		var retainedNode = Assert.IsType<FakeBackendNode>(oldText.Node);
		var nativeOnly = new PropertyId(ushort.MaxValue);
		retainedNode.ApplyProperty(nativeOnly, PropertyValue.From(42));
		var replacementText = new Text("new").Key("stateful");
		var replacementRoot = new VStack { replacementText };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		Assert.Same(retainedNode, replacementText.Node);
		Assert.Equal(42, retainedNode.Get(nativeOnly).AsInt);

		replacementRoot.Dispose();
	}

	[Fact]
	public void RemovedPropertyResetsBeforeNewPropertyReplay()
	{
		var oldCard = new VStack().AsCard().Key("surface");
		var oldRoot = new VStack { oldCard };
		CometBackendBridge.Materialize(
			oldRoot,
			view => new FakeBackendNode(view.GetType().Name),
			Ctx);
		var retainedNode = Assert.IsType<FakeBackendNode>(oldCard.Node);
		retainedNode.Log.Clear();
		var replacementCard = new VStack().Elevation(5).Key("surface");
		var replacementRoot = new VStack { replacementCard };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		var cardReset = retainedNode.Log.FindIndex(
			entry => entry == $"apply {PropertyIds.Container_Card.Value}=Bool(False)");
		var shadowSet = retainedNode.Log.FindIndex(
			entry => entry == $"apply {PropertyIds.Shadow.Value}=Double(5)");
		Assert.True(cardReset >= 0);
		Assert.True(shadowSet > cardReset);

		replacementRoot.Dispose();
	}

	[Fact]
	public void BackendPropertyDefaults_CoverEveryRegisteredProperty()
	{
		var ids = typeof(PropertyIds)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.FieldType == typeof(PropertyId))
			.Select(field => (PropertyId)field.GetValue(null)!);

		foreach (var id in ids)
			_ = BackendPropertyDefaults.Get(id);
	}

	[Fact]
	public void KeyedButton_ReplacesLogicalViewAndGeneratedCallback()
	{
		var oldClickCount = 0;
		var newClickCount = 0;
		var oldButton = new Button("old", () => oldClickCount++).Key("action");
		var oldRoot = new VStack { oldButton };
		CometBackendBridge.Materialize(
			oldRoot,
			view => new FakeBackendNode(view.GetType().Name),
			Ctx);
		var retainedNode = Assert.IsType<FakeBackendNode>(oldButton.Node);
		var replacementButton = new Button("new", () => newClickCount++).Key("action");
		var replacementRoot = new VStack { replacementButton };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		var retainedButton = Assert.IsType<Button>(replacementRoot[0]);
		Assert.Same(replacementButton, retainedButton);
		Assert.Same(retainedNode, retainedButton.Node);
		Assert.Equal("new", retainedButton.Text.CurrentValue);
		Assert.True(oldButton.IsDisposed);
		Assert.False(replacementButton.IsDisposed);

		retainedNode.Sink!.OnEvent(EventIds.Clicked);
		Assert.Equal(0, oldClickCount);
		Assert.Equal(1, newClickCount);

		replacementRoot.Dispose();
	}

	[Fact]
	public void KeyedText_TransfersNodeToReplacementPropertySubscription()
	{
		var oldValue = new Signal<string>("old");
		var newValue = new Signal<string>("new");
		var oldText = new Text(oldValue).Key("reactive");
		var oldRoot = new VStack { oldText };
		CometBackendBridge.Materialize(
			oldRoot,
			view => new FakeBackendNode(view.GetType().Name),
			Ctx);
		var retainedNode = Assert.IsType<FakeBackendNode>(oldText.Node);
		var replacementRoot = new VStack
		{
			new Text(newValue).Key("reactive"),
		};

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();
		newValue.Value = "updated";

		var retainedText = Assert.IsType<Text>(replacementRoot[0]);
		Assert.Equal("updated", retainedText.Value.CurrentValue);
		Assert.Equal("updated", retainedNode.Get(PropertyIds.Text_Value).AsString);
		Assert.True(oldText.IsDisposed);

		oldValue.Value = "obsolete";
		Assert.Equal("updated", retainedText.Value.CurrentValue);
		replacementRoot.Dispose();
	}

	[Fact]
	public void KeyedContainer_TransfersRootNodeAndReconcilesChildren()
	{
		var oldChild = new TrackingText("old");
		var oldGrid = new Grid { oldChild }.Key("container");
		var oldRoot = new VStack { oldGrid };
		CometBackendBridge.Materialize(
			oldRoot,
			view => new FakeBackendNode(view.GetType().Name),
			Ctx);
		var retainedGridNode = Assert.IsType<FakeBackendNode>(oldGrid.Node);
		var replacementChild = new TrackingText("new");
		var replacementGrid = new Grid { replacementChild }.Key("container");
		var replacementRoot = new VStack { replacementGrid };

		replacementRoot.Diff(oldRoot, false);
		oldRoot.Dispose();

		var retainedGrid = Assert.IsType<Grid>(replacementRoot[0]);
		Assert.Same(replacementGrid, retainedGrid);
		Assert.Same(retainedGridNode, retainedGrid.Node);
		Assert.Single(retainedGrid);
		Assert.Same(replacementChild, retainedGrid[0]);
		Assert.Equal("new", retainedGridNode.Children[0].Get(PropertyIds.Text_Value).AsString);
		Assert.True(oldChild.IsDisposed);
		Assert.Equal(1, oldChild.DisposeCount);
		Assert.True(oldGrid.IsDisposed);
		Assert.False(replacementGrid.IsDisposed);
		Assert.False(replacementChild.IsDisposed);

		replacementRoot.Dispose();
		Assert.Equal(1, replacementChild.DisposeCount);
	}

	static int PropertyChangedSubscriberCount<T>(Signal<T> signal)
	{
		var field = typeof(Signal<T>).GetField(
			nameof(Signal<T>.PropertyChanged),
			BindingFlags.Instance | BindingFlags.NonPublic);
		var handlers = field?.GetValue(signal) as Delegate;
		return handlers?.GetInvocationList().Length ?? 0;
	}

	static int ReactiveSubscriberCount<T>(Signal<T> signal)
	{
		var subscribers = typeof(Signal<T>).GetField(
			"_subscribers",
			BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(signal)!;
		return (int)subscribers.GetType().GetField(
			"_count",
			BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(subscribers)!;
	}
}
