using System.Text.Json;
using DocumentExtractionSample.Models;
using DocumentExtractionSample.Pages;
using DocumentExtractionSample.Services;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Essentials.AI;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Storage;
#if IOS || MACCATALYST
using PdfKit;
#endif

namespace DocumentExtractionSample;

/// <summary>
/// The single page of this sample. Lets the user pick an image or PDF (or, on iOS/Mac Catalyst, scan pages with the
/// system document camera), runs Apple Vision document extraction, and displays the resulting page text plus a
/// recursive tree of blocks, tables, lists, and barcodes. Any node backed by an Apple Vision object can be inspected
/// as raw JSON, and the client's reported capabilities can be viewed on demand.
/// </summary>
public sealed class MainPage : ContentPage
{
	private const int MaxDisplayedNodes = 2000;

	private static readonly FilePickerFileType ImageOrPdfFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
	{
		{ DevicePlatform.iOS, new[] { "public.image", "com.adobe.pdf" } },
		{ DevicePlatform.MacCatalyst, new[] { "public.image", "com.adobe.pdf" } },
		{ DevicePlatform.macOS, new[] { "public.image", "com.adobe.pdf" } },
	});

	private IReadOnlyList<DocumentTreeNode> _nodes = [];
	private readonly CollectionView _resultsView;
	private readonly Label _statusLabel;
	private readonly Label _errorLabel;
	private readonly ActivityIndicator _activityIndicator;
	private readonly ProgressBar _progressBar;
	private readonly Button _pickButton;
	private readonly Button _scanButton;
	private readonly Button _cancelButton;
	private readonly Button _capabilitiesButton;
	private readonly Button _viewResultJsonButton;
	private readonly Grid _previewGrid;
	private readonly Image _previewImage;
	private readonly GraphicsView _overlayView;
	private readonly DocumentOverlayDrawable _overlayDrawable;

	private CancellationTokenSource? _cts;
	private DocumentExtractionResult? _lastResult;
	private bool _autoOpenAttempted;

	public MainPage()
	{
		Title = "Document Extraction Sample";

		_pickButton = new Button { Text = "Pick Image or PDF" };
		_pickButton.Clicked += OnPickClicked;

		_scanButton = new Button { Text = "Scan with Camera", IsVisible = false };
#if IOS || MACCATALYST
		_scanButton.IsVisible = DocumentCameraScanner.IsSupported;
		_scanButton.Clicked += OnScanClicked;
#endif

		_cancelButton = new Button { Text = "Cancel", IsEnabled = false };
		_cancelButton.Clicked += (_, _) => _cts?.Cancel();

		_capabilitiesButton = new Button { Text = "Capabilities" };
		_capabilitiesButton.Clicked += OnCapabilitiesClicked;

		_viewResultJsonButton = new Button { Text = "View Result JSON", IsEnabled = false };
		_viewResultJsonButton.Clicked += OnViewResultJsonClicked;

		var buttonRow = new FlexLayout
		{
			Wrap = FlexWrap.Wrap,
			JustifyContent = FlexJustify.Start,
			Children = { _pickButton, _scanButton, _capabilitiesButton, _viewResultJsonButton, _cancelButton },
		};

		_statusLabel = new Label { Text = "Pick an image or PDF to begin.", FontSize = 14 };
		_errorLabel = new Label { TextColor = Colors.Red, FontSize = 13, IsVisible = false };
		_activityIndicator = new ActivityIndicator { IsRunning = false, IsVisible = false };
		_progressBar = new ProgressBar { Progress = 0, IsVisible = false };
		_previewImage = new Image { Aspect = Aspect.AspectFit };
		_overlayDrawable = new DocumentOverlayDrawable();
		_overlayView = new GraphicsView
		{
			Drawable = _overlayDrawable,
			InputTransparent = true,
		};
		_previewGrid = new Grid
		{
			HeightRequest = 320,
			IsVisible = false,
			BackgroundColor = Colors.Black,
			Children = { _previewImage, _overlayView },
		};

		_resultsView = new CollectionView
		{
			ItemsSource = _nodes,
			ItemTemplate = CreateNodeTemplate(this),
			SelectionMode = SelectionMode.None,
		};

		var layout = new Grid
		{
			Padding = new Thickness(12),
			RowSpacing = 8,
			RowDefinitions =
			{
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Auto),
				new RowDefinition(GridLength.Star),
			},
		};
		layout.Add(buttonRow, column: 0, row: 0);
		layout.Add(_statusLabel, column: 0, row: 1);
		layout.Add(_errorLabel, column: 0, row: 2);
		layout.Add(new HorizontalStackLayout { Spacing = 8, Children = { _activityIndicator, _progressBar } }, column: 0, row: 3);
		layout.Add(_previewGrid, column: 0, row: 4);
		layout.Add(_resultsView, column: 0, row: 5);

		Content = layout;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (_autoOpenAttempted)
		{
			return;
		}

		_autoOpenAttempted = true;
		var autoOpenPath = Environment.GetEnvironmentVariable(
			"DOCUMENT_EXTRACTION_SAMPLE_FILE");
		if (string.IsNullOrWhiteSpace(autoOpenPath))
		{
			return;
		}

		try
		{
			using var stream = File.OpenRead(autoOpenPath);
			await ExtractFromStreamAsync(
				Path.GetFileName(autoOpenPath),
				stream).ConfigureAwait(true);
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private static DataTemplate CreateNodeTemplate(MainPage owner) =>
		new(() =>
		{
			var titleLabel = new Label { FontAttributes = FontAttributes.Bold, FontSize = 14 };
			titleLabel.SetBinding(Label.TextProperty, nameof(DocumentTreeNode.Title));

			var subtitleLabel = new Label { FontSize = 12, TextColor = Colors.Gray };
			subtitleLabel.SetBinding(Label.TextProperty, nameof(DocumentTreeNode.Subtitle));
			subtitleLabel.SetBinding(IsVisibleProperty, nameof(DocumentTreeNode.HasSubtitle));

			var jsonButton = new Button { Text = "JSON", FontSize = 11, Padding = new Thickness(8, 2) };
			jsonButton.SetBinding(IsVisibleProperty, nameof(DocumentTreeNode.HasRawJson));
			jsonButton.Clicked += async (sender, _) =>
			{
				if (sender is Button { BindingContext: DocumentTreeNode node })
				{
					await owner.ShowRawJsonAsync(node).ConfigureAwait(true);
				}
			};

			var textColumn = new VerticalStackLayout { Spacing = 2, Children = { titleLabel, subtitleLabel } };

			var row = new Grid
			{
				ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
				Padding = new Thickness(0, 4),
			};
			row.SetBinding(Grid.MarginProperty, nameof(DocumentTreeNode.Indent));
			row.Add(textColumn, column: 0, row: 0);
			row.Add(jsonButton, column: 1, row: 0);
			return row;
		});

	private async void OnPickClicked(object? sender, EventArgs e)
	{
		try
		{
#if IOS || MACCATALYST
			var pickedDocument = await DocumentFilePicker.PickAsync().ConfigureAwait(true);
			if (pickedDocument is null)
			{
				return;
			}

			using var stream = new MemoryStream(pickedDocument.Data, writable: false);
			await ExtractFromStreamAsync(
				pickedDocument.FileName,
				stream,
				pickedDocument.Data).ConfigureAwait(true);
#else
			var result = await FilePicker.Default.PickAsync(new PickOptions
			{
				PickerTitle = "Select an image or PDF",
				FileTypes = ImageOrPdfFileType,
			});
			if (result is null)
			{
				return;
			}

			await ExtractFromFileAsync(result).ConfigureAwait(true);
#endif
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private async Task ExtractFromFileAsync(FileResult file)
	{
		using var stream = await file.OpenReadAsync().ConfigureAwait(true);
		await ExtractFromStreamAsync(file.FileName, stream).ConfigureAwait(true);
	}

	private async Task ExtractFromStreamAsync(
		string fileName,
		Stream sourceStream,
		byte[]? knownBytes = null)
	{
		var mediaType = DocumentExtractionRunner.GetMediaType(fileName);
		if (mediaType is null)
		{
			ShowError($"'{fileName}' is not a supported image or PDF file.");
			return;
		}

		if (!DocumentExtractionRunner.IsPlatformSupported)
		{
			ShowError("Apple Vision document recognition requires iOS, Mac Catalyst, or macOS 26 or later.");
			return;
		}

		ClearError();
		BeginWork();
		_statusLabel.Text = $"Recognizing {fileName}…";
		try
		{
			Stream extractionStream = sourceStream;
			MemoryStream? imageStream = null;
			if (!string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
			{
				var bytes = knownBytes;
				if (bytes is null)
				{
					using var imageBytes = new MemoryStream();
					await sourceStream.CopyToAsync(imageBytes, _cts!.Token).ConfigureAwait(true);
					bytes = imageBytes.ToArray();
				}
				imageStream = new MemoryStream(bytes, writable: false);
				extractionStream = imageStream;
				_previewImage.Source = ImageSource.FromStream(
					() => new MemoryStream(bytes, writable: false));
				_previewGrid.IsVisible = true;
			}
			else
			{
				ClearPreview();
			}

			using (imageStream)
			{
				var cts = _cts!;
				_lastResult = await Task.Run(
					() => DocumentExtractionRunner.ExtractAsync(
						extractionStream,
						mediaType,
						progress => OnProgress(progress),
						cts.Token),
					cts.Token).ConfigureAwait(true);
			}

			var resultNodes = DocumentTreeBuilder.BuildTree(_lastResult);
			var totalNodeCount = resultNodes.Count;
			_nodes = totalNodeCount > MaxDisplayedNodes
				? resultNodes.Take(MaxDisplayedNodes).ToArray()
				: resultNodes;
			_resultsView.ItemsSource = _nodes;
			_overlayDrawable.SetPage(_lastResult.Pages.FirstOrDefault());
			_overlayView.Invalidate();
			_viewResultJsonButton.IsEnabled = true;
			SetCompletionStatus("Done", _lastResult, totalNodeCount);
		}
		catch (OperationCanceledException)
		{
			_statusLabel.Text = "Cancelled.";
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
		finally
		{
			EndWork();
		}
	}

	private void OnProgress(DocumentExtractionProgress progress)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			_statusLabel.Text = progress.TotalPages is { } total
				? $"Processed page {progress.PagesProcessed}/{total}…"
				: $"Processed page {progress.PagesProcessed}…";
			if (progress.PagesProcessed is { } processed && progress.TotalPages is { } totalPages and > 0)
			{
				_progressBar.Progress = (double)processed / totalPages;
			}
		});
	}

#if IOS || MACCATALYST
	private async void OnScanClicked(object? sender, EventArgs e)
	{
		if (!DocumentExtractionRunner.IsPlatformSupported)
		{
			ShowError("Apple Vision document recognition requires iOS, Mac Catalyst, or macOS 26 or later.");
			return;
		}

		ClearError();
		try
		{
			var scan = await DocumentCameraScanner.ScanAsync().ConfigureAwait(true);
			if (scan is null)
			{
				return;
			}

			await ExtractFromScanAsync(scan).ConfigureAwait(true);
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private async Task ExtractFromScanAsync(VisionKit.VNDocumentCameraScan scan)
	{
		BeginWork();
		try
		{
			_nodes = [];
			_resultsView.ItemsSource = _nodes;
			ClearPreview();
			var totalPages = checked((int)scan.PageCount);
			var cancellationToken = _cts!.Token;
			using var pdf = new PdfDocument();
			for (var index = 0; index < totalPages; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				using var image = scan.GetImage((nuint)index);
				using var page = new PdfPage(image);
				pdf.InsertPage(page, index);
			}

			using var pdfData = pdf.GetDataRepresentation()
				?? throw new InvalidOperationException("Could not create a PDF from the captured pages.");
			using var stream = pdfData.AsStream();
			_lastResult = await DocumentExtractionRunner.ExtractAsync(
				stream,
				"application/pdf",
				progress => OnProgress(progress),
				cancellationToken).ConfigureAwait(true);
			var resultNodes = DocumentTreeBuilder.BuildTree(_lastResult);
			var totalNodeCount = resultNodes.Count;
			_nodes = totalNodeCount > MaxDisplayedNodes
				? resultNodes.Take(MaxDisplayedNodes).ToArray()
				: resultNodes;
			_resultsView.ItemsSource = _nodes;
			_viewResultJsonButton.IsEnabled = true;
			SetCompletionStatus("Scan complete", _lastResult, totalNodeCount);
		}
		catch (OperationCanceledException)
		{
			_statusLabel.Text = "Cancelled.";
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
		finally
		{
			EndWork();
		}
	}
#endif

	private async void OnCapabilitiesClicked(object? sender, EventArgs e)
	{
		if (!DocumentExtractionRunner.IsPlatformSupported)
		{
			ShowError("Apple Vision document recognition requires iOS, Mac Catalyst, or macOS 26 or later.");
			return;
		}

		try
		{
			using var client = new AppleVisionRecognizeDocumentsClient();
			var capabilities = client.GetService<AppleVisionDocumentCapabilities>();
			if (capabilities is null)
			{
				ShowError("Capabilities are unavailable on this device.");
				return;
			}

			var text = FormatCapabilities(capabilities);
			await Navigation.PushModalAsync(new TextViewerPage("Apple Vision Capabilities", text)).ConfigureAwait(true);
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private async void OnViewResultJsonClicked(object? sender, EventArgs e)
	{
		if (_lastResult is null)
		{
			return;
		}

		try
		{
			var json = JsonSerializer.Serialize(_lastResult, AppleDocumentExtractionJson.Default);
			await Navigation.PushModalAsync(new TextViewerPage("Document Extraction Result JSON", json)).ConfigureAwait(true);
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private async Task ShowRawJsonAsync(DocumentTreeNode node)
	{
		if (node.RawReference is null)
		{
			return;
		}

		string json;
		try
		{
			using var document = JsonDocument.Parse(node.RawReference.GetRawJsonText());
			json = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
		}
		catch (JsonException)
		{
			json = node.RawReference.GetRawJsonText();
		}

		await Navigation.PushModalAsync(new TextViewerPage($"Raw JSON — {node.Title}", json)).ConfigureAwait(true);
	}

	private static string FormatCapabilities(AppleVisionDocumentCapabilities capabilities)
	{
		var writer = new System.Text.StringBuilder();
		writer.AppendLine("Recognition languages:");
		foreach (var language in capabilities.RecognitionLanguages)
		{
			writer.AppendLine($"  - {language}");
		}
		writer.AppendLine();
		writer.AppendLine("Barcode symbologies:");
		foreach (var symbology in capabilities.BarcodeSymbologies)
		{
			writer.AppendLine($"  - {symbology}");
		}
		writer.AppendLine();
		writer.AppendLine("Revisions:");
		foreach (var revision in capabilities.Revisions)
		{
			writer.AppendLine($"  - {revision}");
		}
		return writer.ToString();
	}

	private void BeginWork()
	{
		_cts?.Dispose();
		_cts = new CancellationTokenSource();
		_pickButton.IsEnabled = false;
		_scanButton.IsEnabled = false;
		_cancelButton.IsEnabled = true;
		_viewResultJsonButton.IsEnabled = false;
		_activityIndicator.IsVisible = true;
		_activityIndicator.IsRunning = true;
		_progressBar.IsVisible = true;
		_progressBar.Progress = 0;
	}

	private void SetCompletionStatus(
		string prefix,
		DocumentExtractionResult result,
		int totalNodeCount)
	{
		var message = totalNodeCount > MaxDisplayedNodes
			? $"{prefix} — {result.Pages.Count} page(s), showing {MaxDisplayedNodes:N0} of {totalNodeCount:N0} nodes."
			: $"{prefix} — {result.Pages.Count} page(s), {totalNodeCount:N0} node(s).";
		var repeatedContainerCount = result.Pages.Sum(static page =>
			GetLongProperty(page, "apple.vision.repeatedContainersPruned"));
		if (repeatedContainerCount > 0)
		{
			message += $" Pruned {repeatedContainerCount:N0} repeated Apple container traversal(s).";
		}
		_statusLabel.Text = message;

		foreach (var page in result.Pages.Where(static page =>
			GetLongProperty(page, "apple.vision.repeatedContainersPruned") > 0))
		{
			var examples = page.AdditionalProperties?.TryGetValue(
				"apple.vision.repeatedContainerExamples",
				out var value) == true &&
				value is string[] paths
					? string.Join("; ", paths)
					: "unavailable";
			Console.WriteLine(
				$"[DocumentExtractionSample] Page {page.PageNumber}: " +
				$"{GetLongProperty(page, "apple.vision.projectedNodeCount")} projected nodes, " +
				$"maximum traversal depth {GetLongProperty(page, "apple.vision.maximumTraversalDepth")}, " +
				$"{GetLongProperty(page, "apple.vision.repeatedContainersPruned")} repeated containers pruned. " +
				$"Examples: {examples}");
		}
	}

	private static long GetLongProperty(DocumentPage page, string key) =>
		page.AdditionalProperties?.TryGetValue(key, out var value) == true &&
		value is long number
			? number
			: 0;

	private void EndWork()
	{
		_pickButton.IsEnabled = true;
		_scanButton.IsEnabled = true;
		_cancelButton.IsEnabled = false;
		_activityIndicator.IsRunning = false;
		_activityIndicator.IsVisible = false;
		_progressBar.IsVisible = false;
	}

	private void ShowError(Exception ex) => ShowError(ex.Message);

	private void ShowError(string message)
	{
		_errorLabel.Text = message;
		_errorLabel.IsVisible = true;
		_statusLabel.Text = "Failed.";
	}

	private void ClearError()
	{
		_errorLabel.Text = string.Empty;
		_errorLabel.IsVisible = false;
	}

	private void ClearPreview()
	{
		_previewImage.Source = null;
		_overlayDrawable.SetPage(null);
		_overlayView.Invalidate();
		_previewGrid.IsVisible = false;
	}
}
