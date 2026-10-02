#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Net;
using Android.Provider;
using AndroidX.Core.Content;
using CometBaristaNotes.Services;

namespace CometComposeProbe;

public partial class MainActivity
{
    BaristaNotesPhotoService? _baristaPhotos;

    protected override void OnActivityResult(
        int requestCode,
        Result resultCode,
        Intent? data)
    {
        if (_baristaPhotos?.TryHandleActivityResult(requestCode, resultCode, data) == true)
            return;
        base.OnActivityResult(requestCode, resultCode, data);
    }
}

sealed class BaristaNotesPhotoService : IBaristaPhotoService, IDisposable
{
    const int CameraRequestCode = 7311;
    const int GalleryRequestCode = 7312;

    readonly MainActivity _activity;
    readonly IImageProcessingService _imageProcessing = new LocalImageProcessingService();
    readonly object _gate = new();
    PendingRequest? _pending;

    public BaristaNotesPhotoService(MainActivity activity) =>
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));

    public bool IsCameraAvailable =>
        CanResolve(new Intent(MediaStore.ActionImageCapture));

    public bool IsGalleryAvailable =>
        CanResolve(CreateGalleryIntent());

    public Task<PhotoOperationResult> CapturePhotoAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsCameraAvailable)
        {
            return Task.FromResult(PhotoOperationResult.Unavailable(
                "camera_unavailable",
                "No camera application is available on this device."));
        }

        string? cameraPath = null;
        try
        {
            var cameraDirectory = Path.Combine(_activity.CacheDir!.AbsolutePath, "baristanotes-camera");
            Directory.CreateDirectory(cameraDirectory);
            cameraPath = Path.Combine(cameraDirectory, $"capture-{Guid.NewGuid():N}.jpg");
            using (System.IO.File.Create(cameraPath))
            {
            }

            var outputUri = FileProvider.GetUriForFile(
                _activity,
                $"{_activity.PackageName}.baristanotes.files",
                new Java.IO.File(cameraPath));
            var intent = new Intent(MediaStore.ActionImageCapture);
            intent.PutExtra(MediaStore.ExtraOutput, outputUri);
            intent.ClipData = ClipData.NewRawUri("BaristaNotes photo", outputUri);
            intent.AddFlags(
                ActivityFlags.GrantReadUriPermission
                | ActivityFlags.GrantWriteUriPermission);

            return StartAsync(
                CameraRequestCode,
                intent,
                PhotoSource.Camera,
                cancellationToken,
                cameraPath);
        }
        catch (Java.Lang.SecurityException)
        {
            DeleteTemporaryCameraFile(cameraPath);
            return Task.FromResult(PhotoOperationResult.PermissionDenied(
                "Camera permission was denied. Enable camera access in Settings to take a photo."));
        }
        catch (UnauthorizedAccessException)
        {
            DeleteTemporaryCameraFile(cameraPath);
            return Task.FromResult(PhotoOperationResult.PermissionDenied(
                "Camera permission was denied. Enable camera access in Settings to take a photo."));
        }
        catch (Exception ex)
        {
            DeleteTemporaryCameraFile(cameraPath);
            return Task.FromResult(PhotoOperationResult.Error(
                "camera_start_failed",
                $"The camera could not be opened: {ex.Message}"));
        }
    }

    public Task<PhotoOperationResult> PickPhotoAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsGalleryAvailable)
        {
            return Task.FromResult(PhotoOperationResult.Unavailable(
                "gallery_unavailable",
                "No photo gallery is available on this device."));
        }

        try
        {
            return StartAsync(
                GalleryRequestCode,
                CreateGalleryIntent(),
                PhotoSource.Gallery,
                cancellationToken);
        }
        catch (Java.Lang.SecurityException)
        {
            return Task.FromResult(PhotoOperationResult.PermissionDenied(
                "Photo gallery permission was denied. Enable photo access in Settings to choose a photo."));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(PhotoOperationResult.PermissionDenied(
                "Photo gallery permission was denied. Enable photo access in Settings to choose a photo."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(PhotoOperationResult.Error(
                "gallery_start_failed",
                $"The photo gallery could not be opened: {ex.Message}"));
        }
    }

    public bool TryHandleActivityResult(
        int requestCode,
        Result resultCode,
        Intent? data)
    {
        PendingRequest? pending;
        lock (_gate)
        {
            if (_pending is null || _pending.RequestCode != requestCode)
                return false;
            pending = _pending;
            _pending = null;
        }

        pending.CancellationRegistration.Dispose();
        _ = CompleteAsync(pending, resultCode, data);
        return true;
    }

    Task<PhotoOperationResult> StartAsync(
        int requestCode,
        Intent intent,
        PhotoSource source,
        CancellationToken cancellationToken,
        string? cameraPath = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<PhotoOperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingRequest(
            requestCode,
            source,
            completion,
            cameraPath,
            cancellationToken);
        lock (_gate)
        {
            if (_pending is not null)
            {
                DeleteTemporaryCameraFile(cameraPath);
                return Task.FromResult(PhotoOperationResult.Error(
                    "photo_operation_busy",
                    "Another photo operation is already active."));
            }
            _pending = pending;
        }

        pending.CancellationRegistration = cancellationToken.Register(() =>
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_pending, pending))
                    return;
                _pending = null;
            }
            if (source == PhotoSource.Camera)
                DeleteTemporaryCameraFile(pending.CameraPath);
            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            _activity.StartActivityForResult(intent, requestCode);
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, pending))
                    _pending = null;
            }
            pending.CancellationRegistration.Dispose();
            throw;
        }
        return completion.Task;
    }

    async Task CompleteAsync(
        PendingRequest pending,
        Result resultCode,
        Intent? data)
    {
        if (resultCode != Result.Ok)
        {
            if (pending.Source == PhotoSource.Camera)
                DeleteTemporaryCameraFile(pending.CameraPath);
            pending.Completion.TrySetResult(PhotoOperationResult.Cancelled());
            return;
        }

        try
        {
            if (pending.Source == PhotoSource.Camera)
            {
                var path = pending.CameraPath;
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                    throw new IOException("The camera did not return an image file.");
                await using var stream = System.IO.File.OpenRead(path);
                pending.Completion.TrySetResult(
                    await NormalizeAsync(stream, PhotoSource.Camera, pending.CancellationToken));
                return;
            }

            var uri = data?.Data;
            if (uri is null)
                throw new IOException("The photo gallery did not return an image.");
            await using var selected = _activity.ContentResolver!.OpenInputStream(uri)
                ?? throw new IOException("The selected image could not be opened.");
            pending.Completion.TrySetResult(
                await NormalizeAsync(selected, PhotoSource.Gallery, pending.CancellationToken));
        }
        catch (UnauthorizedAccessException ex)
        {
            pending.Completion.TrySetResult(PhotoOperationResult.PermissionDenied(
                $"Photo access was denied: {ex.Message}"));
        }
        catch (Java.Lang.SecurityException ex)
        {
            pending.Completion.TrySetResult(PhotoOperationResult.PermissionDenied(
                $"Photo access was denied: {ex.Message}"));
        }
        catch (Exception ex)
        {
            pending.Completion.TrySetResult(PhotoOperationResult.Error(
                pending.Source == PhotoSource.Camera
                    ? "camera_read_failed"
                    : "gallery_read_failed",
                $"The selected photo could not be read: {ex.Message}"));
        }
        finally
        {
            if (pending.Source == PhotoSource.Camera)
                DeleteTemporaryCameraFile(pending.CameraPath);
        }
    }

    bool CanResolve(Intent intent)
    {
        using (intent)
            return intent.ResolveActivity(_activity.PackageManager!) is not null;
    }

    static Intent CreateGalleryIntent()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.SetType("image/*");
        intent.AddCategory(Intent.CategoryOpenable);
        return intent;
    }

    async Task<PhotoOperationResult> NormalizeAsync(
        Stream source,
        PhotoSource photoSource,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await ReadBytesAsync(source, cancellationToken);
            using var input = new MemoryStream(bytes, writable: false);
            using var normalized = await _imageProcessing.DownsampleAsync(
                input,
                BaristaPhotoNormalization.MaximumDimension,
                BaristaPhotoNormalization.JpegQuality);
            if (normalized is null)
            {
                return PhotoOperationResult.Error(
                    "photo_normalization_failed",
                    "The selected photo is unsupported or could not be normalized.");
            }
            return BaristaPhotoNormalization.CreateResult(normalized.ToArray(), photoSource);
        }
        catch (PhotoTooLargeException)
        {
            return PhotoOperationResult.Error(
                "photo_too_large",
                "The selected photo exceeds the 12 MB source limit.");
        }
    }

    static async Task<byte[]> ReadBytesAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var block = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + read > BaristaPhotoNormalization.MaximumSourceBytes)
                throw new PhotoTooLargeException();
            await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken);
        }
        return buffer.ToArray();
    }

    static void DeleteTemporaryCameraFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }

    public void Dispose()
    {
        PendingRequest? pending;
        lock (_gate)
        {
            pending = _pending;
            _pending = null;
        }
        if (pending is null)
            return;

        pending.CancellationRegistration.Dispose();
        DeleteTemporaryCameraFile(pending.CameraPath);
        pending.Completion.TrySetCanceled();
    }

    sealed class PendingRequest(
        int requestCode,
        PhotoSource source,
        TaskCompletionSource<PhotoOperationResult> completion,
        string? cameraPath,
        CancellationToken cancellationToken)
    {
        public int RequestCode { get; } = requestCode;
        public PhotoSource Source { get; } = source;
        public TaskCompletionSource<PhotoOperationResult> Completion { get; } = completion;
        public string? CameraPath { get; } = cameraPath;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public CancellationTokenRegistration CancellationRegistration { get; set; }
    }

    sealed class PhotoTooLargeException : IOException;
}
