#nullable enable
using Android.Content;

namespace Comet.Compose.Facade
{
	/// <summary>
	/// Phase 1 risk probe: confirms the core Jetpack Compose types are reachable from
	/// C# through the Xamarin.AndroidX.Compose bindings — the host view (ComposeView),
	/// reactive state (MutableState), and the composition entry point. Compiling this
	/// file is the de-risk signal; the JNI composable-lambda bridge follows.
	/// </summary>
	static class InteropProbe
	{
		public static AndroidX.Compose.UI.Platform.ComposeView CreateHost(Context context)
		{
			// The Android View that hosts a Compose composition as activity/fragment content.
			var view = new AndroidX.Compose.UI.Platform.ComposeView(context);
			return view;
		}

		public static AndroidX.Compose.Runtime.IMutableState CreateState()
		{
			// Snapshot-backed observable state: the bridge writes these to drive recomposition.
			// Kotlin defaults `policy` to structuralEqualityPolicy(); the binding exposes the
			// full-arity overload, so we pass it explicitly (this is the $default gap the
			// facade's source generator will paper over for ergonomic call sites).
			return AndroidX.Compose.Runtime.SnapshotStateKt.MutableStateOf(
				new Java.Lang.String("initial"),
				AndroidX.Compose.Runtime.SnapshotStateKt.StructuralEqualityPolicy());
		}
	}
}
