using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// Embeds any Microsoft.Maui.Controls view (XAML or code-behind) inside a Comet MVU view tree.
	/// Uses a dedicated MauiViewHostHandler to create and host the platform view directly.
	/// 
	/// Usage:
	///   new MauiViewHost(new SfCircularChart { ... })
	///   new MauiViewHost(() => new MyExpensiveControl())
	///   new MauiViewHost(new MyChart()).Frame(width: 300, height: 200)
	/// </summary>
	public class MauiViewHost : View
	{
		private IView _hostedView;
		private Func<IView> _factory;
		private readonly object _lock = new object();

		public MauiViewHost(IView view)
		{
			_hostedView = view ?? throw new ArgumentNullException(nameof(view));
		}

		public MauiViewHost(Func<IView> factory)
		{
			_factory = factory ?? throw new ArgumentNullException(nameof(factory));
		}

		public IView HostedView
		{
			get
			{
				if (_hostedView is null && _factory is not null)
				{
					lock (_lock)
					{
						if (_hostedView is null && _factory is not null)
						{
							var factory = _factory;
							_factory = null; // Clear first to prevent retry on exception
							_hostedView = factory();
						}
					}
				}
				return _hostedView;
			}
		}

		public override Size GetDesiredSize(Size availableSize)
		{
			var frameConstraints = this.GetFrameConstraints();
			var margins = this.GetMargin();

			if (frameConstraints?.Height > 0 && frameConstraints?.Width > 0)
				return new Size(frameConstraints.Width.Value, frameConstraints.Height.Value);

			// Use frame constraints and available size as fallback.
			// Don't measure HostedView here — it may not have a handler/MauiContext yet.
			// The actual measurement happens in the platform handler's LayoutSubviews/SizeThatFits.
			Size ms = new Size(
				frameConstraints?.Width ?? availableSize.Width,
				frameConstraints?.Height ?? availableSize.Height);

			// If the hosted view has a handler, try to measure it
			if (HostedView?.Handler is not null)
			{
				try
				{
					var measured = HostedView.Measure(availableSize.Width, availableSize.Height);
					if (measured.Width > 0 || measured.Height > 0)
						ms = measured;
				}
				catch { }
			}

			if (frameConstraints?.Width > 0)
				ms.Width = frameConstraints.Width.Value;
			if (frameConstraints?.Height > 0)
				ms.Height = frameConstraints.Height.Value;

			// Ensure non-zero size when we have constraints
			if (ms.Width <= 0 && availableSize.Width > 0)
				ms.Width = availableSize.Width;
			if (ms.Height <= 0 && frameConstraints?.Height > 0)
				ms.Height = frameConstraints.Height.Value;
			else if (ms.Height <= 0)
				ms.Height = 44; // Default minimum height

			ms.Width += margins.HorizontalThickness;
			ms.Height += margins.VerticalThickness;
			MeasuredSize = ms;
			MeasurementValid = ViewHandler is not null;
			return MeasuredSize;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (_hostedView?.Handler is IElementHandler handler)
					handler.DisconnectHandler();
				if (_hostedView is IDisposable disposable)
					disposable.Dispose();
				_hostedView = null;
				_factory = null;
			}
			base.Dispose(disposing);
		}
	}
}
