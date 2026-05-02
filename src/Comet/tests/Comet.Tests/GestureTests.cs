using System;
using System.Collections.Generic;
using Comet.Tests.Handlers;
using Xunit;

namespace Comet.Tests
{
	public class GestureTests : TestBase
	{
		[Fact]
		public void AddGestureAddsToGesturesList()
		{
			var view = new Text("Hello");
			var gesture = new TapGesture(_ => { });
			view.AddGesture(gesture);

			Assert.NotNull(view.Gestures);
			Assert.Single(view.Gestures);
			Assert.Same(gesture, view.Gestures[0]);
		}

		[Fact]
		public void AddMultipleGestures()
		{
			var view = new Text("Hello");
			var tap = new TapGesture(_ => { });
			var pan = new PanGesture(_ => { });
			view.AddGesture(tap);
			view.AddGesture(pan);

			Assert.Equal(2, view.Gestures.Count);
			Assert.Same(tap, view.Gestures[0]);
			Assert.Same(pan, view.Gestures[1]);
		}

		[Fact]
		public void RemoveGestureRemovesFromList()
		{
			var view = new Text("Hello");
			var gesture = new TapGesture(_ => { });
			view.AddGesture(gesture);
			Assert.Single(view.Gestures);

			view.RemoveGesture(gesture);
			Assert.Empty(view.Gestures);
		}

		[Fact]
		public void OnTapAddsGesture()
		{
			bool tapped = false;
			var view = new Text("Hello").OnTap(v => tapped = true);

			Assert.NotNull(view.Gestures);
			Assert.Single(view.Gestures);
			Assert.IsType<TapGesture>(view.Gestures[0]);
		}

		[Fact]
		public void OnLongPressAddsGesture()
		{
			var view = new Text("Hello").OnLongPress(v => { });

			Assert.Single(view.Gestures);
			Assert.IsType<LongPressGesture>(view.Gestures[0]);
		}

		[Fact]
		public void OnPanAddsGesture()
		{
			var view = new Text("Hello").OnPan(g => { });

			Assert.Single(view.Gestures);
			Assert.IsType<PanGesture>(view.Gestures[0]);
		}

		[Fact]
		public void OnPinchAddsGesture()
		{
			var view = new Text("Hello").OnPinch(g => { });

			Assert.Single(view.Gestures);
			Assert.IsType<PinchGesture>(view.Gestures[0]);
		}

		[Fact]
		public void OnSwipeAddsGesture()
		{
			var view = new Text("Hello").OnSwipe(g => { }, SwipeDirection.Right);

			Assert.Single(view.Gestures);
			var swipe = Assert.IsType<SwipeGesture>(view.Gestures[0]);
			Assert.Equal(SwipeDirection.Right, swipe.Direction);
		}

		[Fact]
		public void PanGestureProperties()
		{
			var gesture = new PanGesture(_ => { });
			gesture.TotalX = 10.5;
			gesture.TotalY = 20.3;
			gesture.Status = GestureStatus.Running;

			Assert.Equal(10.5, gesture.TotalX);
			Assert.Equal(20.3, gesture.TotalY);
			Assert.Equal(GestureStatus.Running, gesture.Status);
		}

		[Fact]
		public void PinchGestureProperties()
		{
			var gesture = new PinchGesture(_ => { });
			Assert.Equal(1.0, gesture.Scale);

			gesture.Scale = 2.5;
			gesture.Status = GestureStatus.Completed;

			Assert.Equal(2.5, gesture.Scale);
			Assert.Equal(GestureStatus.Completed, gesture.Status);
		}

		[Fact]
		public void SwipeGestureDirection()
		{
			var gesture = new SwipeGesture(_ => { });
			gesture.Direction = SwipeDirection.Up;
			Assert.Equal(SwipeDirection.Up, gesture.Direction);
		}

		[Fact]
		public void LongPressGestureMinimumPressDuration()
		{
			var gesture = new LongPressGesture(_ => { });
			Assert.Equal(0.5, gesture.MinimumPressDuration);

			gesture.MinimumPressDuration = 1.0;
			Assert.Equal(1.0, gesture.MinimumPressDuration);
		}

		[Fact]
		public void TapGestureInvokeCallsAction()
		{
			bool invoked = false;
			var gesture = new TapGesture(_ => invoked = true);
			gesture.Invoke();
			Assert.True(invoked);
		}

		[Fact]
		public void PanGestureInvokeCallsAction()
		{
			PanGesture received = null;
			var gesture = new PanGesture(g => received = g);
			gesture.TotalX = 5;
			gesture.Invoke();

			Assert.NotNull(received);
			Assert.Equal(5, received.TotalX);
		}

		[Fact]
		public void PinchGestureInvokeCallsAction()
		{
			PinchGesture received = null;
			var gesture = new PinchGesture(g => received = g);
			gesture.Scale = 3.0;
			gesture.Invoke();

			Assert.NotNull(received);
			Assert.Equal(3.0, received.Scale);
		}

		[Fact]
		public void SwipeGestureInvokeCallsAction()
		{
			SwipeGesture received = null;
			var gesture = new SwipeGesture(g => received = g);
			gesture.Direction = SwipeDirection.Down;
			gesture.Invoke();

			Assert.NotNull(received);
			Assert.Equal(SwipeDirection.Down, received.Direction);
		}

		[Fact]
		public void GestureViewInterfaceReturnsGestures()
		{
			var view = new Text("Hello");
			view.AddGesture(new TapGesture(_ => { }));

			IGestureView gestureView = view;
			Assert.NotNull(gestureView.Gestures);
			Assert.Single(gestureView.Gestures);
		}
	}
}
