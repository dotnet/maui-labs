using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet
{
	public partial class NativeHost : View, INativeHost
	{
		readonly object syncLock = new object();
		readonly List<Action<object, IMauiContext>> connectActions = new List<Action<object, IMauiContext>>();
		readonly List<Action<object, IMauiContext>> updateActions = new List<Action<object, IMauiContext>>();
		readonly List<Action<object>> disconnectActions = new List<Action<object>>();
		readonly Func<IMauiContext, object> factory;
		object nativeView;
		Func<Size, Size> measureOverride;

		NativeHost(Func<IMauiContext, object> factory, object sourceToken, bool ownsNativeView)
		{
			this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
			SourceToken = sourceToken ?? throw new ArgumentNullException(nameof(sourceToken));
			OwnsNativeView = ownsNativeView;
		}

		public NativeHost(Func<IMauiContext, object> factory, bool ownsNativeView = true)
			: this(factory, factory, ownsNativeView)
		{
		}

		public NativeHost(object nativeView, bool ownsNativeView = false)
			: this(_ => nativeView ?? throw new ArgumentNullException(nameof(nativeView)), nativeView ?? throw new ArgumentNullException(nameof(nativeView)), ownsNativeView)
		{
			this.nativeView = nativeView;
		}

		public bool OwnsNativeView { get; }
		internal object SourceToken { get; }

		internal object GetOrCreateNativeView(IMauiContext mauiContext)
		{
			if (nativeView is not null)
				return nativeView;

			lock (syncLock)
			{
				if (nativeView is null)
					nativeView = factory(mauiContext) ?? throw new InvalidOperationException("NativeHost factory returned null.");
			}

			return nativeView;
		}

		internal void ReleaseNativeView(object releasedView, bool disposed)
		{
			if (disposed && ReferenceEquals(nativeView, releasedView))
				nativeView = null;
		}

		internal void ApplyConnected(object connectedView, IMauiContext mauiContext)
		{
			foreach (var action in connectActions)
				action?.Invoke(connectedView, mauiContext);

			ApplyUpdated(connectedView, mauiContext);
		}

		internal void ApplyUpdated(object updatedView, IMauiContext mauiContext)
		{
			foreach (var action in updateActions)
				action?.Invoke(updatedView, mauiContext);
		}

		internal void ApplyDisconnected(object disconnectedView)
		{
			foreach (var action in disconnectActions)
				action?.Invoke(disconnectedView);
		}

		internal bool TryMeasureOverride(Size availableSize, out Size size)
		{
			if (measureOverride is not null)
			{
				size = measureOverride(availableSize);
				return true;
			}

			size = default;
			return false;
		}

		public NativeHost OnConnect(Action<object, IMauiContext> action)
		{
			if (action is null)
				throw new ArgumentNullException(nameof(action));

			connectActions.Add(action);
			return this;
		}

		public NativeHost OnUpdate(Action<object, IMauiContext> action)
		{
			if (action is null)
				throw new ArgumentNullException(nameof(action));

			updateActions.Add(action);
			RequestNativeViewUpdate();
			return this;
		}

		public NativeHost OnDisconnect(Action<object> action)
		{
			if (action is null)
				throw new ArgumentNullException(nameof(action));

			disconnectActions.Add(action);
			return this;
		}

		public NativeHost Sync<T>(T value, Action<object, T> apply)
		{
			if (apply is null)
				throw new ArgumentNullException(nameof(apply));

			return OnUpdate((native, _) => apply(native, value));
		}

		public NativeHost MeasureUsing(Func<Size, Size> measure)
		{
			measureOverride = measure ?? throw new ArgumentNullException(nameof(measure));
			InvalidateMeasurement();
			return this;
		}

		public bool TryGetNativeView<T>(out T resolvedView) where T : class
		{
			if (ViewHandler is Handlers.INativeHostHandler handler && handler.GetNativeView() is T typedView)
			{
				resolvedView = typedView;
				return true;
			}

			resolvedView = null;
			return false;
		}

		protected override void OnHandlerChange()
		{
			base.OnHandlerChange();
			RequestNativeViewUpdate();
		}

		void RequestNativeViewUpdate()
		{
			if (ViewHandler is Handlers.INativeHostHandler handler)
			{
				handler.SyncNativeView();
				InvalidateMeasurement();
			}
		}

		public override Size GetDesiredSize(Size availableSize)
		{
			var frameConstraints = this.GetFrameConstraints();
			var margins = this.GetMargin();

			if (frameConstraints?.Height > 0 && frameConstraints?.Width > 0)
				return new Size(frameConstraints.Width.Value + margins.HorizontalThickness, frameConstraints.Height.Value + margins.VerticalThickness);

			Size measured;
			if (TryMeasureOverride(availableSize, out measured))
			{
			}
			else if (ViewHandler is Handlers.INativeHostHandler handler)
			{
				measured = handler.MeasureNativeView(availableSize);
			}
			else
			{
				measured = new Size(
					frameConstraints?.Width ?? availableSize.Width,
					frameConstraints?.Height ?? availableSize.Height);
			}

			if (frameConstraints?.Width > 0)
				measured.Width = frameConstraints.Width.Value;
			if (frameConstraints?.Height > 0)
				measured.Height = frameConstraints.Height.Value;

			if (measured.Width <= 0 && availableSize.Width > 0 && !double.IsInfinity(availableSize.Width))
				measured.Width = availableSize.Width;
			if (measured.Height <= 0 && availableSize.Height > 0 && !double.IsInfinity(availableSize.Height))
				measured.Height = availableSize.Height;
			else if (measured.Height <= 0)
				measured.Height = 44;

			measured.Width += margins.HorizontalThickness;
			measured.Height += margins.VerticalThickness;
			MeasuredSize = measured;
			MeasurementValid = ViewHandler is not null;
			return MeasuredSize;
		}
	}
}
