using System;
using System.Collections.Generic;
using System.Linq;
using Android.Views;
using AView = Android.Views.View;
namespace Comet.Android.Controls
{
	public class CometTouchGestureListener : Java.Lang.Object, AView.IOnTouchListener
	{
		class GestureDetectorListener : Java.Lang.Object, GestureDetector.IOnGestureListener, ScaleGestureDetector.IOnScaleGestureListener
		{
			readonly GestureDetector gestureDetector;
			readonly ScaleGestureDetector scaleDetector;
			public GestureDetectorListener(View view)
			{
				var context = view.GetMauiContext().Context;
				gestureDetector = new GestureDetector(context, this);
				scaleDetector = new ScaleGestureDetector(context, this);
			}


			public bool OnDown(MotionEvent e) => true;

			public bool OnFling(MotionEvent e1, MotionEvent e2, float velocityX, float velocityY)
			{
				if (dictionary.TryGetValue(e1, out var listener))
				{
					float diffX = e2.GetX() - e1.GetX();
					float diffY = e2.GetY() - e1.GetY();

					if (Math.Abs(diffX) > Math.Abs(diffY))
					{
						var direction = diffX > 0 ? SwipeDirection.Right : SwipeDirection.Left;
						listener.OnSwipe(direction);
					}
					else
					{
						var direction = diffY > 0 ? SwipeDirection.Down : SwipeDirection.Up;
						listener.OnSwipe(direction);
					}
				}
				return true;
			}

			public void OnLongPress(MotionEvent e)
			{
				if (dictionary.TryGetValue(e, out var listener))
					listener.OnLongPress();
			}

			public bool OnScroll(MotionEvent e1, MotionEvent e2, float distanceX, float distanceY)
			{
				if (dictionary.TryGetValue(e1, out var listener))
				{
					float totalX = e2.GetX() - e1.GetX();
					float totalY = e2.GetY() - e1.GetY();
					var status = e2.Action == MotionEventActions.Up ? GestureStatus.Completed : GestureStatus.Running;
					listener.OnPan(totalX, totalY, status);
				}
				return true;
			}

			public void OnShowPress(MotionEvent e)
			{
			}

			public bool OnSingleTapUp(MotionEvent e)
			{
				if (dictionary.TryGetValue(e, out var listener))
					listener.OnTap();
				return true;
			}

			// Scale gesture callbacks
			public bool OnScale(ScaleGestureDetector detector)
			{
				if (currentListener is not null)
					currentListener.OnPinch(detector.ScaleFactor, GestureStatus.Running);
				return true;
			}

			public bool OnScaleBegin(ScaleGestureDetector detector)
			{
				if (currentListener is not null)
					currentListener.OnPinch(detector.ScaleFactor, GestureStatus.Started);
				return true;
			}

			public void OnScaleEnd(ScaleGestureDetector detector)
			{
				if (currentListener is not null)
					currentListener.OnPinch(detector.ScaleFactor, GestureStatus.Completed);
			}

			CometTouchGestureListener currentListener;
			Dictionary<MotionEvent, CometTouchGestureListener> dictionary = new Dictionary<MotionEvent, CometTouchGestureListener>();
			public bool OnTouchEvent(CometTouchGestureListener v, MotionEvent e)
			{
				var isComplete = e.IsComplete();
				try
				{
					if (!isComplete)
					{
						dictionary[e] = v;
						currentListener = v;
					}
					Logger.Debug($"Touch dictionary {dictionary.Count}");
					bool handled = scaleDetector.OnTouchEvent(e);
					handled |= gestureDetector.OnTouchEvent(e);
					return handled;
				}
				finally
				{
					if (isComplete)
					{
						dictionary.Remove(e);
						currentListener = null;
					}
				}
			}
		}

		AView nativeView;
		View view;
		GestureDetectorListener _gestureDetector;
		GestureDetectorListener gestureDetector => _gestureDetector ?? (_gestureDetector = new GestureDetectorListener(view));
		public CometTouchGestureListener(AView nativeView, View view)
		{
			this.view = view;
			this.nativeView = nativeView;
			nativeView.SetOnTouchListener(this);
		}
		List<Gesture> gestures = new List<Gesture>();
		public void AddGesture(Gesture gesture) => gestures.Add(gesture);

		public void RemoveGesture(Gesture gesture) => gestures.Remove(gesture);

		protected void OnTap()
		{
			foreach (var g in gestures.OfType<TapGesture>())
				g.Invoke();
		}

		protected void OnLongPress()
		{
			foreach (var g in gestures.OfType<LongPressGesture>())
				g.Invoke();
		}

		protected void OnPan(float totalX, float totalY, GestureStatus status)
		{
			foreach (var g in gestures.OfType<PanGesture>())
			{
				g.TotalX = totalX;
				g.TotalY = totalY;
				g.Status = status;
				g.Invoke();
			}
		}

		protected void OnPinch(float scale, GestureStatus status)
		{
			foreach (var g in gestures.OfType<PinchGesture>())
			{
				g.Scale = scale;
				g.Status = status;
				g.Invoke();
			}
		}

		protected void OnSwipe(SwipeDirection direction)
		{
			foreach (var g in gestures.OfType<SwipeGesture>())
			{
				if (g.Direction == direction)
					g.Invoke();
			}
		}

		public bool OnTouch(AView v, MotionEvent e)
		{
			return gestureDetector.OnTouchEvent(this, e);
		}
		protected override void Dispose(bool disposing)
		{
			nativeView.SetOnTouchListener(null);
			base.Dispose(disposing);
		}

	}
}
