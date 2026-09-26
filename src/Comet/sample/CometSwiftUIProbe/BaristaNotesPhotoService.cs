#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using AVFoundation;
using CometBaristaNotes.Services;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using PhotosUI;
using UIKit;

namespace CometSwiftUIProbe;

sealed class BaristaNotesPhotoService : IBaristaPhotoService, IDisposable
{
    readonly Func<UIWindow?> _window;
    readonly object _gate = new();
    PendingRequest? _pending;

    public BaristaNotesPhotoService(Func<UIWindow?> window) =>
        _window = window ?? throw new ArgumentNullException(nameof(window));

    public bool IsCameraAvailable =>
        Runtime.Arch != Arch.SIMULATOR
        && UIImagePickerController.IsSourceTypeAvailable(UIImagePickerControllerSourceType.Camera);

    public bool IsGalleryAvailable => true;

    public async Task<PhotoOperationResult> CapturePhotoAsync(
        CancellationToken cancellationToken = default)
    {
        if (Runtime.Arch == Arch.SIMULATOR)
        {
            return PhotoOperationResult.Unavailable(
                "camera_unavailable",
                "Camera capture is not available in the iOS simulator.");
        }

        if (!IsCameraAvailable)
        {
            return PhotoOperationResult.Unavailable(
                "camera_unavailable",
                "Camera capture is not available on this device.");
        }

        var authorization = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        if (authorization == AVAuthorizationStatus.NotDetermined)
        {
            var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
            authorization = granted
                ? AVAuthorizationStatus.Authorized
                : AVAuthorizationStatus.Denied;
        }
        if (authorization != AVAuthorizationStatus.Authorized)
        {
            return PhotoOperationResult.PermissionDenied(
                "Camera permission was denied. Enable camera access in Settings to take a photo.");
        }

        return await StartPickerAsync(
            UIImagePickerControllerSourceType.Camera,
            PhotoSource.Camera,
            cancellationToken);
    }

    public async Task<PhotoOperationResult> PickPhotoAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsGalleryAvailable)
        {
            return PhotoOperationResult.Unavailable(
                "gallery_unavailable",
                "Photo library selection is not available on this device.");
        }

        return await StartGalleryAsync(cancellationToken);
    }

    Task<PhotoOperationResult> StartPickerAsync(
        UIImagePickerControllerSourceType sourceType,
        PhotoSource source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<PhotoOperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            lock (_gate)
            {
                if (_pending is not null)
                {
                    completion.TrySetResult(PhotoOperationResult.Error(
                        "photo_operation_busy",
                        "Another photo operation is already active."));
                    return;
                }
            }

            var presenter = FindPresenter();
            if (presenter is null)
            {
                completion.TrySetResult(PhotoOperationResult.Error(
                    "presentation_unavailable",
                    "The photo picker could not find an active view controller."));
                return;
            }

            var picker = new UIImagePickerController
            {
                SourceType = sourceType,
                AllowsEditing = false,
                MediaTypes = ["public.image"],
            };
            var pickerDelegate = new CameraPickerDelegate(this);
            picker.Delegate = pickerDelegate;
            var pending = new PendingRequest(source, picker, pickerDelegate, completion);
            lock (_gate)
                _pending = pending;

            pending.CancellationRegistration = cancellationToken.Register(() =>
            {
                pending.TryCancelBeforePresentation();
                PendingRequest? cancelled = null;
                lock (_gate)
                {
                    if (ReferenceEquals(_pending, pending))
                    {
                        cancelled = _pending;
                        _pending = null;
                    }
                }
                if (cancelled is not null)
                {
                    UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                    {
                        cancelled.Picker.DismissViewController(true, null);
                        cancelled.Dispose();
                    });
                }
                completion.TrySetCanceled(cancellationToken);
            });

            try
            {
                if (CancelBeforePresentation(pending, cancellationToken))
                    return;
                presenter.PresentViewController(picker, true, null);
            }
            catch (Exception ex)
            {
                var ownsPending = false;
                lock (_gate)
                {
                    if (ReferenceEquals(_pending, pending))
                    {
                        _pending = null;
                        ownsPending = true;
                    }
                }
                if (ownsPending)
                    pending.Dispose();
                completion.TrySetResult(PhotoOperationResult.Error(
                    "presentation_failed",
                    $"The photo picker could not be presented: {ex.Message}"));
            }
        });

        return completion.Task;
    }

    Task<PhotoOperationResult> StartGalleryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<PhotoOperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            lock (_gate)
            {
                if (_pending is not null)
                {
                    completion.TrySetResult(PhotoOperationResult.Error(
                        "photo_operation_busy",
                        "Another photo operation is already active."));
                    return;
                }
            }

            var presenter = FindPresenter();
            if (presenter is null)
            {
                completion.TrySetResult(PhotoOperationResult.Error(
                    "presentation_unavailable",
                    "The photo picker could not find an active view controller."));
                return;
            }

            var configuration = new PHPickerConfiguration
            {
                Filter = PHPickerFilter.ImagesFilter,
                SelectionLimit = 1,
            };
            var picker = new PHPickerViewController(configuration);
            var pickerDelegate = new GalleryPickerDelegate(this);
            picker.WeakDelegate = pickerDelegate;
            var pending = new PendingRequest(
                PhotoSource.Gallery,
                picker,
                pickerDelegate,
                completion);
            lock (_gate)
                _pending = pending;

            pending.CancellationRegistration = cancellationToken.Register(() =>
            {
                pending.TryCancelBeforePresentation();
                PendingRequest? cancelled = null;
                lock (_gate)
                {
                    if (ReferenceEquals(_pending, pending))
                    {
                        cancelled = _pending;
                        _pending = null;
                    }
                }
                if (cancelled is not null)
                {
                    UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                    {
                        cancelled.Picker.DismissViewController(true, null);
                        cancelled.Dispose();
                    });
                }
                completion.TrySetCanceled(cancellationToken);
            });

            try
            {
                if (CancelBeforePresentation(pending, cancellationToken))
                    return;
                presenter.PresentViewController(picker, true, null);
            }
            catch (Exception ex)
            {
                var ownsPending = false;
                lock (_gate)
                {
                    if (ReferenceEquals(_pending, pending))
                    {
                        _pending = null;
                        ownsPending = true;
                    }
                }
                if (ownsPending)
                    pending.Dispose();
                completion.TrySetResult(PhotoOperationResult.Error(
                    "presentation_failed",
                    $"The photo picker could not be presented: {ex.Message}"));
            }
        });

        return completion.Task;
    }

    bool CancelBeforePresentation(
        PendingRequest pending,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            pending.TryCancelBeforePresentation();
        if (pending.TryBeginPresentation())
            return false;

        var ownsPending = false;
        lock (_gate)
        {
            if (ReferenceEquals(_pending, pending))
            {
                _pending = null;
                ownsPending = true;
            }
        }
        if (ownsPending)
            pending.Dispose();
        pending.Completion.TrySetCanceled(cancellationToken);
        return true;
    }

    void Complete(UIImage? image)
    {
        var pending = TakePending();
        if (pending is null)
            return;
        Complete(pending, image, null);
    }

    void Complete(PendingRequest pending, UIImage? image, string? nativeError)
    {
        try
        {
            if (image is null)
            {
                pending.Completion.TrySetResult(PhotoOperationResult.Error(
                    "photo_read_failed",
                    nativeError ?? "The selected photo could not be decoded."));
                return;
            }

            var pixelWidth = Math.Max(1, (int)Math.Round(image.Size.Width * image.CurrentScale));
            var pixelHeight = Math.Max(1, (int)Math.Round(image.Size.Height * image.CurrentScale));
            var target = BaristaPhotoNormalization.FitWithin(pixelWidth, pixelHeight);
            using var scaled = target.Width != pixelWidth || target.Height != pixelHeight
                ? ResizeImage(image, target.Width, target.Height)
                : null;
            var normalizedImage = scaled ?? image;
            using var jpeg = normalizedImage.AsJPEG(
                (nfloat)(BaristaPhotoNormalization.JpegQuality / 100d));
            if (jpeg is null)
            {
                pending.Completion.TrySetResult(PhotoOperationResult.Error(
                    "photo_encode_failed",
                    "The selected photo could not be converted to JPEG."));
                return;
            }

            pending.Completion.TrySetResult(
                BaristaPhotoNormalization.CreateResult(jpeg.ToArray(), pending.Source));
        }
        catch (Exception ex)
        {
            pending.Completion.TrySetResult(PhotoOperationResult.Error(
                "photo_read_failed",
                $"The selected photo could not be read: {ex.Message}"));
        }
        finally
        {
            pending.Picker.DismissViewController(true, null);
            pending.Dispose();
        }
    }

    static UIImage ResizeImage(UIImage image, int width, int height)
    {
        using var format = UIGraphicsImageRendererFormat.DefaultFormat;
        format.Scale = 1;
        format.Opaque = true;
        using var renderer = new UIGraphicsImageRenderer(new CGSize(width, height), format);
        return renderer.CreateImage(_ => image.Draw(new CGRect(0, 0, width, height)));
    }

    void CompleteGallery(PHPickerResult[] results)
    {
        var pending = TakePending();
        if (pending is null)
            return;
        if (results.Length == 0)
        {
            Cancel(pending);
            return;
        }

        var provider = results[0].ItemProvider;
        if (!provider.CanLoadObject(typeof(UIImage)))
        {
            Complete(pending, null, "The selected item is not a supported image.");
            return;
        }

        try
        {
            provider.LoadObject<UIImage>((image, error) =>
                UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                    Complete(pending, image, error?.LocalizedDescription)));
        }
        catch (Exception ex)
        {
            Complete(pending, null, $"The selected photo could not be loaded: {ex.Message}");
        }
    }

    void Cancel()
    {
        var pending = TakePending();
        if (pending is null)
            return;
        Cancel(pending);
    }

    static void Cancel(PendingRequest pending)
    {
        pending.Completion.TrySetResult(PhotoOperationResult.Cancelled());
        pending.Picker.DismissViewController(true, null);
        pending.Dispose();
    }

    PendingRequest? TakePending()
    {
        lock (_gate)
        {
            var pending = _pending;
            _pending = null;
            return pending;
        }
    }

    public void Dispose()
    {
        var pending = TakePending();
        if (pending is null)
            return;

        pending.Completion.TrySetCanceled();
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            pending.Picker.DismissViewController(false, null);
            pending.Dispose();
        });
    }

    UIViewController? FindPresenter()
    {
        var controller = _window()?.RootViewController;
        while (controller?.PresentedViewController is { } presented)
            controller = presented;
        return controller;
    }

    sealed class CameraPickerDelegate(BaristaNotesPhotoService owner)
        : UIImagePickerControllerDelegate, IUINavigationControllerDelegate
    {
        public override void FinishedPickingMedia(
            UIImagePickerController picker,
            NSDictionary info)
        {
            owner.Complete(info[UIImagePickerController.OriginalImage] as UIImage);
        }

        public override void Canceled(UIImagePickerController picker) =>
            owner.Cancel();
    }

    sealed class GalleryPickerDelegate(BaristaNotesPhotoService owner)
        : PHPickerViewControllerDelegate
    {
        public override void DidFinishPicking(
            PHPickerViewController picker,
            PHPickerResult[] results) =>
            owner.CompleteGallery(results);
    }

    sealed class PendingRequest(
        PhotoSource source,
        UIViewController picker,
        NSObject pickerDelegate,
        TaskCompletionSource<PhotoOperationResult> completion) : IDisposable
    {
        public PhotoSource Source { get; } = source;
        public UIViewController Picker { get; } = picker;
        public NSObject PickerDelegate { get; } = pickerDelegate;
        public TaskCompletionSource<PhotoOperationResult> Completion { get; } = completion;
        public CancellationTokenRegistration CancellationRegistration { get; set; }
        readonly BaristaPhotoNormalization.PhotoPresentationGate _presentationGate = new();

        public bool TryBeginPresentation() =>
            _presentationGate.TryBeginPresentation();

        public bool TryCancelBeforePresentation() =>
            _presentationGate.TryCancelBeforePresentation();

        public void Dispose()
        {
            CancellationRegistration.Dispose();
            if (Picker is UIImagePickerController cameraPicker)
                cameraPicker.Delegate = null;
            else if (Picker is PHPickerViewController galleryPicker)
                galleryPicker.WeakDelegate = null;
            Picker.Dispose();
            PickerDelegate.Dispose();
        }
    }
}
