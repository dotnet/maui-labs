using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// SwipeView handler for macOS. Swipe-to-reveal actions via horizontal pan gesture.
/// </summary>
public partial class SwipeViewHandler : MacOSViewHandler<SwipeView, NSView>
{
	public static readonly IPropertyMapper<SwipeView, SwipeViewHandler> Mapper =
		new PropertyMapper<SwipeView, SwipeViewHandler>(ViewMapper)
		{
			[nameof(SwipeView.Content)] = MapContent,
			[nameof(SwipeView.LeftItems)] = MapSwipeItems,
			[nameof(SwipeView.RightItems)] = MapSwipeItems,
		};

	static readonly CommandMapper<SwipeView, SwipeViewHandler> CommandMapper =
		new CommandMapper<SwipeView, SwipeViewHandler>(ViewCommandMapper)
		{
			[nameof(ISwipeView.RequestClose)] = MapRequestClose,
		};

	MacOSContainerView? _container;
	NSView? _contentView;
	NSView? _actionContainer;
	nfloat _panOffset;
	bool _isOpen;
	static readonly nfloat ActionWidth = 80;

	public SwipeViewHandler() : base(Mapper, CommandMapper) { }

	protected override NSView CreatePlatformView()
	{
		_container = new SwipeContainerView { Handler = this };
		return _container;
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);
		if (_contentView != null)
			_contentView.Frame = new CGRect(_panOffset, 0, rect.Width, rect.Height);
	}

	public static void MapContent(SwipeViewHandler handler, SwipeView view) => handler.UpdateContent();
	public static void MapSwipeItems(SwipeViewHandler handler, SwipeView view) { }

	public static void MapRequestClose(SwipeViewHandler handler, SwipeView view, object? args)
	{
		handler.CloseSwipe(true);
	}

	void UpdateContent()
	{
		if (_container == null || MauiContext == null) return;

		_contentView?.RemoveFromSuperview();
		_contentView = null;

		if (VirtualView?.Content is IView content)
		{
			_contentView = content.ToMacOSPlatform(MauiContext);
			_container.AddSubview(_contentView);
		}
	}

	void ShowActions(SwipeItems? items, bool fromRight)
	{
		if (_container == null || MauiContext == null || items == null || items.Count == 0)
			return;

		_actionContainer?.RemoveFromSuperview();
		var width = ActionWidth * items.Count;
		var containerBounds = _container.Bounds;
		var x = fromRight ? containerBounds.Width - width : 0;

		_actionContainer = new MacOSContainerView();
		_actionContainer.WantsLayer = true;
		_actionContainer.Frame = new CGRect(x, 0, width, containerBounds.Height);

		nfloat btnX = 0;
		foreach (var item in items)
		{
			if (item is SwipeItem swipeItem)
			{
				var btn = CreateActionButton(swipeItem, containerBounds.Height);
				btn.Frame = new CGRect(btnX, 0, ActionWidth, containerBounds.Height);
				_actionContainer.AddSubview(btn);
				btnX += ActionWidth;
			}
		}

		_container.AddSubview(_actionContainer, NSWindowOrderingMode.Below, _contentView);
	}

	NSView CreateActionButton(SwipeItem item, nfloat height)
	{
		var view = new NSView(new CGRect(0, 0, ActionWidth, height));
		view.WantsLayer = true;

		if (item.BackgroundColor != null)
			view.Layer!.BackgroundColor = item.BackgroundColor.ToPlatformColor().CGColor;
		else
			view.Layer!.BackgroundColor = NSColor.SystemBlue.CGColor;

		var label = new NSTextField
		{
			StringValue = item.Text ?? string.Empty,
			Editable = false,
			Bezeled = false,
			DrawsBackground = false,
			Alignment = NSTextAlignment.Center,
			TextColor = NSColor.White,
			Font = NSFont.SystemFontOfSize(12),
		};
		label.SizeToFit();
		label.Frame = new CGRect(0, (height - label.Frame.Height) / 2, ActionWidth, label.Frame.Height);
		view.AddSubview(label);

		var click = new NSClickGestureRecognizer(() =>
		{
			item.Command?.Execute(item.CommandParameter);
			CloseSwipe(true);
		});
		view.AddGestureRecognizer(click);

		return view;
	}

	internal void HandlePan(nfloat deltaX, bool ended)
	{
		if (_contentView == null || _container == null) return;

		if (!ended)
		{
			_panOffset += deltaX;

			// Determine which actions to show
			if (_panOffset > 10 && VirtualView?.LeftItems?.Count > 0)
				ShowActions(VirtualView.LeftItems, false);
			else if (_panOffset < -10 && VirtualView?.RightItems?.Count > 0)
				ShowActions(VirtualView.RightItems, true);

			var bounds = _container.Bounds;
			_contentView.Frame = new CGRect(_panOffset, 0, bounds.Width, bounds.Height);
		}
		else
		{
			var threshold = ActionWidth / 2;
			if (Math.Abs(_panOffset) > threshold)
			{
				var items = _panOffset > 0 ? VirtualView?.LeftItems : VirtualView?.RightItems;
				if (items?.Count > 0)
				{
					var targetOffset = _panOffset > 0 ? ActionWidth * items.Count : -ActionWidth * items.Count;
					AnimateContent(targetOffset);
					_isOpen = true;
					return;
				}
			}
			CloseSwipe(true);
		}
	}

	void CloseSwipe(bool animated)
	{
		_isOpen = false;
		if (animated)
			AnimateContent(0);
		else
		{
			_panOffset = 0;
			if (_contentView != null && _container != null)
				_contentView.Frame = new CGRect(0, 0, _container.Bounds.Width, _container.Bounds.Height);
			_actionContainer?.RemoveFromSuperview();
			_actionContainer = null;
		}
	}

	void AnimateContent(nfloat targetX)
	{
		if (_contentView == null || _container == null) return;

		_panOffset = targetX;
		NSAnimationContext.RunAnimation(ctx =>
		{
			ctx.Duration = 0.25;
			ctx.AllowsImplicitAnimation = true;
			_contentView.Frame = new CGRect(targetX, 0, _container.Bounds.Width, _container.Bounds.Height);
		}, () =>
		{
			if (targetX == 0)
			{
				_actionContainer?.RemoveFromSuperview();
				_actionContainer = null;
			}
		});
	}

	class SwipeContainerView : MacOSContainerView
	{
		internal SwipeViewHandler? Handler { get; set; }
		NSPanGestureRecognizer? _pan;
		nfloat _lastTranslationX;

		public override void ViewDidMoveToWindow()
		{
			base.ViewDidMoveToWindow();
			if (_pan == null)
			{
				_pan = new NSPanGestureRecognizer(HandlePanGesture);
				AddGestureRecognizer(_pan);
			}
		}

		void HandlePanGesture(NSPanGestureRecognizer gesture)
		{
			var translation = gesture.TranslationInView(this);

			if (gesture.State == NSGestureRecognizerState.Began)
			{
				_lastTranslationX = 0;
			}

			var deltaX = translation.X - _lastTranslationX;
			_lastTranslationX = translation.X;

			Handler?.HandlePan(deltaX, gesture.State == NSGestureRecognizerState.Ended ||
										gesture.State == NSGestureRecognizerState.Cancelled);
		}
	}
}
