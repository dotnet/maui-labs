using System.ComponentModel;
using AIExtensions.Sample.ChatPlayground.Features.Images.Services;
using AIExtensions.Sample.ChatPlayground.Shared.Models;
using AIExtensions.Sample.ChatPlayground.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.ChatPlayground.Features.Images.ViewModels;

/// <summary>Turns the selected real image generator's result into one preview.</summary>
public sealed partial class ImagePlaygroundViewModel : ObservableObject
{
    private readonly ImageGenerationService _generation;
    private readonly ImageInputService _imageInput;
    private ImageAttachment? _original;

    public ImagePlaygroundViewModel(
        ImageGenerationService generation, ImageInputService imageInput, ImageSettingsViewModel settings)
    {
        _generation = generation;
        _imageInput = imageInput;
        Settings = settings;
        Settings.PropertyChanged += SettingsPropertyChanged;
        StatusMessage = Settings.HasGenerator
            ? "Ready to generate or edit an image."
            : "No image generator configured.";
    }

    public ImageSettingsViewModel Settings { get; }

    [ObservableProperty] private string prompt = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string originalImageName = string.Empty;
    [ObservableProperty] private ImageSource? originalImagePreview;
    [ObservableProperty] private ImageSource? generatedImage;
    [ObservableProperty] private bool isBusy;

    public bool HasOriginalImage => _original is not null;
    public bool HasResult => GeneratedImage is not null;
    public bool SupportsEdits => Settings.SupportsEdits;
    public bool IsIdle => !IsBusy;

    public IAsyncRelayCommand Generate => GenerateCommand;
    public System.Windows.Input.ICommand CancelGeneration => GenerateCancelCommand;
    public IAsyncRelayCommand ChooseImage => ChooseImageCommand;
    public IAsyncRelayCommand UseSampleImage => UseSampleImageCommand;
    public IRelayCommand RemoveOriginalImage => RemoveImageCommand;

    private bool CanGenerate() =>
        !IsBusy && Settings.HasGenerator && !string.IsNullOrWhiteSpace(Prompt);

    [RelayCommand(CanExecute = nameof(CanGenerate), IncludeCancelCommand = true)]
    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        var option = Settings.SelectedOption ?? throw new InvalidOperationException("Select an image generator.");
        IsBusy = true;
        Settings.IsBusy = true;
        StatusMessage = _original is null ? "Generating image..." : "Editing image...";
        try
        {
            var images = await _generation.GenerateAsync(
                option.Generator, Prompt, _original, Settings.CreateOptions(), cancellationToken);
            var first = images[0];
            GeneratedImage = first.Bytes is { } bytes
                ? ImageSource.FromStream(() => new MemoryStream(bytes, writable: false))
                : ImageSource.FromUri(first.Uri ?? throw new InvalidDataException("A generated image needs bytes or a URI."));
            StatusMessage =
                $"Generated an image with {option.Descriptor.DisplayName}." +
                (images.Count > 1 ? $" Showing the first of {images.Count} returned images." : string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Image request cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Image generation failed: {exception.Message}";
        }
        finally
        {
            Settings.IsBusy = false;
            IsBusy = false;
        }
    }

    private bool CanEdit() => !IsBusy && SupportsEdits;

    [RelayCommand(CanExecute = nameof(CanEdit))]
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

    [RelayCommand(CanExecute = nameof(CanEdit))]
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

    private bool CanRemoveImage() => !IsBusy && HasOriginalImage;

    [RelayCommand(CanExecute = nameof(CanRemoveImage))]
    private void RemoveImage() => ClearImage();

    private void SetOriginalImage(ImageAttachment image)
    {
        _original = image;
        OriginalImageName = image.FileName;
        OriginalImagePreview = ImageSource.FromStream(() => new MemoryStream(image.Bytes, writable: false));
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

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageSettingsViewModel.SelectedOption))
        {
            OnPropertyChanged(nameof(SupportsEdits));
            GeneratedImage = null;
            if (!SupportsEdits && HasOriginalImage)
                ClearImage();
            StatusMessage = Settings.HasGenerator
                ? $"Selected {Settings.SelectedDescriptor!.DisplayName}. Enter a prompt to generate."
                : "No image generator is registered.";
            RefreshCommands();
        }
    }

    partial void OnGeneratedImageChanged(ImageSource? value) =>
        OnPropertyChanged(nameof(HasResult));

    partial void OnPromptChanged(string value) =>
        GenerateCommand.NotifyCanExecuteChanged();

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
    }
}
