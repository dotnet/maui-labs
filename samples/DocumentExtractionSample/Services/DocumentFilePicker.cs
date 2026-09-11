#if IOS || MACCATALYST

using Foundation;
using UIKit;
using UniformTypeIdentifiers;

namespace DocumentExtractionSample.Services;

internal sealed record PickedDocument(string FileName, byte[] Data);

internal static class DocumentFilePicker
{
	private static readonly HashSet<PickerDelegate> s_activeDelegates = [];

	internal static Task<PickedDocument?> PickAsync()
	{
		var presentingController = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController()
			?? throw new InvalidOperationException("No view controller is available to present the document picker.");
		var completion = new TaskCompletionSource<PickedDocument?>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var controller = new UIDocumentPickerViewController(
			[UTTypes.Image, UTTypes.Pdf],
			asCopy: true)
		{
			AllowsMultipleSelection = false,
		};
		var pickerDelegate = new PickerDelegate(completion, controller);
		lock (s_activeDelegates)
		{
			s_activeDelegates.Add(pickerDelegate);
		}
		controller.Delegate = pickerDelegate;
		presentingController.PresentViewController(controller, animated: true, completionHandler: null);
		return completion.Task;
	}

	private sealed class PickerDelegate(
		TaskCompletionSource<PickedDocument?> completion,
		UIDocumentPickerViewController controller)
		: UIDocumentPickerDelegate
	{
		public override async void DidPickDocument(
			UIDocumentPickerViewController controller,
			NSUrl[] urls)
		{
			if (urls.FirstOrDefault() is not { } url)
			{
				Complete(null);
				return;
			}

			var hasSecurityScope = url.StartAccessingSecurityScopedResource();
			try
			{
				var path = url.Path
					?? throw new InvalidOperationException("The selected file does not have a local path.");
				var data = await File.ReadAllBytesAsync(path);
				Complete(new PickedDocument(url.LastPathComponent ?? Path.GetFileName(path), data));
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
				Release();
			}
			finally
			{
				if (hasSecurityScope)
				{
					url.StopAccessingSecurityScopedResource();
				}
			}
		}

		public override void WasCancelled(UIDocumentPickerViewController controller) =>
			Complete(null);

		private void Complete(PickedDocument? document)
		{
			completion.TrySetResult(document);
			Release();
		}

		private void Release()
		{
			controller.Delegate = null!;
			lock (s_activeDelegates)
			{
				s_activeDelegates.Remove(this);
			}
		}
	}
}

#endif
