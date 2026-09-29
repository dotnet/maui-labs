using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using ImageIO;
using PdfKit;
using UIKit;
using UniformTypeIdentifiers;
using VisionKit;
#endif

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Loads images and PDFs from the system picker or VisionKit scanner.</summary>
public sealed class DocumentInputService
{
    private const int MaximumDocumentBytes = 75 * 1024 * 1024;

    private static readonly FilePickerFileType s_imageOrPdfFileType = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = ["image/*", "application/pdf"],
            [DevicePlatform.iOS] = ["public.image", "com.adobe.pdf"],
            [DevicePlatform.MacCatalyst] = ["public.image", "com.adobe.pdf"],
            [DevicePlatform.WinUI] = [".png", ".jpg", ".jpeg", ".heic", ".tif", ".tiff", ".pdf"],
        });

#if IOS || MACCATALYST
    private static readonly HashSet<PickerDelegate> s_activePickerDelegates = [];
    private static readonly HashSet<ScannerDelegate> s_activeScannerDelegates = [];
#endif

    public bool CanScan =>
#if IOS || MACCATALYST
        VNDocumentCameraViewController.Supported;
#else
        false;
#endif

    public async Task<DocumentInput?> PickAsync(CancellationToken cancellationToken = default)
    {
#if IOS || MACCATALYST
        return await PickAppleDocumentAsync(cancellationToken);
#else
        FileResult? result;
        try
        {
            result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select an image or PDF",
                FileTypes = s_imageOrPdfFileType,
            });
        }
        catch (PermissionException exception)
        {
            throw new InvalidOperationException(
                "File access was denied. Allow file access in the platform settings and try again.",
                exception);
        }

        if (result is null)
            return null;

        var mediaType = GetMediaType(result.FileName);
        if (mediaType is null)
            throw new InvalidOperationException("Choose a PNG, JPEG, HEIC, TIFF, or PDF document.");

        await using var stream = await result.OpenReadAsync();
        return await CreateInputAsync(
            result.FileName,
            mediaType,
            await ReadBytesAsync(stream, cancellationToken),
            cancellationToken);
#endif
    }

    public async Task<DocumentInput> LoadSampleAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("document_playground_sample.png");
        return await CreateInputAsync(
            "document_playground_sample.png",
            "image/png",
            await ReadBytesAsync(stream, cancellationToken),
            cancellationToken);
    }

    public async Task<DocumentInput?> ScanAsync(CancellationToken cancellationToken = default)
    {
#if IOS || MACCATALYST
        if (!CanScan)
            throw new NotSupportedException("The VisionKit document camera is unavailable on this device.");

        using var scan = await ScanAppleDocumentAsync();
        if (scan is null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        using var pdf = new PdfDocument();
        var pageCount = checked((int)scan.PageCount);
        for (var index = 0; index < pageCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var image = scan.GetImage((nuint)index);
            using var page = new PdfPage(image);
            pdf.InsertPage(page, index);
        }

        using var data = pdf.GetDataRepresentation()
            ?? throw new InvalidOperationException("Could not create a PDF from the captured pages.");
        return await CreateInputAsync(
            "Scanned document.pdf",
            "application/pdf",
            data.ToArray(),
            cancellationToken);
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("Document scanning requires VisionKit on iOS or Mac Catalyst.");
#endif
    }

    private static string? GetMediaType(string fileName) =>
        DocumentExtractionRunner.GetMediaType(fileName);

    private static Task<DocumentInput> CreateInputAsync(
        string fileName,
        string mediaType,
        byte[] data,
        CancellationToken cancellationToken)
    {
#if IOS || MACCATALYST
        if (string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Task.Run(
                () => new DocumentInput(
                    fileName,
                    mediaType,
                    data,
                    RenderPdfPreviewPages(data, cancellationToken)),
                cancellationToken);
        }
#endif
        return Task.FromResult(new DocumentInput(
            fileName,
            mediaType,
            data,
            [new DocumentPreviewPage(1, "Page 1", data)]));
    }

    private static async Task<byte[]> ReadBytesAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + bytesRead > MaximumDocumentBytes)
                throw new InvalidOperationException("Documents must be 75 MB or smaller.");

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
        return output.ToArray();
    }

#if IOS || MACCATALYST
    private static Task<DocumentInput?> PickAppleDocumentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var presentingController = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController()
            ?? throw new InvalidOperationException("No view controller is available to present the document picker.");
        var completion = new TaskCompletionSource<DocumentInput?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new UIDocumentPickerViewController(
            [UTTypes.Image, UTTypes.Pdf],
            asCopy: true)
        {
            AllowsMultipleSelection = false,
        };
        var pickerDelegate = new PickerDelegate(completion, controller, cancellationToken);
        lock (s_activePickerDelegates)
            s_activePickerDelegates.Add(pickerDelegate);
        controller.Delegate = pickerDelegate;
        presentingController.PresentViewController(controller, animated: true, completionHandler: null);
        return completion.Task;
    }

    private static Task<VNDocumentCameraScan?> ScanAppleDocumentAsync()
    {
        var presentingController = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController()
            ?? throw new InvalidOperationException("No view controller is available to present the document camera.");
        var completion = new TaskCompletionSource<VNDocumentCameraScan?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new ScannerController();
        var scannerDelegate = new ScannerDelegate(completion, controller);
        lock (s_activeScannerDelegates)
            s_activeScannerDelegates.Add(scannerDelegate);
        controller.Delegate = scannerDelegate;
        controller.Disappeared = scannerDelegate.DidDisappear;
        presentingController.PresentViewController(controller, animated: true, completionHandler: null);
        return completion.Task;
    }

    private sealed class PickerDelegate(
        TaskCompletionSource<DocumentInput?> completion,
        UIDocumentPickerViewController controller,
        CancellationToken cancellationToken)
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
                cancellationToken.ThrowIfCancellationRequested();
                var path = url.Path
                    ?? throw new InvalidOperationException("The selected file does not have a local path.");
                var fileName = url.LastPathComponent ?? Path.GetFileName(path);
                var mediaType = GetMediaType(fileName)
                    ?? throw new InvalidOperationException("Choose a PNG, JPEG, HEIC, TIFF, or PDF document.");
                await using var stream = File.OpenRead(path);
                Complete(await CreateInputAsync(
                    fileName,
                    mediaType,
                    await ReadBytesAsync(stream, cancellationToken),
                    cancellationToken));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
                Release();
            }
            finally
            {
                if (hasSecurityScope)
                    url.StopAccessingSecurityScopedResource();
            }
        }

        public override void WasCancelled(UIDocumentPickerViewController controller) =>
            Complete(null);

        private void Complete(DocumentInput? input)
        {
            completion.TrySetResult(input);
            Release();
        }

        private void Release()
        {
            controller.Delegate = null!;
            lock (s_activePickerDelegates)
                s_activePickerDelegates.Remove(this);
        }
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
        TaskCompletionSource<VNDocumentCameraScan?> completion,
        ScannerController controller)
        : VNDocumentCameraViewControllerDelegate
    {
        public override void DidFinish(
            VNDocumentCameraViewController controller,
            VNDocumentCameraScan scan)
        {
            controller.DismissViewController(animated: true, completionHandler: null);
            completion.TrySetResult(scan);
            Release();
        }

        public override void DidCancel(VNDocumentCameraViewController controller)
        {
            controller.DismissViewController(animated: true, completionHandler: null);
            completion.TrySetResult(null);
            Release();
        }

        public override void DidFail(
            VNDocumentCameraViewController controller,
            NSError error)
        {
            controller.DismissViewController(animated: true, completionHandler: null);
            completion.TrySetException(new NSErrorException(error));
            Release();
        }

        internal void DidDisappear()
        {
            if (completion.TrySetResult(null))
                Release();
        }

        private void Release()
        {
            controller.Delegate = null;
            controller.Disappeared = null;
            lock (s_activeScannerDelegates)
                s_activeScannerDelegates.Remove(this);
        }
    }

    private static IReadOnlyList<DocumentPreviewPage> RenderPdfPreviewPages(
        byte[] data,
        CancellationToken cancellationToken)
    {
        using var nativeData = NSData.FromArray(data);
        using var document = new PdfDocument(nativeData);
        if (document.IsLocked)
            throw new NotSupportedException("Password-protected PDF documents must be unlocked before preview.");

        var previews = new List<DocumentPreviewPage>(checked((int)document.PageCount));
        for (var index = 0; index < checked((int)document.PageCount); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var page = document.GetPage(index)
                ?? throw new InvalidOperationException($"PDFKit could not load page {index + 1} for preview.");
            var bounds = page.GetBoundsForBox(PdfDisplayBox.Crop);
            var pageNumber = index + 1;
            var pdfLabel = page.Label;
            previews.Add(new(
                pageNumber,
                string.IsNullOrWhiteSpace(pdfLabel) ||
                    string.Equals(pdfLabel, pageNumber.ToString(), StringComparison.Ordinal)
                        ? $"Page {pageNumber}"
                        : $"Page {pageNumber} ({pdfLabel})",
                RenderPdfPage(page, bounds)));
        }
        return previews;
    }

    private static byte[] RenderPdfPage(PdfPage page, CGRect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("The PDF page has invalid preview bounds.");

        const double previewDpi = 144;
        const int maximumPixelDimension = 1800;
        var rotation = ((checked((int)page.Rotation) % 360) + 360) % 360;
        var swapsDimensions = rotation is 90 or 270;
        var widthPoints = swapsDimensions ? bounds.Height : bounds.Width;
        var heightPoints = swapsDimensions ? bounds.Width : bounds.Height;
        var scale = previewDpi / 72d;
        var width = Math.Max(1, (int)Math.Ceiling(widthPoints * scale));
        var height = Math.Max(1, (int)Math.Ceiling(heightPoints * scale));
        var largest = Math.Max(width, height);
        if (largest > maximumPixelDimension)
        {
            var clamp = (double)maximumPixelDimension / largest;
            width = Math.Max(1, (int)Math.Floor(width * clamp));
            height = Math.Max(1, (int)Math.Floor(height * clamp));
        }

        using var colorSpace = CGColorSpace.CreateDeviceRGB();
        using var context = new CGBitmapContext(
            data: null,
            width,
            height,
            bitsPerComponent: 8,
            bytesPerRow: width * 4,
            colorSpace,
            CGBitmapFlags.PremultipliedLast);
        context.SetFillColor(1, 1, 1, 1);
        context.FillRect(new CGRect(0, 0, width, height));
        context.SaveState();
        context.ScaleCTM(width / widthPoints, height / heightPoints);
        page.Draw(PdfDisplayBox.Crop, context);
        context.RestoreState();

        using var image = context.ToImage()
            ?? throw new InvalidOperationException("PDFKit could not render the preview page.");
        using var output = new NSMutableData();
        using var destination = CGImageDestination.Create(output, "public.png", 1)
            ?? throw new InvalidOperationException("ImageIO could not create a PNG preview.");
        destination.AddImage(image);
        if (!destination.Close())
            throw new InvalidOperationException("ImageIO could not encode the PNG preview.");
        return output.ToArray();
    }
#endif
}
