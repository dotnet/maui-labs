using Microsoft.Maui.Controls;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Controls how a modal page is presented on macOS.
/// </summary>
public enum MacOSModalPresentationStyle
{
	/// <summary>
	/// Present as a native AppKit sheet that slides down from the window titlebar.
	/// This is the standard macOS modal presentation style.
	/// </summary>
	Sheet = 0,

	/// <summary>
	/// Present as an overlay view on top of the window content with a semi-transparent
	/// backdrop and rounded corners. This is a custom MAUI presentation style.
	/// </summary>
	Overlay = 1,

	/// <summary>
	/// Present as a separate floating NSWindow that is modal relative to its parent.
	/// The parent window is dimmed and blocked from interaction until the modal is dismissed.
	/// Supports stacking — each modal window becomes the host for the next.
	/// </summary>
	Window = 2,
}

/// <summary>
/// Attached properties for configuring macOS-specific page behavior.
/// </summary>
/// <example>
/// <code>
/// // Native sheet with custom size
/// var page = new MyModalPage();
/// MacOSPage.SetModalSheetWidth(page, 600);
/// MacOSPage.SetModalSheetHeight(page, 400);
/// await Navigation.PushModalAsync(page);
///
/// // Sheet that sizes to its content
/// MacOSPage.SetModalSheetSizesToContent(page, true);
/// MacOSPage.SetModalSheetMinWidth(page, 300);
/// MacOSPage.SetModalSheetMinHeight(page, 200);
/// await Navigation.PushModalAsync(page);
///
/// // Overlay style (old behavior)
/// MacOSPage.SetModalPresentationStyle(page, MacOSModalPresentationStyle.Overlay);
/// await Navigation.PushModalAsync(page);
/// </code>
/// </example>
public static class MacOSPage
{
	/// <summary>
	/// Controls how the page is presented when pushed modally.
	/// Defaults to <see cref="MacOSModalPresentationStyle.Sheet"/> for native AppKit sheet presentation.
	/// Set to <see cref="MacOSModalPresentationStyle.Overlay"/> for the overlay-style presentation.
	/// </summary>
	public static readonly BindableProperty ModalPresentationStyleProperty =
		BindableProperty.CreateAttached(
			"ModalPresentationStyle",
			typeof(MacOSModalPresentationStyle),
			typeof(MacOSPage),
			MacOSModalPresentationStyle.Sheet);

	public static MacOSModalPresentationStyle GetModalPresentationStyle(BindableObject obj)
		=> (MacOSModalPresentationStyle)obj.GetValue(ModalPresentationStyleProperty);

	public static void SetModalPresentationStyle(BindableObject obj, MacOSModalPresentationStyle value)
		=> obj.SetValue(ModalPresentationStyleProperty, value);

	/// <summary>
	/// When true, the sheet measures the page content and sizes to fit.
	/// Respects <see cref="ModalSheetMinWidthProperty"/> and <see cref="ModalSheetMinHeightProperty"/>.
	/// Ignored when <see cref="ModalSheetWidthProperty"/> or <see cref="ModalSheetHeightProperty"/> are set.
	/// Defaults to false (sheet matches parent window size).
	/// </summary>
	public static readonly BindableProperty ModalSheetSizesToContentProperty =
		BindableProperty.CreateAttached(
			"ModalSheetSizesToContent",
			typeof(bool),
			typeof(MacOSPage),
			false);

	public static bool GetModalSheetSizesToContent(BindableObject obj)
		=> (bool)obj.GetValue(ModalSheetSizesToContentProperty);

	public static void SetModalSheetSizesToContent(BindableObject obj, bool value)
		=> obj.SetValue(ModalSheetSizesToContentProperty, value);

	/// <summary>
	/// Requested width for the modal sheet. When set to -1 (default), the sheet
	/// matches the parent window width. Only applies to Sheet presentation style.
	/// </summary>
	public static readonly BindableProperty ModalSheetWidthProperty =
		BindableProperty.CreateAttached(
			"ModalSheetWidth",
			typeof(double),
			typeof(MacOSPage),
			-1d);

	public static double GetModalSheetWidth(BindableObject obj)
		=> (double)obj.GetValue(ModalSheetWidthProperty);

	public static void SetModalSheetWidth(BindableObject obj, double value)
		=> obj.SetValue(ModalSheetWidthProperty, value);

	/// <summary>
	/// Requested height for the modal sheet. When set to -1 (default), the sheet
	/// matches the parent window height. Only applies to Sheet presentation style.
	/// </summary>
	public static readonly BindableProperty ModalSheetHeightProperty =
		BindableProperty.CreateAttached(
			"ModalSheetHeight",
			typeof(double),
			typeof(MacOSPage),
			-1d);

	public static double GetModalSheetHeight(BindableObject obj)
		=> (double)obj.GetValue(ModalSheetHeightProperty);

	public static void SetModalSheetHeight(BindableObject obj, double value)
		=> obj.SetValue(ModalSheetHeightProperty, value);

	/// <summary>
	/// Minimum width for the modal sheet. Used when <see cref="ModalSheetSizesToContentProperty"/>
	/// is true, or as the NSWindow minimum content size. Defaults to -1 (no minimum).
	/// </summary>
	public static readonly BindableProperty ModalSheetMinWidthProperty =
		BindableProperty.CreateAttached(
			"ModalSheetMinWidth",
			typeof(double),
			typeof(MacOSPage),
			-1d);

	public static double GetModalSheetMinWidth(BindableObject obj)
		=> (double)obj.GetValue(ModalSheetMinWidthProperty);

	public static void SetModalSheetMinWidth(BindableObject obj, double value)
		=> obj.SetValue(ModalSheetMinWidthProperty, value);

	/// <summary>
	/// Minimum height for the modal sheet. Used when <see cref="ModalSheetSizesToContentProperty"/>
	/// is true, or as the NSWindow minimum content size. Defaults to -1 (no minimum).
	/// </summary>
	public static readonly BindableProperty ModalSheetMinHeightProperty =
		BindableProperty.CreateAttached(
			"ModalSheetMinHeight",
			typeof(double),
			typeof(MacOSPage),
			-1d);

	public static double GetModalSheetMinHeight(BindableObject obj)
		=> (double)obj.GetValue(ModalSheetMinHeightProperty);

	public static void SetModalSheetMinHeight(BindableObject obj, double value)
		=> obj.SetValue(ModalSheetMinHeightProperty, value);
}
