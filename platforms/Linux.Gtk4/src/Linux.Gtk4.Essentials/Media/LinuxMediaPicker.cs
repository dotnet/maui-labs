using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Media;

public sealed class LinuxMediaPicker : IMediaPicker
{
	readonly ILinuxMediaCaptureProvider _captureProvider;

	public LinuxMediaPicker()
		: this(new GStreamerMediaCaptureProvider())
	{
	}

	internal LinuxMediaPicker(ILinuxMediaCaptureProvider captureProvider) =>
		_captureProvider = captureProvider;

	public bool IsCaptureSupported => _captureProvider.IsSupported;

	public Task<FileResult?> PickPhotoAsync(MediaPickerOptions? options) =>
		PickFileAsync("Select Photo", "image/*");

	public Task<List<FileResult>> PickPhotosAsync(MediaPickerOptions? options) =>
		PickFilesAsync("Select Photos", "image/*");

	public async Task<FileResult?> CapturePhotoAsync(MediaPickerOptions? options)
	{
		EnsureCaptureSupported();
		var path = await _captureProvider.CapturePhotoAsync(options?.Title);
		return path is null ? null : new FileResult(path);
	}

	public Task<FileResult?> PickVideoAsync(MediaPickerOptions? options) =>
		PickFileAsync("Select Video", "video/*");

	public Task<List<FileResult>> PickVideosAsync(MediaPickerOptions? options) =>
		PickFilesAsync("Select Videos", "video/*");

	public async Task<FileResult?> CaptureVideoAsync(MediaPickerOptions? options)
	{
		EnsureCaptureSupported();
		var path = await _captureProvider.CaptureVideoAsync(options?.Title);
		return path is null ? null : new FileResult(path);
	}

	void EnsureCaptureSupported()
	{
		if (!IsCaptureSupported)
			throw new FeatureNotSupportedException(
				"Linux camera capture requires gst-launch-1.0, gst-inspect-1.0, " +
				"the GStreamer v4l2src, videoconvert, jpegenc, vp8enc, and webmmux plugins, " +
				"and an accessible V4L2 camera under /dev/video*.");
	}

	static async Task<FileResult?> PickFileAsync(string title, string mimeType)
	{
		var dialog = Gtk.FileDialog.New();
		dialog.SetTitle(title);

		var filter = Gtk.FileFilter.New();
		filter.AddMimeType(mimeType);
		var filterList = Gio.ListStore.New(Gtk.FileFilter.GetGType());
		filterList.Append(filter);
		dialog.SetFilters(filterList);

		var window = GetActiveWindow("select media");
		var file = await PickOrCancelAsync(() => dialog.OpenAsync(window));
		var path = file?.GetPath();
		return path is not null ? new FileResult(path) : null;
	}

	static async Task<List<FileResult>> PickFilesAsync(string title, string mimeType)
	{
		var dialog = Gtk.FileDialog.New();
		dialog.SetTitle(title);

		var filter = Gtk.FileFilter.New();
		filter.AddMimeType(mimeType);
		var filterList = Gio.ListStore.New(Gtk.FileFilter.GetGType());
		filterList.Append(filter);
		dialog.SetFilters(filterList);

		var window = GetActiveWindow("select media");
		var files = await PickOrCancelAsync(() => dialog.OpenMultipleAsync(window));
		if (files is null)
			return [];

		var results = new List<FileResult>();
		for (uint i = 0; i < files.GetNItems(); i++)
		{
			var gioFile = (Gio.File)files.GetObject(i)!;
			var path = gioFile.GetPath();
			if (path is not null)
				results.Add(new FileResult(path));
		}
		return results;
	}

	internal static async Task<T?> PickOrCancelAsync<T>(Func<Task<T?>> pick) where T : class
	{
		try
		{
			return await pick();
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		// GirCore 0.7 exposes only the GError message on GException, as in LinuxFilePicker.
		catch (GLib.GException ex) when (string.Equals(ex.Message, "Dismissed by user", StringComparison.OrdinalIgnoreCase))
		{
			ex.Dispose();
			return null;
		}
	}

	internal static Gtk.Window GetActiveWindow(string operation) =>
		(Gtk.Application.GetDefault() as Gtk.Application)?.GetActiveWindow()
		?? throw new InvalidOperationException(
			$"A running GTK application window is required to {operation}.");
}

internal interface ILinuxMediaCaptureProvider
{
	bool IsSupported { get; }
	Task<string?> CapturePhotoAsync(string? title);
	Task<string?> CaptureVideoAsync(string? title);
}

internal sealed class GStreamerMediaCaptureProvider : ILinuxMediaCaptureProvider
{
	internal static readonly string[] RequiredPluginNames =
		["v4l2src", "videoconvert", "jpegenc", "vp8enc", "webmmux"];
	static readonly Lazy<GStreamerSupport> ToolSupport = new(DetectToolSupport);
	static readonly SemaphoreSlim CaptureGate = new(1, 1);

	public bool IsSupported => DetectSupport().IsSupported;

	public async Task<string?> CapturePhotoAsync(string? title)
	{
		await CaptureGate.WaitAsync();
		try
		{
			var outputPath = CreateTemporaryPath(".jpg");
			var support = DetectSupport();
			var process = StartGStreamer(
				support,
				CreatePhotoArguments(support.DevicePath!, outputPath));
			return await RunPhotoCaptureAsync(process, outputPath, title);
		}
		finally
		{
			CaptureGate.Release();
		}
	}

	public async Task<string?> CaptureVideoAsync(string? title)
	{
		await CaptureGate.WaitAsync();
		try
		{
			var outputPath = CreateTemporaryPath(".webm");
			var support = DetectSupport();
			var process = StartGStreamer(
				support,
				CreateVideoArguments(support.DevicePath!, outputPath));
			return await RunVideoCaptureAsync(process, outputPath, title);
		}
		finally
		{
			CaptureGate.Release();
		}
	}

	static async Task<string?> RunPhotoCaptureAsync(
		Process process,
		string outputPath,
		string? title)
	{
		try
		{
			using (process)
			using (var cancellation = new CancellationTokenSource())
			{
				var captureTask = WaitForSuccessfulExitAsync(process, cancellation.Token);
				var dialogTask = ShowCaptureDialogAsync(
					title ?? "Capture Photo",
					"Capturing a photo from the default camera.",
					primaryLabel: null);
				var completed = await Task.WhenAny(captureTask, dialogTask);
				if (completed == dialogTask)
				{
					await dialogTask;
					cancellation.Cancel();
					KillProcess(process);
					await IgnoreCancellationAsync(captureTask);
					DeleteIfExists(outputPath);
					return null;
				}

				await captureTask;
				await CloseCaptureDialogAsync();
			}

			return ValidateOutput(outputPath, "photo");
		}
		catch
		{
			KillProcess(process);
			DeleteIfExists(outputPath);
			await CloseCaptureDialogAsync();
			throw;
		}
	}

	static async Task<string?> RunVideoCaptureAsync(
		Process process,
		string outputPath,
		string? title)
	{
		try
		{
			using (process)
			{
				var errorTask = process.StandardError.ReadToEndAsync();
				var processExitTask = process.WaitForExitAsync();
				var dialogTask = ShowCaptureDialogAsync(
					title ?? "Capture Video",
					"Recording from the default camera.",
					"Stop Recording");
				var completed = await Task.WhenAny(dialogTask, processExitTask);
				if (completed == processExitTask)
				{
					await processExitTask;
					await CloseCaptureDialogAsync();
					var earlyError = await errorTask;
					throw new InvalidOperationException(process.ExitCode == 0
						? "GStreamer video capture ended before Stop was requested."
						: $"GStreamer video capture failed with exit code {process.ExitCode}: {earlyError.Trim()}");
				}

				var decision = await dialogTask;
				if (decision == CaptureDecision.Cancel)
				{
					KillProcess(process);
					await process.WaitForExitAsync();
					await errorTask;
					DeleteIfExists(outputPath);
					return null;
				}

				if (kill(process.Id, SignalInterrupt) != 0)
				{
					KillProcess(process);
					DeleteIfExists(outputPath);
					throw new InvalidOperationException(
						$"GStreamer recording could not be stopped cleanly (errno {Marshal.GetLastPInvokeError()}).");
				}

				using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
				try
				{
					await process.WaitForExitAsync(timeout.Token);
				}
				catch (OperationCanceledException)
				{
					KillProcess(process);
					DeleteIfExists(outputPath);
					throw new TimeoutException("GStreamer did not finalize the video within 15 seconds.");
				}

				var error = await errorTask;
				if (process.ExitCode != 0)
					throw new InvalidOperationException(
						$"GStreamer video capture failed with exit code {process.ExitCode}: {error.Trim()}");
			}

			return ValidateOutput(outputPath, "video");
		}
		catch
		{
			KillProcess(process);
			DeleteIfExists(outputPath);
			throw;
		}
	}

	static Process StartGStreamer(
		GStreamerSupport support,
		IReadOnlyList<string> arguments)
	{
		if (!support.IsSupported)
			throw new FeatureNotSupportedException(support.FailureReason);

		var startInfo = new ProcessStartInfo(support.LaunchPath!)
		{
			UseShellExecute = false,
			RedirectStandardError = true,
		};
		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		return Process.Start(startInfo)
			?? throw new InvalidOperationException("GStreamer could not be started.");
	}

	internal static IReadOnlyList<string> CreatePhotoArguments(
		string devicePath,
		string outputPath) =>
	[
		"-q",
		"v4l2src", $"device={devicePath}", "num-buffers=1",
		"!", "videoconvert",
		"!", "jpegenc",
		"!", "filesink", $"location={outputPath}",
	];

	internal static IReadOnlyList<string> CreateVideoArguments(
		string devicePath,
		string outputPath) =>
	[
		"-q", "-e",
		"v4l2src", $"device={devicePath}",
		"!", "videoconvert",
		"!", "vp8enc", "deadline=1",
		"!", "webmmux",
		"!", "filesink", $"location={outputPath}",
	];

	static async Task WaitForSuccessfulExitAsync(Process process, CancellationToken cancellationToken)
	{
		var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);
		var error = await errorTask;
		if (process.ExitCode != 0)
			throw new InvalidOperationException(
				$"GStreamer photo capture failed with exit code {process.ExitCode}: {error.Trim()}");
	}

	static Task<CaptureDecision> ShowCaptureDialogAsync(
		string title,
		string message,
		string? primaryLabel)
	{
		var completion = new TaskCompletionSource<CaptureDecision>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		GLib.Functions.IdleAdd(0, () =>
		{
			try
			{
				var parent = LinuxMediaPicker.GetActiveWindow("capture media");
				var dialog = new Gtk.Window();
				dialog.SetTitle(title);
				dialog.SetModal(true);
				dialog.SetTransientFor(parent);
				dialog.SetResizable(false);
				dialog.SetDefaultSize(360, -1);
				if (parent.GetApplication() is { } application)
					dialog.SetApplication(application);

				var box = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
				box.SetMarginTop(20);
				box.SetMarginBottom(20);
				box.SetMarginStart(20);
				box.SetMarginEnd(20);
				box.Append(Gtk.Label.New(message));

				var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
				buttons.SetHalign(Gtk.Align.End);
				if (primaryLabel is not null)
				{
					var primary = Gtk.Button.NewWithLabel(primaryLabel);
					primary.OnClicked += (_, _) =>
					{
						completion.TrySetResult(CaptureDecision.Accept);
						dialog.Close();
					};
					buttons.Append(primary);
				}

				var cancel = Gtk.Button.NewWithLabel("Cancel");
				cancel.OnClicked += (_, _) =>
				{
					completion.TrySetResult(CaptureDecision.Cancel);
					dialog.Close();
				};
				buttons.Append(cancel);
				box.Append(buttons);
				dialog.SetChild(box);
				dialog.OnCloseRequest += (_, _) =>
				{
					completion.TrySetResult(CaptureDecision.Cancel);
					return false;
				};

				_activeCaptureDialog = dialog;
				dialog.Present();
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
			}
			return false;
		});
		return completion.Task;
	}

	static Gtk.Window? _activeCaptureDialog;

	static Task CloseCaptureDialogAsync()
	{
		var completion = new TaskCompletionSource(
			TaskCreationOptions.RunContinuationsAsynchronously);
		GLib.Functions.IdleAdd(0, () =>
		{
			_activeCaptureDialog?.Close();
			_activeCaptureDialog = null;
			completion.TrySetResult();
			return false;
		});
		return completion.Task;
	}

	static GStreamerSupport DetectSupport()
	{
		var support = ToolSupport.Value;
		if (support.LaunchPath is null)
			return support;

		var devicePath = FindVideoDevice();
		return devicePath is null
			? new(
				support.LaunchPath,
				null,
				"Linux camera capture requires an accessible V4L2 capture device under /dev/video*.")
			: support with { DevicePath = devicePath };
	}

	static GStreamerSupport DetectToolSupport()
	{
		var launchPath = FindExecutable("gst-launch-1.0");
		var inspectPath = FindExecutable("gst-inspect-1.0");
		if (launchPath is null || inspectPath is null)
			return new(null, null, "Linux camera capture requires gst-launch-1.0 and gst-inspect-1.0.");

		foreach (var plugin in RequiredPluginNames)
		{
			using var process = Process.Start(new ProcessStartInfo(inspectPath, plugin)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			});
			if (process is null)
				return new(null, null, $"GStreamer plugin inspection could not start for '{plugin}'.");
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			if (!process.WaitForExit(5000))
			{
				KillProcess(process);
				return new(null, null, $"GStreamer plugin inspection timed out for '{plugin}'.");
			}
			if (process.ExitCode != 0)
				return new(null, null, $"The required GStreamer plugin '{plugin}' is unavailable.");
		}

		return new(launchPath, null, null);
	}

	internal static string? FindVideoDevice() =>
		Directory.Exists("/dev")
			? FindVideoDevice(Directory.EnumerateFiles("/dev", "video*"), V4l2DeviceProbe.CanCapture)
			: null;

	internal static string? FindVideoDevice(IEnumerable<string> candidates, Func<string, bool> canCapture) =>
		candidates.Order(StringComparer.Ordinal).FirstOrDefault(canCapture);

	static string? FindExecutable(string name)
	{
		var path = Environment.GetEnvironmentVariable("PATH");
		if (string.IsNullOrWhiteSpace(path))
			return null;

		return path.Split(Path.PathSeparator)
			.Select(directory => Path.Combine(directory, name))
			.FirstOrDefault(File.Exists);
	}

	static string CreateTemporaryPath(string extension) =>
		Path.Combine(FileSystem.CacheDirectory, $"capture-{Guid.NewGuid():N}{extension}");

	static string ValidateOutput(string path, string mediaKind)
	{
		if (!File.Exists(path) || new FileInfo(path).Length == 0)
		{
			DeleteIfExists(path);
			throw new InvalidOperationException(
				$"GStreamer completed without producing a non-empty {mediaKind} file.");
		}

		return path;
	}

	static void KillProcess(Process process)
	{
		try
		{
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
		}
		catch (InvalidOperationException)
		{
		}
	}

	static async Task IgnoreCancellationAsync(Task task)
	{
		try
		{
			await task;
		}
		catch (OperationCanceledException)
		{
		}
	}

	static void DeleteIfExists(string path)
	{
		if (File.Exists(path))
			File.Delete(path);
	}

	const int SignalInterrupt = 2;

	[DllImport("libc", SetLastError = true)]
	static extern int kill(int processId, int signal);

	internal sealed record GStreamerSupport(
		string? LaunchPath,
		string? DevicePath,
		string? FailureReason)
	{
		public bool IsSupported => LaunchPath is not null && DevicePath is not null;
	}

	enum CaptureDecision
	{
		Accept,
		Cancel,
	}
}
