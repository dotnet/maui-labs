using System.Windows;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Platforms.Windows.WPF;
using MauiPoint = Microsoft.Maui.Graphics.Point;
using WBorder = System.Windows.Controls.Border;
using WCanvas = System.Windows.Controls.Canvas;

namespace HandlerTests;

public class GestureManagerTests
{
	[Theory]
	[InlineData(DataPackageOperation.None, DragDropEffects.None)]
	[InlineData(DataPackageOperation.Copy, DragDropEffects.Copy)]
	[InlineData(DataPackageOperation.Move, DragDropEffects.Move)]
	[InlineData(DataPackageOperation.Copy | DataPackageOperation.Move, DragDropEffects.Copy | DragDropEffects.Move)]
	public void DataPackageOperation_RoundTripsThroughDragDropEffects(DataPackageOperation operation, DragDropEffects effects)
	{
		Assert.Equal(effects, GestureManager.ToDragDropEffects(operation));
		Assert.Equal(operation, GestureManager.ToDataPackageOperation(effects));
	}

	[Fact]
	public void DragDropEffects_WithoutMauiEquivalent_AreDropped()
	{
		Assert.Equal(DataPackageOperation.Move,
			GestureManager.ToDataPackageOperation(DragDropEffects.Move | DragDropEffects.Link | DragDropEffects.Scroll));
		Assert.Equal(DataPackageOperation.Copy | DataPackageOperation.Move,
			GestureManager.ToDataPackageOperation(DragDropEffects.All));
	}

	[Theory]
	[InlineData(DataPackageOperation.Move, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.Move)]
	[InlineData(DataPackageOperation.Copy, DragDropEffects.Move, DragDropEffects.None)]
	[InlineData(DataPackageOperation.Copy | DataPackageOperation.Move, DragDropEffects.Move, DragDropEffects.Move)]
	[InlineData(DataPackageOperation.Copy | DataPackageOperation.Move, DragDropEffects.All, DragDropEffects.Copy | DragDropEffects.Move)]
	[InlineData(DataPackageOperation.None, DragDropEffects.All, DragDropEffects.None)]
	public void NegotiateEffects_LimitsAcceptedOperationToSourceEffects(
		DataPackageOperation accepted, DragDropEffects allowed, DragDropEffects expected)
	{
		Assert.Equal(expected, GestureManager.NegotiateEffects(accepted, allowed));
	}

	[Fact]
	public void DropRecognizer_AllowDropChanges_ReachPlatformView_UntilGesturesAreCleared()
	{
		StaHelper.RunOnSta(() =>
		{
			var platformView = new WBorder();
			var mauiView = new Grid();
			var drop = new DropGestureRecognizer { AllowDrop = false };
			mauiView.GestureRecognizers.Add(drop);

			GestureManager.SetupGestures(platformView, mauiView);
			Assert.False(platformView.AllowDrop);

			drop.AllowDrop = true;
			Assert.True(platformView.AllowDrop);

			drop.AllowDrop = false;
			Assert.False(platformView.AllowDrop);

			drop.AllowDrop = true;
			mauiView.GestureRecognizers.Clear();
			GestureManager.SetupGestures(platformView, mauiView);
			Assert.False(platformView.AllowDrop);

			// The detached recognizer no longer drives the platform view.
			drop.AllowDrop = false;
			drop.AllowDrop = true;
			Assert.False(platformView.AllowDrop);
		});
	}

	[Fact]
	public void ResolvePosition_NullIsRootRelative_ElementsResolveThroughHandlers_UnresolvedIsNull()
	{
		StaHelper.RunOnSta(() =>
		{
			var recognizing = new WBorder { Width = 100, Height = 50 };
			var sibling = new WBorder { Width = 40, Height = 40 };
			var root = new WCanvas { Width = 400, Height = 300 };
			WCanvas.SetLeft(recognizing, 30);
			WCanvas.SetTop(recognizing, 20);
			WCanvas.SetLeft(sibling, 200);
			WCanvas.SetTop(sibling, 100);
			root.Children.Add(recognizing);
			root.Children.Add(sibling);
			root.Measure(new System.Windows.Size(400, 300));
			root.Arrange(new System.Windows.Rect(0, 0, 400, 300));

			var siblingElement = new Label { Handler = new StubPlatformHandler(sibling) };
			var recognizingElement = new Label { Handler = new StubPlatformHandler(recognizing) };
			var positionInView = new System.Windows.Point(10, 5);

			Assert.Equal(new MauiPoint(40, 25), GestureManager.ResolvePosition(recognizing, positionInView, null));
			Assert.Equal(new MauiPoint(10, 5), GestureManager.ResolvePosition(recognizing, positionInView, recognizingElement));
			Assert.Equal(new MauiPoint(-160, -75), GestureManager.ResolvePosition(recognizing, positionInView, siblingElement));
			Assert.Null(GestureManager.ResolvePosition(recognizing, positionInView, new Label()));
			Assert.Null(GestureManager.ResolvePosition(recognizing, positionInView,
				new Label { Handler = new StubPlatformHandler(new WBorder()) }));
		});
	}

	[Fact]
	public void SelectTapRecognizer_FallsBackPastDisabledCommands()
	{
		var disabled = new TapGestureRecognizer { Command = new Command(() => { }, () => false) };
		var enabled = new TapGestureRecognizer { Command = new Command(() => { }) };
		var eventOnly = new TapGestureRecognizer();

		Assert.Same(enabled, CollectionViewHandler.SelectTapRecognizer([disabled, enabled, eventOnly]));
		Assert.Same(eventOnly, CollectionViewHandler.SelectTapRecognizer([disabled, eventOnly, enabled]));
		Assert.Same(disabled, CollectionViewHandler.SelectTapRecognizer([disabled]));
		Assert.Null(CollectionViewHandler.SelectTapRecognizer([]));
	}

	[Fact]
	public void SelectTapRecognizer_HonorsCommandParameter()
	{
		var rejectsParameter = new TapGestureRecognizer
		{
			Command = new Command<string>(_ => { }, value => value == "go"),
			CommandParameter = "stop"
		};
		var acceptsParameter = new TapGestureRecognizer
		{
			Command = new Command<string>(_ => { }, value => value == "go"),
			CommandParameter = "go"
		};

		Assert.Same(acceptsParameter, CollectionViewHandler.SelectTapRecognizer([rejectsParameter, acceptsParameter]));
	}

	sealed class StubPlatformHandler(object platformView) : IViewHandler
	{
		public object? PlatformView { get; } = platformView;
		public IView? VirtualView { get; private set; }
		public bool HasContainer { get; set; }
		public object? ContainerView => null;
		public IMauiContext? MauiContext => null;
		IElement? IElementHandler.VirtualView => VirtualView;
		public void SetMauiContext(IMauiContext mauiContext) { }
		public void SetVirtualView(IElement view) => VirtualView = (IView)view;
		public void UpdateValue(string property) { }
		public void Invoke(string command, object? args = null) { }
		public void DisconnectHandler() { }
		public Microsoft.Maui.Graphics.Size GetDesiredSize(double widthConstraint, double heightConstraint) => default;
		public void PlatformArrange(Microsoft.Maui.Graphics.Rect frame) { }
	}
}
