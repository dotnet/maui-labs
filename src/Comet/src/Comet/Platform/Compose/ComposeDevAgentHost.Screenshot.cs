#nullable enable
#if ANDROID
using System;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;

namespace Comet.Platform.Compose
{
	public static partial class ComposeDevAgentHost
	{
		/// <summary>Captures the activity's GPU-rendered window as PNG using PixelCopy (API 26+).</summary>
		public static Task<byte[]?> CaptureScreenshotAsync(Activity activity)
		{
			ArgumentNullException.ThrowIfNull(activity);
			var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);

			// Java peers must be created on a JNI-attached thread, not the HTTP worker.
			activity.RunOnUiThread(() =>
			{
				Bitmap? bitmap = null;
				ComposePixelCopyListener? listener = null;
				Handler? handler = null;

				void Release()
				{
					bitmap?.Recycle();
					bitmap?.Dispose();
					listener?.Dispose();
					handler?.Dispose();
				}

				try
				{
					if (Build.VERSION.SdkInt < BuildVersionCodes.O)
						throw new PlatformNotSupportedException("Window PixelCopy requires Android API 26 or later.");

					var window = activity.Window;
					var decor = window?.DecorView;
					if (activity.IsDestroyed || activity.IsFinishing ||
						window is null || decor is null || decor.Width <= 0 || decor.Height <= 0)
						throw new InvalidOperationException("The activity has no drawable window.");

					bitmap = Bitmap.CreateBitmap(decor.Width, decor.Height, Bitmap.Config.Argb8888!)
						?? throw new InvalidOperationException("Cannot allocate the screenshot bitmap.");
					listener = new ComposePixelCopyListener(result =>
					{
						try
						{
							if (result != 0)
								throw new InvalidOperationException($"Window PixelCopy failed with result {result}.");

							using var stream = new MemoryStream();
							if (!bitmap.Compress(Bitmap.CompressFormat.Png!, 90, stream))
								throw new IOException("Cannot encode the screenshot as PNG.");
							completion.TrySetResult(stream.ToArray());
						}
						catch (Exception ex)
						{
							completion.TrySetException(ex);
						}
						finally
						{
							Release();
						}
					});
					handler = new Handler(Looper.MainLooper!);
					PixelCopy.Request(window, bitmap, listener, handler);
				}
				catch (Exception ex)
				{
					Release();
					completion.TrySetException(ex);
				}
			});

			return completion.Task;
		}
	}

	// Keep the Java peer outside DEBUG guards so Release JNI code generation includes it.
	sealed class ComposePixelCopyListener : Java.Lang.Object, PixelCopy.IOnPixelCopyFinishedListener
	{
		readonly Action<int> _callback;
		public ComposePixelCopyListener(Action<int> callback) => _callback = callback;
		public void OnPixelCopyFinished(int copyResult) => _callback(copyResult);
	}
}
#endif
