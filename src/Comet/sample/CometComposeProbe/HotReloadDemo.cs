#if DEBUG
using Android.Content;
using Comet;

namespace CometComposeProbe
{
	/// <summary>
	/// The app root as a [Body] view — the shape a real Comet app has, and what
	/// <c>MauiHotReloadHelper.TriggerReload</c> targets (it reloads root views, so a
	/// bare static-built tree would never re-render on hot reload).
	/// </summary>
	public class JetchatRoot : View
	{
		[Body]
		View body() => CometSamples.Jetchat.JetchatConversation.Build(topInset: 24);
	}

	/// <summary>
	/// Stand-in for the EnC-updated JetchatRoot: property-level deltas (inset + root
	/// background) make a successful reload unmistakable on screen while every static
	/// signal (composer text, drawer state, selector) carries over untouched.
	/// </summary>
	public class JetchatRootV2 : View
	{
		[Body]
		View body() => CometSamples.Jetchat.JetchatConversation.Build(topInset: 140)
			.Background(Microsoft.Maui.Graphics.Color.FromArgb("#B3261E"));
	}

	/// <summary>
	/// Demo trigger standing in for the .NET MetadataUpdateHandler: registers the
	/// replacement type and triggers the reload — exactly the two calls
	/// <c>CometMetadataUpdateHandler.UpdateType/UpdateApplication</c> make when the
	/// runtime applies an EnC delta. Fire with:
	/// <c>adb shell am broadcast -n com.comet.composeprobe/.HotReloadDemoReceiver</c>
	/// </summary>
	[BroadcastReceiver(Enabled = true, Exported = true, Name = "com.comet.composeprobe.HotReloadDemoReceiver")]
	public class HotReloadDemoReceiver : BroadcastReceiver
	{
		public override void OnReceive(Context? context, Intent? intent)
		{
			Android.Util.Log.Info("CometProbe", "HotReload demo: registering JetchatRootV2 + TriggerReload");
			Comet.HotReload.CometHotReloadHelper.RegisterReplacedView(
				typeof(JetchatRoot).FullName!, typeof(JetchatRootV2));
			ThreadHelper.RunOnMainThread(() =>
			{
				var before = MainActivity.RootView?.BuiltView;
				Microsoft.Maui.HotReload.MauiHotReloadHelper.TriggerReload();
				var after = MainActivity.RootView?.BuiltView;
				Android.Util.Log.Info("CometProbe",
					$"HotReload demo: built {before?.GetType().Name}#{before?.GetHashCode()} -> {after?.GetType().Name}#{after?.GetHashCode()} (changed={!ReferenceEquals(before, after)})");
			});
		}
	}
}
#endif
