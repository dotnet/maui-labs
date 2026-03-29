using Microsoft.Maui.Controls;

namespace Platform.Maui.Linux.Gtk4.Platform;

/// <summary>
/// Controls how a page is presented when pushed modally via PushModalAsync.
/// </summary>
public enum GtkModalPresentationStyle
{
	/// <summary>
	/// Present as a native GTK4 modal window (Gtk.Window with SetModal/SetTransientFor).
	/// </summary>
	Dialog = 0,

	/// <summary>
	/// Present inline within the main window, hiding the current content (legacy behavior).
	/// </summary>
	Inline = 1,
}

/// <summary>
/// Attached properties for configuring GTK-specific page behavior.
/// </summary>
/// <example>
/// <code>
/// // Native dialog with custom size
/// var page = new MyModalPage();
/// GtkPage.SetModalWidth(page, 600);
/// GtkPage.SetModalHeight(page, 400);
/// await Navigation.PushModalAsync(page);
///
/// // Dialog that sizes to its content
/// GtkPage.SetModalSizesToContent(page, true);
/// GtkPage.SetModalMinWidth(page, 300);
/// GtkPage.SetModalMinHeight(page, 200);
/// await Navigation.PushModalAsync(page);
///
/// // Inline style (old behavior)
/// GtkPage.SetModalPresentationStyle(page, GtkModalPresentationStyle.Inline);
/// await Navigation.PushModalAsync(page);
/// </code>
/// </example>
public static class GtkPage
{
	/// <summary>
	/// Controls how the page is presented when pushed modally.
	/// Defaults to <see cref="GtkModalPresentationStyle.Dialog"/> for native GTK4 dialog window.
	/// Set to <see cref="GtkModalPresentationStyle.Inline"/> for the inline presentation.
	/// </summary>
	public static readonly BindableProperty ModalPresentationStyleProperty =
		BindableProperty.CreateAttached(
			"ModalPresentationStyle",
			typeof(GtkModalPresentationStyle),
			typeof(GtkPage),
			GtkModalPresentationStyle.Dialog);

	public static GtkModalPresentationStyle GetModalPresentationStyle(BindableObject obj)
		=> (GtkModalPresentationStyle)obj.GetValue(ModalPresentationStyleProperty);

	public static void SetModalPresentationStyle(BindableObject obj, GtkModalPresentationStyle value)
		=> obj.SetValue(ModalPresentationStyleProperty, value);

	/// <summary>
	/// When true, the dialog measures the page content and sizes to fit.
	/// Respects <see cref="ModalMinWidthProperty"/> and <see cref="ModalMinHeightProperty"/>.
	/// Ignored when <see cref="ModalWidthProperty"/> or <see cref="ModalHeightProperty"/> are set.
	/// Defaults to false (dialog matches parent window size).
	/// </summary>
	public static readonly BindableProperty ModalSizesToContentProperty =
		BindableProperty.CreateAttached(
			"ModalSizesToContent",
			typeof(bool),
			typeof(GtkPage),
			false);

	public static bool GetModalSizesToContent(BindableObject obj)
		=> (bool)obj.GetValue(ModalSizesToContentProperty);

	public static void SetModalSizesToContent(BindableObject obj, bool value)
		=> obj.SetValue(ModalSizesToContentProperty, value);

	/// <summary>
	/// Requested width for the modal dialog. When set to -1 (default), the dialog
	/// matches the parent window width. Only applies to Dialog presentation style.
	/// </summary>
	public static readonly BindableProperty ModalWidthProperty =
		BindableProperty.CreateAttached(
			"ModalWidth",
			typeof(double),
			typeof(GtkPage),
			-1d);

	public static double GetModalWidth(BindableObject obj)
		=> (double)obj.GetValue(ModalWidthProperty);

	public static void SetModalWidth(BindableObject obj, double value)
		=> obj.SetValue(ModalWidthProperty, value);

	/// <summary>
	/// Requested height for the modal dialog. When set to -1 (default), the dialog
	/// matches the parent window height. Only applies to Dialog presentation style.
	/// </summary>
	public static readonly BindableProperty ModalHeightProperty =
		BindableProperty.CreateAttached(
			"ModalHeight",
			typeof(double),
			typeof(GtkPage),
			-1d);

	public static double GetModalHeight(BindableObject obj)
		=> (double)obj.GetValue(ModalHeightProperty);

	public static void SetModalHeight(BindableObject obj, double value)
		=> obj.SetValue(ModalHeightProperty, value);

	/// <summary>
	/// Minimum width for the modal dialog. Used when <see cref="ModalSizesToContentProperty"/>
	/// is true, or as the GTK window size request. Defaults to -1 (no minimum).
	/// </summary>
	public static readonly BindableProperty ModalMinWidthProperty =
		BindableProperty.CreateAttached(
			"ModalMinWidth",
			typeof(double),
			typeof(GtkPage),
			-1d);

	public static double GetModalMinWidth(BindableObject obj)
		=> (double)obj.GetValue(ModalMinWidthProperty);

	public static void SetModalMinWidth(BindableObject obj, double value)
		=> obj.SetValue(ModalMinWidthProperty, value);

	/// <summary>
	/// Minimum height for the modal dialog. Used when <see cref="ModalSizesToContentProperty"/>
	/// is true, or as the GTK window size request. Defaults to -1 (no minimum).
	/// </summary>
	public static readonly BindableProperty ModalMinHeightProperty =
		BindableProperty.CreateAttached(
			"ModalMinHeight",
			typeof(double),
			typeof(GtkPage),
			-1d);

	public static double GetModalMinHeight(BindableObject obj)
		=> (double)obj.GetValue(ModalMinHeightProperty);

	public static void SetModalMinHeight(BindableObject obj, double value)
		=> obj.SetValue(ModalMinHeightProperty, value);
}
