using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Comet.Handlers;

/// <summary>
/// Windows handler for CometHost. Creates a container that hosts
/// the Comet View's rendered body content.
/// </summary>
public partial class CometHostHandler : ViewHandler<CometHost, CometHostHandler.CometHostContainerPanel>
{
	public CometHostHandler() : base(CometHostMapper) { }

	protected override CometHostContainerPanel CreatePlatformView()
		=> new CometHostContainerPanel();

	protected override void ConnectHandler(CometHostContainerPanel platformView)
	{
		base.ConnectHandler(platformView);
		UpdateCometView();
	}

	protected override void DisconnectHandler(CometHostContainerPanel platformView)
	{
		platformView.ClearContent();
		base.DisconnectHandler(platformView);
	}

	public override Microsoft.Maui.Graphics.Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		if (VirtualView is IContentView contentView)
		{
			var size = contentView.CrossPlatformMeasure(widthConstraint, heightConstraint);
			if (size.Width > 0 && size.Height > 0)
				return size;
		}
		var w = double.IsInfinity(widthConstraint) ? 400 : widthConstraint;
		var h = double.IsInfinity(heightConstraint) ? 800 : heightConstraint;
		return new Microsoft.Maui.Graphics.Size(w, h);
	}

	void UpdateCometView()
	{
		if (VirtualView?.CometView is null || MauiContext is null)
			return;

		try
		{
			var cometView = VirtualView.CometView;
			
			// Get the render view (body content) to avoid CometViewHandler handler circularity
			var renderView = cometView.GetView();
			IView viewToRender = (renderView is not null && renderView != cometView) ? renderView : cometView;
			
			var platformView = viewToRender.ToPlatform(MauiContext);
			if (platformView is FrameworkElement fe)
				PlatformView.SetContent(fe, viewToRender);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[CometHostHandler] UpdateCometView failed: {ex.Message}");
		}
	}

	public class CometHostContainerPanel : Canvas
	{
		FrameworkElement _contentElement;
		IView _virtualView;

		public void SetContent(FrameworkElement element, IView virtualView)
		{
			if (_contentElement is not null)
				Children.Remove(_contentElement);
			_contentElement = element;
			_virtualView = virtualView;
			if (_contentElement is not null)
				Children.Add(_contentElement);
		}

		public void ClearContent()
		{
			if (_contentElement is not null)
				Children.Remove(_contentElement);
			_contentElement = null;
			_virtualView = null;
		}

		protected override global::Microsoft.UI.Xaml.Size ArrangeOverride(global::Microsoft.UI.Xaml.Size finalSize)
		{
			if (_contentElement is not null)
			{
				_virtualView?.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, finalSize.Width, finalSize.Height));
				_contentElement.Arrange(new global::Microsoft.UI.Xaml.Rect(0, 0, finalSize.Width, finalSize.Height));
			}
			return base.ArrangeOverride(finalSize);
		}

		protected override global::Microsoft.UI.Xaml.Size MeasureOverride(global::Microsoft.UI.Xaml.Size availableSize)
		{
			if (_contentElement is not null)
			{
				_virtualView?.Measure(availableSize.Width, availableSize.Height);
				_contentElement.Measure(availableSize);
			}
			return base.MeasureOverride(availableSize);
		}
	}
}
