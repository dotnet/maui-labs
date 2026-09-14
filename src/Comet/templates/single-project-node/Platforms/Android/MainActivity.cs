using Android.App;
using Android.OS;
using Comet;
using Comet.Platform.Compose;

namespace CometNodeApp1;

[Activity(Label = "CometNodeApp1", MainLauncher = true)]
public class MainActivity : AndroidX.Activity.ComponentActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		// Comet's fluent env writes marshal through ThreadHelper; run them on the UI thread.
		ThreadHelper.SetFireOnMainThread(a => RunOnUiThread(a));

		var backend = new ComposeBackendRoot(new App.Services())
		{
			UseYogaLayout = true,
			// Material 3 components (Button, ripples) read their colors from a MaterialTheme
			// ancestor — wrap the composed content in one (default scheme here).
			WrapContent = content =>
			{
				var theme = new AndroidX.Compose.MaterialTheme();
				theme.Add(content);
				return theme;
			},
		};

		// topInset clears the status bar (the node backend doesn't apply safe-area insets yet).
		SetContentView(backend.CreateView(this, App.Build(topInset: 24)));
	}
}
