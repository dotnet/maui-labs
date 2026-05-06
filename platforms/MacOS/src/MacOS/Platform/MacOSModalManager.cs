using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Graphics;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Manages modal page presentation on macOS.
/// By default, modals are presented as native AppKit sheets via NSWindow.BeginSheet.
/// Pages can opt into the overlay presentation style via MacOSPage.ModalPresentationStyleProperty.
/// </summary>
internal class MacOSModalManager
{
	readonly NSView _contentContainer;
	readonly List<ModalEntry> _modalStack = new();
	NSWindow? _parentWindow;

	record ModalEntry(
		Page Page,
		NSView PageView,
		IMauiContext MauiContext,
		MacOSModalPresentationStyle Style,
		// Sheet and Window modes: the NSWindow hosting the modal content
		NSWindow? ModalWindow = null,
		// Sheet and Window modes: the window that presented this modal
		NSWindow? PresentingWindow = null,
		// Overlay mode
		NSView? BackdropView = null,
		NSView? EffectView = null,
		// Window mode: dims and blocks interaction on the presenting window
		NSView? HostBlockerView = null);

	public MacOSModalManager(NSView contentContainer, NSWindow parentWindow)
	{
		_contentContainer = contentContainer;
		_parentWindow = parentWindow;
	}

	public int ModalCount => _modalStack.Count;
	public bool HasModals => _modalStack.Count > 0;

	public void UpdateParentWindow(NSWindow parentWindow)
	{
		_parentWindow = parentWindow;
	}

	/// <summary>
	/// Returns the topmost modal NSWindow (sheet or window-modal) if one exists,
	/// otherwise the parent window. This enables nesting: each new modal is
	/// presented from the topmost existing modal rather than always from the parent.
	/// </summary>
	NSWindow? GetTopmostModalHost()
	{
		for (int i = _modalStack.Count - 1; i >= 0; i--)
		{
			if (_modalStack[i].ModalWindow is { } win)
				return win;
		}
		return _parentWindow;
	}

	public void PushModal(Page page, IMauiContext mauiContext, bool animated)
	{
		var style = MacOSPage.GetModalPresentationStyle(page);

		switch (style)
		{
			case MacOSModalPresentationStyle.Sheet:
				PushSheet(page, mauiContext, animated);
				break;
			case MacOSModalPresentationStyle.Window:
				PushModalWindow(page, mauiContext, animated);
				break;
			default:
				PushOverlay(page, mauiContext, animated);
				break;
		}
	}

	public Page? PopModal(bool animated)
	{
		if (_modalStack.Count == 0)
			return null;

		var entry = _modalStack[^1];
		_modalStack.RemoveAt(_modalStack.Count - 1);

		switch (entry.Style)
		{
			case MacOSModalPresentationStyle.Sheet:
				RemoveSheet(entry);
				break;
			case MacOSModalPresentationStyle.Window:
				RemoveModalWindow(entry);
				break;
			default:
				RemoveOverlay(entry);
				break;
		}

		return entry.Page;
	}

	public void LayoutAllModals()
	{
		foreach (var entry in _modalStack)
		{
			if (entry.Style == MacOSModalPresentationStyle.Overlay)
				LayoutOverlay(entry);
			// Sheet layout is managed by AppKit
		}
	}

	#region Sheet Presentation

	void PushSheet(Page page, IMauiContext mauiContext, bool animated)
	{
		var platformView = ((IView)page).ToMacOSPlatform(mauiContext);

		if (platformView is MacOSContainerView container)
		{
			container.IgnorePlatformSafeArea = true;
			container.ExternalFrameManagement = true;
		}

		var sheetSize = ComputeSheetSize(page);

		var sheetFrame = new CGRect(0, 0, sheetSize.Width, sheetSize.Height);

		var sheetWindow = new NSWindow(
			sheetFrame,
			NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
			NSBackingStore.Buffered,
			deferCreation: false);
		sheetWindow.ReleasedWhenClosed = false;

		// Apply min size constraints to the sheet window
		var minWidth = MacOSPage.GetModalSheetMinWidth(page);
		var minHeight = MacOSPage.GetModalSheetMinHeight(page);
		if (minWidth > 0 || minHeight > 0)
		{
			sheetWindow.ContentMinSize = new CGSize(
				minWidth > 0 ? minWidth : 0,
				minHeight > 0 ? minHeight : 0);
		}

		platformView.Frame = new CGRect(0, 0, sheetFrame.Width, sheetFrame.Height);
		platformView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
		sheetWindow.ContentView = platformView;

		var presentingWindow = GetTopmostModalHost();
		var entry = new ModalEntry(page, platformView, mauiContext, MacOSModalPresentationStyle.Sheet, ModalWindow: sheetWindow, PresentingWindow: presentingWindow);
		if (presentingWindow == null)
		{
			DisposeModalWindow(entry);
			throw new InvalidOperationException("Cannot present a modal sheet without a host window.");
		}

		_modalStack.Add(entry);

		presentingWindow.BeginSheet(sheetWindow, (returnCode) => { });
	}

	CGSize ComputeSheetSize(Page page)
	{
		var parentFrame = _parentWindow?.ContentView?.Frame ?? _contentContainer.Bounds;
		var requestedWidth = MacOSPage.GetModalSheetWidth(page);
		var requestedHeight = MacOSPage.GetModalSheetHeight(page);
		var sizesToContent = MacOSPage.GetModalSheetSizesToContent(page);

		double width = parentFrame.Width;
		double height = parentFrame.Height;

		if (requestedWidth > 0)
			width = requestedWidth;

		if (requestedHeight > 0)
			height = requestedHeight;

		// When sizing to content, measure the page's Content (not the Page itself,
		// since Page always fills available space). Use unconstrained dimensions
		// so the content reports its natural/desired size.
		if (sizesToContent && (requestedWidth <= 0 || requestedHeight <= 0))
		{
			var contentView = (page as ContentPage)?.Content as IView;
			if (contentView != null)
			{
				var measured = contentView.Measure(
					double.PositiveInfinity,
					double.PositiveInfinity);

				// Account for the page's own padding
				var padding = page.Padding;
				var contentWidth = measured.Width + padding.Left + padding.Right;
				var contentHeight = measured.Height + padding.Top + padding.Bottom;

				if (requestedWidth <= 0)
					width = contentWidth;
				if (requestedHeight <= 0)
					height = contentHeight;
			}
		}

		// Apply min size constraints
		var minWidth = MacOSPage.GetModalSheetMinWidth(page);
		var minHeight = MacOSPage.GetModalSheetMinHeight(page);
		if (minWidth > 0 && width < minWidth) width = minWidth;
		if (minHeight > 0 && height < minHeight) height = minHeight;

		// Don't exceed parent window size
		if (width > parentFrame.Width) width = parentFrame.Width;
		if (height > parentFrame.Height) height = parentFrame.Height;

		return new CGSize(width, height);
	}

	void RemoveSheet(ModalEntry entry)
	{
		if (entry.ModalWindow != null)
		{
			var host = entry.PresentingWindow ?? _parentWindow;
			host?.EndSheet(entry.ModalWindow);
			DisposeModalWindow(entry);
		}
		else
		{
			entry.Page.Handler?.DisconnectHandler();
		}
	}

	#endregion

	#region Window Modal Presentation

	void PushModalWindow(Page page, IMauiContext mauiContext, bool animated)
	{
		var platformView = ((IView)page).ToMacOSPlatform(mauiContext);

		if (platformView is MacOSContainerView container)
		{
			container.IgnorePlatformSafeArea = true;
			container.ExternalFrameManagement = true;
		}

		var hostWindow = GetTopmostModalHost();

		// Add a dimming overlay to the host window to block interaction
		NSView? blockerView = null;
		if (hostWindow?.ContentView is { } hostContent)
		{
			blockerView = new NSView(hostContent.Bounds)
			{
				WantsLayer = true,
				AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
			};
			blockerView.Layer!.BackgroundColor = new CGColor(0, 0, 0, 0.25f);
			hostContent.AddSubview(blockerView);
		}

		var modalSize = ComputeSheetSize(page);
		var modalFrame = ComputeCenteredFrame(hostWindow, modalSize);

		var modalWindow = new NSWindow(
			modalFrame,
			NSWindowStyle.Titled | NSWindowStyle.Resizable,
			NSBackingStore.Buffered,
			deferCreation: false);
		modalWindow.ReleasedWhenClosed = false;

		var minWidth = MacOSPage.GetModalSheetMinWidth(page);
		var minHeight = MacOSPage.GetModalSheetMinHeight(page);
		if (minWidth > 0 || minHeight > 0)
		{
			modalWindow.ContentMinSize = new CGSize(
				minWidth > 0 ? minWidth : 0,
				minHeight > 0 ? minHeight : 0);
		}

		platformView.Frame = new CGRect(0, 0, modalSize.Width, modalSize.Height);
		platformView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
		modalWindow.ContentView = platformView;

		hostWindow?.AddChildWindow(modalWindow, NSWindowOrderingMode.Above);
		modalWindow.MakeKeyAndOrderFront(null);

		var entry = new ModalEntry(page, platformView, mauiContext, MacOSModalPresentationStyle.Window,
			ModalWindow: modalWindow, PresentingWindow: hostWindow, HostBlockerView: blockerView);
		_modalStack.Add(entry);
	}

	void RemoveModalWindow(ModalEntry entry)
	{
		// Remove the blocker overlay from the host window
		entry.HostBlockerView?.RemoveFromSuperview();

		if (entry.ModalWindow != null)
		{
			entry.PresentingWindow?.RemoveChildWindow(entry.ModalWindow);
			DisposeModalWindow(entry);
		}
		else
		{
			entry.Page.Handler?.DisconnectHandler();
		}

		// Re-activate the host window
		entry.PresentingWindow?.MakeKeyAndOrderFront(null);
	}

	static void DisposeModalWindow(ModalEntry entry)
	{
		if (entry.ModalWindow is not { } modalWindow)
			return;

		modalWindow.OrderOut(null);
		entry.Page.Handler?.DisconnectHandler();
		modalWindow.ContentView = null;
		modalWindow.Dispose();
	}

	static CGRect ComputeCenteredFrame(NSWindow? host, CGSize size)
	{
		if (host == null)
			return new CGRect(100, 100, size.Width, size.Height);

		var hostFrame = host.Frame;
		var x = hostFrame.X + (hostFrame.Width - size.Width) / 2;
		var y = hostFrame.Y + (hostFrame.Height - size.Height) / 2;
		return new CGRect(x, y, size.Width, size.Height);
	}

	#endregion

	#region Overlay Presentation

	void PushOverlay(Page page, IMauiContext mauiContext, bool animated)
	{
		// Create a semi-transparent backdrop
		var backdrop = new NSView(_contentContainer.Bounds)
		{
			WantsLayer = true,
			AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
		};
		backdrop.Layer!.BackgroundColor = new CGColor(0, 0, 0, 0.4f);
		_contentContainer.AddSubview(backdrop);

		var platformView = ((IView)page).ToMacOSPlatform(mauiContext);

		if (platformView is MacOSContainerView container)
		{
			container.IgnorePlatformSafeArea = true;
			container.ExternalFrameManagement = true;
		}

		var inset = GetOverlayInset();
		var safeRect = _contentContainer.SafeAreaRect;

		var pageFrame = new CGRect(
			safeRect.X + inset,
			safeRect.Y + inset,
			safeRect.Width - inset * 2,
			safeRect.Height - inset * 2);

		// Use NSVisualEffectView as the modal background container so it
		// automatically adapts to light/dark mode appearance changes.
		var effectView = new NSVisualEffectView(pageFrame)
		{
			Material = NSVisualEffectMaterial.WindowBackground,
			State = NSVisualEffectState.Active,
			BlendingMode = NSVisualEffectBlendingMode.BehindWindow,
			WantsLayer = true,
			AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
		};
		effectView.Layer!.CornerRadius = 10;
		effectView.Layer.MasksToBounds = true;

		platformView.Frame = new CGRect(0, 0, pageFrame.Width, pageFrame.Height);
		platformView.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
		effectView.AddSubview(platformView);

		_contentContainer.AddSubview(effectView);

		var entry = new ModalEntry(page, platformView, mauiContext, MacOSModalPresentationStyle.Overlay, BackdropView: backdrop, EffectView: effectView);
		_modalStack.Add(entry);

		LayoutOverlay(entry);
	}

	void RemoveOverlay(ModalEntry entry)
	{
		entry.PageView.RemoveFromSuperview();
		entry.EffectView?.RemoveFromSuperview();
		entry.BackdropView?.RemoveFromSuperview();
		entry.Page.Handler?.DisconnectHandler();
	}

	void LayoutOverlay(ModalEntry entry)
	{
		var inset = GetOverlayInset();
		var bounds = _contentContainer.Bounds;
		var safeRect = _contentContainer.SafeAreaRect;

		var pageFrame = new CGRect(
			safeRect.X + inset,
			safeRect.Y + inset,
			safeRect.Width - inset * 2,
			safeRect.Height - inset * 2);

		if (entry.BackdropView != null)
			entry.BackdropView.Frame = bounds;
		if (entry.EffectView != null)
			entry.EffectView.Frame = pageFrame;
		entry.PageView.Frame = new CGRect(0, 0, pageFrame.Width, pageFrame.Height);

		// Trigger layout on the container — don't call page.Arrange directly
		// because PlatformArrange would reset our frame to (0,0)
		entry.PageView.NeedsLayout = true;
	}

	static nfloat GetOverlayInset() => 20;

	#endregion
}
