using System.Collections.ObjectModel;
using AIExtensions.Sample.ChatPlayground.Features.Images;
using AIExtensions.Sample.ChatPlayground.Models;
using AIExtensions.Sample.ChatPlayground.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Size = System.Drawing.Size;

namespace AIExtensions.Sample.ChatPlayground.ViewModels;

#pragma warning disable MEAI001 // IImageGenerator and its options are experimental in the installed SDK.
public sealed partial class ImagePlaygroundViewModel : ObservableObject
{
    private readonly ImageGenerationService _generation;
    private readonly ImageInputService _imageInput;
    private ImageAttachment? _original;
    private CancellationTokenSource? _cancellation;

    public ImagePlaygroundViewModel(
        ImageGenerationService generation, ImageInputService imageInput,
        IEnumerable<IImageGenerator> generators)
    {
        _generation = generation;
        _imageInput = imageInput;
        Generators = generators.Select((generator, index) => new ImageGeneratorOption(
            generator, generator.GetService<ImageGeneratorDescriptor>()
                ?? throw new InvalidOperationException(
                    $"Image generator {index} did not expose its descriptor."), index)).ToArray();
        SelectedSize = ImageSizes[0];
        SelectedOption = Generators.FirstOrDefault();
        StatusMessage = SelectedOption is null
            ? "Configure AI:ImageDeploymentName to enable an image generator on this device."
            : "Enter a prompt to generate an image, or add an original image to edit.";
    }

    public IReadOnlyList<ImageGeneratorOption> Generators { get; }
    public IReadOnlyList<ImageSizeOption> ImageSizes { get; } =
    [
        new("Provider default", null),
        new("1024 x 1024", new Size(1024, 1024)),
        new("1536 x 1024", new Size(1536, 1024)),
        new("1024 x 1536", new Size(1024, 1536)),
    ];
    public IReadOnlyList<string> MediaTypes { get; } =
        ["Provider default", "image/png", "image/jpeg", "image/webp"];
    public ObservableCollection<GeneratedImageViewModel> GeneratedImages { get; } = [];

    [ObservableProperty] private ImageGeneratorOption? selectedOption;
    [ObservableProperty] private string prompt = string.Empty;
    [ObservableProperty] private string count = string.Empty;
    [ObservableProperty] private ImageSizeOption? selectedSize;
    [ObservableProperty] private string selectedMediaType = "Provider default";
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string originalImageName = string.Empty;
    [ObservableProperty] private ImageSource? originalImagePreview;
    [ObservableProperty] private bool isBusy;

    public IImageGenerator? SelectedGenerator => SelectedOption?.Generator;
    public ImageGeneratorDescriptor? SelectedDescriptor => SelectedOption?.Descriptor;
    public bool HasGenerator => SelectedGenerator is not null;
    public bool SupportsEdits => SelectedDescriptor?.SupportsEdits == true;
    public bool HasOriginalImage => _original is not null;
    public bool IsIdle => !IsBusy;
    public bool HasResults => GeneratedImages.Count > 0;

    public IAsyncRelayCommand GenerateCommand =>
        _generateCommand ??= new AsyncRelayCommand(GenerateAsync, CanGenerate);
    public IAsyncRelayCommand ChooseImageCommand =>
        _chooseImageCommand ??= new AsyncRelayCommand(ChooseImageAsync, CanEdit);
    public IAsyncRelayCommand UseSampleImageCommand =>
        _useSampleImageCommand ??= new AsyncRelayCommand(UseSampleImageAsync, CanEdit);
    public IRelayCommand RemoveImageCommand =>
        _removeImageCommand ??= new RelayCommand(ClearImage, () => !IsBusy && HasOriginalImage);
    public IRelayCommand CancelCommand =>
        _cancelCommand ??= new RelayCommand(() => _cancellation?.Cancel(), () => IsBusy);

    private IAsyncRelayCommand? _generateCommand;
    private IAsyncRelayCommand? _chooseImageCommand;
    private IAsyncRelayCommand? _useSampleImageCommand;
    private IRelayCommand? _removeImageCommand;
    private IRelayCommand? _cancelCommand;

    private bool CanGenerate() => !IsBusy && HasGenerator && !string.IsNullOrWhiteSpace(Prompt);
    private bool CanEdit() => !IsBusy && SupportsEdits;

    private async Task GenerateAsync()
    {
        var generator = SelectedGenerator
            ?? throw new InvalidOperationException("Select a real image generator first.");
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        IsBusy = true;
        StatusMessage = _original is null ? "Generating image..." : "Editing image...";
        try
        {
            var options = ImageGenerationService.CreateOptions(
                Count, SelectedSize?.Size, SelectedMediaType);
            var images = await _generation.GenerateAsync(
                generator, Prompt, _original, options, cancellation.Token);
            GeneratedImages.Clear();
            foreach (var image in images)
                GeneratedImages.Add(new GeneratedImageViewModel(image));
            OnPropertyChanged(nameof(HasResults));
            StatusMessage = $"Generated {images.Count} {(images.Count == 1 ? "image" : "images")} with " +
                $"{SelectedDescriptor?.DisplayName}.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = "Image request cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Image generation failed: {exception.Message}";
        }
        finally
        {
            _cancellation = null;
            IsBusy = false;
        }
    }

    private async Task ChooseImageAsync()
    {
        try
        {
            if (await _imageInput.PickImageAsync() is { } image)
                SetOriginalImage(image);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not select image: {exception.Message}";
        }
    }

    private async Task UseSampleImageAsync()
    {
        try
        {
            SetOriginalImage(await _imageInput.LoadSampleImageAsync());
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not load sample image: {exception.Message}";
        }
    }

    private void SetOriginalImage(ImageAttachment image)
    {
        _original = image;
        OriginalImageName = image.FileName;
        OriginalImagePreview = ImageSource.FromStream(
            () => new MemoryStream(image.Bytes, writable: false));
        OnPropertyChanged(nameof(HasOriginalImage));
        RemoveImageCommand.NotifyCanExecuteChanged();
        StatusMessage = $"Editing {image.FileName}. Enter instructions and generate.";
    }

    private void ClearImage()
    {
        _original = null;
        OriginalImageName = string.Empty;
        OriginalImagePreview = null;
        OnPropertyChanged(nameof(HasOriginalImage));
        RemoveImageCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedOptionChanged(ImageGeneratorOption? value)
    {
        OnPropertyChanged(nameof(SelectedGenerator));
        OnPropertyChanged(nameof(SelectedDescriptor));
        OnPropertyChanged(nameof(HasGenerator));
        OnPropertyChanged(nameof(SupportsEdits));
        if (!SupportsEdits && HasOriginalImage)
            ClearImage();
        RefreshCommands();
    }

    partial void OnPromptChanged(string value) => GenerateCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        GenerateCommand.NotifyCanExecuteChanged();
        ChooseImageCommand.NotifyCanExecuteChanged();
        UseSampleImageCommand.NotifyCanExecuteChanged();
        RemoveImageCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}

public sealed record ImageSizeOption(string DisplayName, Size? Size);

public sealed record ImageGeneratorOption(IImageGenerator Generator, ImageGeneratorDescriptor Descriptor, int Index)
{
    public string AutomationId => $"ImageGenerator{Index}Radio";
}

public sealed class GeneratedImageViewModel
{
    public GeneratedImageViewModel(GeneratedImage image)
    {
        Source = image.Bytes is { } bytes
            ? ImageSource.FromStream(() => new MemoryStream(bytes, writable: false))
            : ImageSource.FromUri(image.Uri ??
                throw new InvalidDataException("A generated image needs bytes or a URI."));
        Description = image.MediaType;
    }

    public ImageSource Source { get; }
    public string Description { get; }
}
#pragma warning restore MEAI001
