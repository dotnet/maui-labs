namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

public static class GtkWebViewSignals
{
	public static readonly GObject.Signal<WebKit.WebView, LoadChangedArgs> LoadChanged =
		new("load-changed", "LoadChanged");
	public static readonly GObject.ReturningSignal<WebKit.WebView, LoadFailedArgs, bool> LoadFailed =
		new("load-failed", "LoadFailed");
	public static readonly GObject.Signal<WebKit.UserContentManager, ScriptMessageArgs> ScriptMessageReceived =
		new("script-message-received", "ScriptMessageReceived");

	public abstract class OwnedArgs : GObject.SignalArgs, IDisposable
	{
		public void Dispose()
		{
			// GirCore 0.8.1 copies signal GValues, including the sender. WebKit must
			// release these references on the GTK thread, not in GValue finalizers.
			foreach (var value in Args)
				value.Dispose();
		}
	}

	public sealed class LoadChangedArgs : OwnedArgs
	{
		public WebKit.LoadEvent LoadEvent => (WebKit.LoadEvent)Extract<int>(Args[1]);
	}

	public sealed class LoadFailedArgs : OwnedArgs
	{
		public string FailingUri => Extract<string>(Args[2]);
	}

	public sealed class ScriptMessageArgs : OwnedArgs
	{
		public JavaScriptCore.Value Value => Extract<JavaScriptCore.Value>(Args[1]);
	}
}
