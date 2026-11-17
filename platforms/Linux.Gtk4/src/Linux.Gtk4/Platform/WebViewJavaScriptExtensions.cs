using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// Evaluates JavaScript in a WebKitGTK <see cref="WebKit.WebView"/> without exposing the
/// resulting <c>JSCValue</c> to managed code.
/// </summary>
/// <remarks>
/// GirCore wraps the transfer-full <c>JSCValue</c> returned by
/// <c>webkit_web_view_evaluate_javascript_finish</c> in a toggle-referenced object whose release
/// is queued onto the GLib main loop when the wrapper is collected or disposed. That release can
/// run after a navigation has destroyed the value's JavaScript context, or after JavaScriptCore has
/// handed the same value pointer to a newer wrapper, which crashes inside JavaScriptCore. These
/// helpers read the result and release the native value synchronously inside WebKit's completion
/// callback on the GTK thread, while its context is still alive.
/// </remarks>
public static unsafe class WebViewJavaScriptExtensions
{
	const string WebKitLibrary = "libwebkitgtk-6.0.so.4";
	const string JavaScriptCoreLibrary = "libjavascriptcoregtk-6.0.so.1";
	const string GObjectLibrary = "libgobject-2.0.so.0";
	const string GLibLibrary = "libglib-2.0.so.0";

	/// <summary>
	/// Evaluates <paramref name="script"/> and returns the result converted with
	/// <c>jsc_value_to_string</c> (for example <c>"undefined"</c> for no value).
	/// Must be called on the GTK thread.
	/// </summary>
	public static Task<string?> EvaluateJavaScriptToStringAsync(this WebKit.WebView webView, string script)
	{
		ArgumentNullException.ThrowIfNull(webView);
		ArgumentNullException.ThrowIfNull(script);

		var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var state = GCHandle.Alloc(completion);
		var scriptUtf8 = Marshal.StringToCoTaskMemUTF8(script);
		try
		{
			webkit_web_view_evaluate_javascript(
				webView.Handle.DangerousGetHandle(),
				scriptUtf8,
				-1,
				IntPtr.Zero,
				IntPtr.Zero,
				IntPtr.Zero,
				&OnEvaluated,
				GCHandle.ToIntPtr(state));
		}
		catch
		{
			state.Free();
			throw;
		}
		finally
		{
			// WebKit copies the script before returning.
			Marshal.FreeCoTaskMem(scriptUtf8);
		}

		return completion.Task;
	}

	/// <summary>
	/// Evaluates <paramref name="script"/> for its side effects and discards the result.
	/// Must be called on the GTK thread.
	/// </summary>
	public static Task RunJavaScriptAsync(this WebKit.WebView webView, string script) =>
		webView.EvaluateJavaScriptToStringAsync(script);

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	static void OnEvaluated(IntPtr sourceObject, IntPtr result, IntPtr userData)
	{
		var state = GCHandle.FromIntPtr(userData);
		var completion = (TaskCompletionSource<string?>)state.Target!;
		state.Free();

		try
		{
			IntPtr error = IntPtr.Zero;
			var value = webkit_web_view_evaluate_javascript_finish(sourceObject, result, &error);
			if (value == IntPtr.Zero)
			{
				completion.TrySetException(new InvalidOperationException(ConsumeError(error)));
				return;
			}

			try
			{
				var text = jsc_value_to_string(value);
				try
				{
					completion.TrySetResult(Marshal.PtrToStringUTF8(text));
				}
				finally
				{
					g_free(text);
				}
			}
			finally
			{
				g_object_unref(value);
			}
		}
		catch (Exception ex)
		{
			completion.TrySetException(ex);
		}
	}

	static string ConsumeError(IntPtr error)
	{
		if (error == IntPtr.Zero)
			return "WebKit JavaScript evaluation failed.";

		// GError layout: GQuark domain; gint code; gchar* message.
		var message = Marshal.PtrToStringUTF8(((GError*)error)->Message) ?? "WebKit JavaScript evaluation failed.";
		g_error_free(error);
		return message;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct GError
	{
		public uint Domain;
		public int Code;
		public IntPtr Message;
	}

	[DllImport(WebKitLibrary)]
	static extern void webkit_web_view_evaluate_javascript(
		IntPtr webView,
		IntPtr script,
		nint length,
		IntPtr worldName,
		IntPtr sourceUri,
		IntPtr cancellable,
		delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void> callback,
		IntPtr userData);

	[DllImport(WebKitLibrary)]
	static extern IntPtr webkit_web_view_evaluate_javascript_finish(IntPtr webView, IntPtr result, IntPtr* error);

	[DllImport(JavaScriptCoreLibrary)]
	static extern IntPtr jsc_value_to_string(IntPtr value);

	[DllImport(GObjectLibrary)]
	static extern void g_object_unref(IntPtr instance);

	[DllImport(GLibLibrary)]
	static extern void g_free(IntPtr memory);

	[DllImport(GLibLibrary)]
	static extern void g_error_free(IntPtr error);
}
