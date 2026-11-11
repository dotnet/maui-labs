using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using Microsoft.Win32;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials;

public sealed class WPFMediaPicker : IMediaPicker
{
	readonly IWpfMediaCaptureProvider _captureProvider;

	const string ImageFilter =
		"Image files|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.heic;*.heif|All files|*.*";
	const string VideoFilter =
		"Video files|*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm;*.m4v|All files|*.*";

	public WPFMediaPicker()
		: this(new UnsupportedWpfMediaCaptureProvider())
	{
	}

	internal WPFMediaPicker(IWpfMediaCaptureProvider captureProvider) =>
		_captureProvider = captureProvider;

	public bool IsCaptureSupported => _captureProvider.IsSupported;

	public Task<FileResult?> PickPhotoAsync(MediaPickerOptions? options = null) =>
		PickSingleAsync(ImageFilter, options?.Title);

	public Task<List<FileResult>> PickPhotosAsync(MediaPickerOptions? options = null) =>
		PickMultipleAsync(ImageFilter, options?.Title, options?.SelectionLimit ?? 0);

	public Task<FileResult?> PickVideoAsync(MediaPickerOptions? options = null) =>
		PickSingleAsync(VideoFilter, options?.Title);

	public Task<List<FileResult>> PickVideosAsync(MediaPickerOptions? options = null) =>
		PickMultipleAsync(VideoFilter, options?.Title, options?.SelectionLimit ?? 0);

	public Task<FileResult?> CapturePhotoAsync(MediaPickerOptions? options = null) =>
		CaptureAsync(WpfMediaCaptureKind.Photo);

	public Task<FileResult?> CaptureVideoAsync(MediaPickerOptions? options = null) =>
		CaptureAsync(WpfMediaCaptureKind.Video);

	async Task<FileResult?> CaptureAsync(WpfMediaCaptureKind kind)
	{
		if (!IsCaptureSupported)
			throw new FeatureNotSupportedException(
				"Camera capture is not supported by this WPF backend. " +
				"The Windows.Media.Capture.CameraCaptureUI API is UWP-only, and the supported " +
				"Windows App SDK desktop capture API is not yet a dependency of this backend.");

		var path = await _captureProvider.CaptureAsync(kind);
		return path is null ? null : new FileResult(path);
	}

	static Task<FileResult?> PickSingleAsync(string filter, string? title) =>
		RunOnDispatcherAsync(() =>
		{
			var dialog = new OpenFileDialog
			{
				Filter = filter,
				Title = title ?? string.Empty,
				Multiselect = false,
			};

			return dialog.ShowDialog() == true
				? new FileResult(dialog.FileName)
				: null;
		});

	static Task<List<FileResult>> PickMultipleAsync(string filter, string? title, int selectionLimit) =>
		RunOnDispatcherAsync(() =>
		{
			var dialog = new OpenFileDialog
			{
				Filter = filter,
				Title = title ?? string.Empty,
				Multiselect = selectionLimit != 1,
			};

			if (dialog.ShowDialog() != true)
				return [];

			return dialog.FileNames
				.Take(selectionLimit > 0 ? selectionLimit : int.MaxValue)
				.Select(path => new FileResult(path))
				.ToList();
		});

	static Task<T> RunOnDispatcherAsync<T>(Func<T> action)
	{
		var dispatcher = System.Windows.Application.Current?.Dispatcher;
		if (dispatcher is null || dispatcher.CheckAccess())
			return Task.FromResult(action());

		return dispatcher.InvokeAsync(action).Task;
	}
}

internal interface IWpfMediaCaptureProvider
{
	bool IsSupported { get; }
	Task<string?> CaptureAsync(WpfMediaCaptureKind kind);
}

internal enum WpfMediaCaptureKind
{
	Photo,
	Video,
}

internal sealed class UnsupportedWpfMediaCaptureProvider : IWpfMediaCaptureProvider
{
	public bool IsSupported => false;

	public Task<string?> CaptureAsync(WpfMediaCaptureKind kind) =>
		throw new FeatureNotSupportedException(
			$"WPF {kind.ToString().ToLowerInvariant()} capture requires the Windows App SDK desktop CameraCaptureUI API.");
}
