#if IOS || MACCATALYST
using Foundation;
using UIKit;
using VisionKit;

namespace DocumentExtractionSample.Services;

/// <summary>Presents <see cref="VNDocumentCameraViewController"/> and returns the captured multi-page scan, if any.</summary>
public static class DocumentCameraScanner
{
	private static readonly HashSet<ScannerDelegate> s_activeDelegates = [];

	/// <summary>Gets a value indicating whether the document camera scanner is available on this device.</summary>
	public static bool IsSupported => VNDocumentCameraViewController.Supported;

	/// <summary>Presents the system document camera and awaits the user finishing, cancelling, or failing the scan.
	/// Returns <see langword="null"/> when the user cancels.</summary>
	public static Task<VNDocumentCameraScan?> ScanAsync()
	{
		var presentingController = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController()
			?? throw new InvalidOperationException("No view controller is available to present the document camera.");

		var tcs = new TaskCompletionSource<VNDocumentCameraScan?>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var scannerController = new ScannerController();
		var scannerDelegate = new ScannerDelegate(tcs, scannerController);
		lock (s_activeDelegates)
		{
			s_activeDelegates.Add(scannerDelegate);
		}
		scannerController.Delegate = scannerDelegate;
		scannerController.Disappeared = scannerDelegate.DidDisappear;

		presentingController.PresentViewController(scannerController, animated: true, completionHandler: null);
		return tcs.Task;
	}

	private sealed class ScannerController : VNDocumentCameraViewController
	{
		internal Action? Disappeared { get; set; }

		public override void ViewDidDisappear(bool animated)
		{
			base.ViewDidDisappear(animated);
			Disappeared?.Invoke();
		}
	}

	private sealed class ScannerDelegate(
		TaskCompletionSource<VNDocumentCameraScan?> completionSource,
		ScannerController controller)
		: VNDocumentCameraViewControllerDelegate
	{
		public override void DidFinish(VNDocumentCameraViewController controller, VNDocumentCameraScan scan)
		{
			controller.DismissViewController(animated: true, completionHandler: null);
			completionSource.TrySetResult(scan);
			Release();
		}

		public override void DidCancel(VNDocumentCameraViewController controller)
		{
			controller.DismissViewController(animated: true, completionHandler: null);
			completionSource.TrySetResult(null);
			Release();
		}

		public override void DidFail(VNDocumentCameraViewController controller, NSError error)
		{
			controller.DismissViewController(animated: true, completionHandler: null);
			completionSource.TrySetException(new NSErrorException(error));
			Release();
		}

		internal void DidDisappear()
		{
			if (completionSource.TrySetResult(null))
			{
				Release();
			}
		}

		private void Release()
		{
			controller.Delegate = null;
			controller.Disappeared = null;
			lock (s_activeDelegates)
			{
				s_activeDelegates.Remove(this);
			}
		}
	}
}
#endif
