using AppKit;
using AVFoundation;
using Foundation;
using ImageKit;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using UniformTypeIdentifiers;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

#pragma warning disable CS0618 // Obsolete members required by interface
class MediaPickerImplementation : IMediaPicker
{
    readonly MediaCaptureCoordinator _capture;

    public MediaPickerImplementation()
        : this(new AVFoundationMediaCaptureProvider())
    {
    }

    internal MediaPickerImplementation(IMacOSMediaCaptureProvider captureProvider) =>
        _capture = new(captureProvider);

    public bool IsCaptureSupported => _capture.IsSupported;

    public async Task<FileResult?> PickPhotoAsync(MediaPickerOptions? options = null)
    {
        var result = await PickFileWithTypes(GetImageTypes());
        return result;
    }

    public async Task<List<FileResult>> PickPhotosAsync(MediaPickerOptions? options = null)
    {
        var results = await PickFilesWithTypes(GetImageTypes(), options?.SelectionLimit ?? 0);
        return results;
    }

    public async Task<FileResult?> CapturePhotoAsync(MediaPickerOptions? options = null)
    {
        var path = await _capture.CapturePhotoAsync(options?.Title);
        return path is null ? null : new FileResult(path);
    }

    public async Task<FileResult?> PickVideoAsync(MediaPickerOptions? options = null)
    {
        var result = await PickFileWithTypes(GetVideoTypes());
        return result;
    }

    public async Task<List<FileResult>> PickVideosAsync(MediaPickerOptions? options = null)
    {
        var results = await PickFilesWithTypes(GetVideoTypes(), options?.SelectionLimit ?? 0);
        return results;
    }

    public async Task<FileResult?> CaptureVideoAsync(MediaPickerOptions? options = null)
    {
        var path = await _capture.CaptureVideoAsync(options?.Title);
        return path is null ? null : new FileResult(path);
    }

    static Task<FileResult?> PickFileWithTypes(UTType[] allowedTypes)
    {
        var tcs = new TaskCompletionSource<FileResult?>();

        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            var panel = NSOpenPanel.OpenPanel;
            panel.CanChooseFiles = true;
            panel.CanChooseDirectories = false;
            panel.AllowsMultipleSelection = false;
            panel.AllowedContentTypes = allowedTypes;

            var result = panel.RunModal();
            if (result == 1 && panel.Url?.Path is not null)
                tcs.TrySetResult(new FileResult(panel.Url.Path));
            else
                tcs.TrySetResult(null);
        });

        return tcs.Task;
    }

    static Task<List<FileResult>> PickFilesWithTypes(UTType[] allowedTypes, int selectionLimit)
    {
        var tcs = new TaskCompletionSource<List<FileResult>>();

        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            var panel = NSOpenPanel.OpenPanel;
            panel.CanChooseFiles = true;
            panel.CanChooseDirectories = false;
            panel.AllowsMultipleSelection = selectionLimit != 1;
            panel.AllowedContentTypes = allowedTypes;

            var result = panel.RunModal();
            if (result == 1 && panel.Urls?.Length > 0)
            {
                var results = panel.Urls
                    .Where(u => u.Path is not null)
                    .Select(u => new FileResult(u.Path!))
                    .ToList();
                tcs.TrySetResult(results);
            }
            else
            {
                tcs.TrySetResult(new List<FileResult>());
            }
        });

        return tcs.Task;
    }

    static UTType[] GetImageTypes() => new[]
    {
        UTType.CreateFromIdentifier("public.image")!
    };

    static UTType[] GetVideoTypes() => new[]
    {
        UTType.CreateFromIdentifier("public.movie")!
    };
}

internal sealed class AVFoundationMediaCaptureProvider : IMacOSMediaCaptureProvider
{
    const string CameraUsageDescriptionKey = "NSCameraUsageDescription";

    public bool IsSupported =>
        HasCameraUsageDescription() &&
        AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video) is not null;

    public async Task<string?> CapturePhotoAsync(string? title)
    {
        await EnsureCameraPermissionAsync();

        var completion = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            try
            {
                var taker = IKPictureTaker.SharedPictureTaker;
                using var allowVideo = NSNumber.FromBoolean(true);
                using var allowFiles = NSNumber.FromBoolean(false);
                taker.SetValueForKey(allowVideo, IKPictureTaker.AllowsVideoCaptureKey);
                taker.SetValueForKey(allowFiles, IKPictureTaker.AllowsFileChoosingKey);

                var response = taker.RunModal();
                if (response != (nint)NSModalResponse.OK)
                {
                    completion.TrySetResult(null);
                    return;
                }

                using var image = taker.GetOutputImage();
                using var tiff = image?.AsTiff();
                using var bitmap = tiff is null ? null : NSBitmapImageRep.ImageRepFromData(tiff) as NSBitmapImageRep;
                using var data = bitmap?.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png);
                if (data is null)
                    throw new InvalidOperationException("ImageKit did not return an encodable captured image.");

                var path = Path.Combine(
                    FileSystem.CacheDirectory,
                    $"capture-{Guid.NewGuid():N}.png");
                using var url = NSUrl.FromFilename(path);
                if (!data.Save(url, true))
                    throw new IOException($"The captured image could not be written to '{path}'.");

                completion.TrySetResult(path);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return await completion.Task;
    }

    public async Task<string?> CaptureVideoAsync(string? title)
    {
        await EnsureCameraPermissionAsync();

        var completion = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(async () =>
        {
            try
            {
                completion.TrySetResult(await CaptureVideoOnMainThreadAsync(title));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return await completion.Task;
    }

    static async Task<string?> CaptureVideoOnMainThreadAsync(string? title)
    {
        var camera = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video)
            ?? throw new FeatureNotSupportedException("No video capture device is available.");
        using var input = AVCaptureDeviceInput.FromDevice(camera, out var inputError)
            ?? throw new InvalidOperationException(
                $"The camera could not be opened: {inputError?.LocalizedDescription ?? "unknown error"}");
        using var session = new AVCaptureSession();
        using var output = new AVCaptureMovieFileOutput();

        if (!session.CanAddInput(input))
            throw new InvalidOperationException("The selected camera cannot be added to a capture session.");
        session.AddInput(input);

        if (!session.CanAddOutput(output))
            throw new InvalidOperationException("A movie-file output cannot be added to the capture session.");
        session.AddOutput(output);

        var outputPath = Path.Combine(
            FileSystem.CacheDirectory,
            $"capture-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.mov");
        using var outputUrl = NSUrl.FromFilename(outputPath);
        var recordingDelegate = new VideoRecordingDelegate();

        try
        {
            session.StartRunning();
            output.StartRecordingToOutputFile(outputUrl, recordingDelegate);

            var alert = new NSAlert
            {
                MessageText = title ?? "Capture Video",
                InformativeText = "Recording from the default camera.",
                AlertStyle = NSAlertStyle.Informational,
            };
            alert.AddButton("Stop Recording");
            alert.AddButton("Cancel");

            var response = alert.RunModal();
            var cancelled = response != (nint)NSAlertButtonReturn.First;
            output.StopRecording();

            var recordingError = await recordingDelegate.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            if (!RecordingFinishedSuccessfully(recordingError))
                throw new InvalidOperationException(
                    $"macOS video capture failed: {recordingError!.LocalizedDescription}");

            if (cancelled)
            {
                DeleteIfExists(outputPath);
                return null;
            }

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                throw new InvalidOperationException(
                    "macOS video capture completed without producing a non-empty movie file.");

            return outputPath;
        }
        catch
        {
            DeleteIfExists(outputPath);
            throw;
        }
        finally
        {
            if (output.Recording)
                output.StopRecording();
            if (session.Running)
                session.StopRunning();
        }
    }

    static bool RecordingFinishedSuccessfully(NSError? error)
    {
        if (error is null)
            return true;

        return error.UserInfo?[AVErrorKeys.RecordingSuccessfullyFinished] is NSNumber value &&
            value.BoolValue;
    }

    static async Task EnsureCameraPermissionAsync()
    {
        if (!HasCameraUsageDescription())
            throw new InvalidOperationException(
                "The app manifest must define NSCameraUsageDescription before requesting camera access.");
        if (AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video) is null)
            throw new FeatureNotSupportedException("No video capture device is available.");

        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        if (status == AVAuthorizationStatus.NotDetermined)
        {
            if (!await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video))
                throw new PermissionException("Camera access was denied.");
            return;
        }

        if (status is AVAuthorizationStatus.Denied or AVAuthorizationStatus.Restricted)
            throw new PermissionException("Camera access is denied or restricted.");
    }

    static bool HasCameraUsageDescription()
    {
        using var key = new NSString(CameraUsageDescriptionKey);
        return NSBundle.MainBundle.InfoDictionary?[key] is NSString value &&
            !string.IsNullOrWhiteSpace(value.ToString());
    }

    static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    sealed class VideoRecordingDelegate : AVCaptureFileOutputRecordingDelegate
    {
        readonly TaskCompletionSource<NSError?> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<NSError?> Completion => _completion.Task;

        public override void FinishedRecording(
            AVCaptureFileOutput captureOutput,
            NSUrl outputFileUrl,
            NSObject[] connections,
            NSError? error) =>
            _completion.TrySetResult(error);
    }
}
#pragma warning restore CS0618
