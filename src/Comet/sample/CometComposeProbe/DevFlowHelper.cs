using System;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Dispatching;
#endif
#pragma warning disable CA1416

namespace CometComposeProbe
{
	// PixelCopyListener lives outside #if DEBUG so Xamarin.Android generates its JNI typemap
	// entry unconditionally. The build tool only registers Java.Lang.Object subclasses it sees
	// in every build; a type hidden behind #if DEBUG is skipped in the Release codegen pass.
	sealed class PixelCopyListener : Java.Lang.Object, PixelCopy.IOnPixelCopyFinishedListener
	{
		readonly Action<int> _callback;
		public PixelCopyListener(Action<int> callback) => _callback = callback;
		public void OnPixelCopyFinished(int copyResult) => _callback(copyResult);
	}

#if DEBUG
	/// <summary>
	/// Bootstraps the DevFlow in-app agent for CometComposeProbe without requiring UseMaui.
	/// Uses DevFlowAgentService.StartServerOnly (designed for Comet apps where
	/// Application.Current is unavailable) with an Android-native PixelCopy screenshot.
	/// </summary>
	static class DevFlowHelper
	{
		static ComposeProbeAgentService? _agent;

		public static void Start(Activity activity, View rootView)
		{
			var dispatcher = new ActivityDispatcher();

			_ = Task.Run(async () =>
			{
				try
				{
					var broker = new BrokerRegistration(
						project: "CometComposeProbe",
						tfm: "net11.0-android",
						platform: "Android",
						appName: "CometComposeProbe");

					var assignedPort = await broker.TryRegisterAsync(TimeSpan.FromSeconds(5));
					var options = new AgentOptions { Port = assignedPort ?? AgentOptions.DefaultPort };

					var agent = new ComposeProbeAgentService(activity, options);
					agent.SetBrokerRegistration(broker);
					broker.CurrentPort = agent.Port;
					_agent = agent;

					dispatcher.Dispatch(() => agent.StartServerOnly(dispatcher));
					Console.WriteLine($"[CometComposeProbe] DevFlow agent started on port {agent.Port}");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[CometComposeProbe] DevFlow startup failed: {ex.Message}");
				}
			});
		}
	}

	/// <summary>
	/// DevFlowAgentService subclass for CometComposeProbe.
	/// Provides Android PixelCopy screenshot (captures GPU-rendered Compose content).
	/// Visual tree walking returns empty results (no MAUI app) — screenshot and
	/// coordinate-based tap are fully functional.
	/// </summary>
	sealed class ComposeProbeAgentService : DevFlowAgentService
	{
		readonly Activity _activity;

		public ComposeProbeAgentService(Activity activity, AgentOptions? options = null)
			: base(options)
		{
			_activity = activity;
		}

		protected override async Task<HttpResponse> HandleScreenshot(HttpRequest request)
		{
			try
			{
				var pngData = await CaptureFullScreenAsync();
				if (pngData != null)
					return HttpResponse.Png(pngData);
				return HttpResponse.Error("PixelCopy returned null");
			}
			catch (Exception ex)
			{
				return HttpResponse.Error($"Screenshot failed: {ex.Message}");
			}
		}

		protected override Task<byte[]?> CaptureFullScreenAsync()
		{
			var tcs = new TaskCompletionSource<byte[]?>();

			// RunOnUiThread: PixelCopyListener (Java.Lang.Object) must be instantiated on a
			// JNI-attached thread. The HTTP handler thread pool is not registered with the JVM.
			_activity.RunOnUiThread(() =>
			{
				try
				{
					var win = _activity.Window;
					var decorView = win?.DecorView;
					if (win == null || decorView == null || decorView.Width <= 0 || decorView.Height <= 0)
					{
						tcs.SetResult(null);
						return;
					}

					var bmp = Bitmap.CreateBitmap(decorView.Width, decorView.Height, Bitmap.Config.Argb8888!)!;

					if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
					{
						PixelCopy.Request(
							win,
							bmp,
							new PixelCopyListener(result =>
							{
								try
								{
									if (result == 0)
									{
										using var ms = new MemoryStream();
										bmp.Compress(Bitmap.CompressFormat.Png!, 90, ms);
										tcs.SetResult(ms.ToArray());
									}
									else
									{
										Android.Util.Log.Warn("CometProbe", $"PixelCopy failed: {result}");
										tcs.SetResult(null);
									}
								}
								finally { bmp.Recycle(); }
							}),
							new Handler(Looper.MainLooper!));
					}
					else
					{
						try
						{
							decorView.Draw(new Canvas(bmp));
							using var ms = new MemoryStream();
							bmp.Compress(Bitmap.CompressFormat.Png!, 90, ms);
							tcs.SetResult(ms.ToArray());
						}
						catch (Exception ex)
						{
							Android.Util.Log.Warn("CometProbe", $"Canvas fallback failed: {ex.Message}");
							tcs.SetResult(null);
						}
						finally { bmp.Recycle(); }
					}
				}
				catch (Exception ex)
				{
					tcs.TrySetResult(null);
					Android.Util.Log.Warn("CometProbe", $"CaptureFullScreen failed: {ex.Message}");
				}
			});

			return tcs.Task;
		}
	}

	sealed class ActivityDispatcher : IDispatcher
	{
		readonly Handler _handler = new(Looper.MainLooper!);

		public bool IsDispatchRequired => Looper.MyLooper() != Looper.MainLooper;
		public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
		public bool Dispatch(Action action) { _handler.Post(action); return true; }
		public bool DispatchDelayed(TimeSpan delay, Action action)
		{
			_handler.PostDelayed(action, (long)delay.TotalMilliseconds);
			return true;
		}
	}
#endif
}

#pragma warning restore CA1416
