using AIExtensions.Sample.ChatPlayground.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using Size = System.Drawing.Size;

namespace AIExtensions.Sample.ChatPlayground.ViewModels;

#pragma warning disable MEAI001 // IImageGenerator and its options are experimental in the installed SDK.
/// <summary>Owns image-provider selection and optional request settings.</summary>
public sealed partial class ImageSettingsViewModel : ObservableObject
{
    public ImageSettingsViewModel(IEnumerable<IImageGenerator> generators)
    {
        ArgumentNullException.ThrowIfNull(generators);
        Generators = generators.Select((generator, index) => new ImageGeneratorOption(generator, index)).ToArray();
        if (Generators.Select(option => option.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != Generators.Count)
            throw new ArgumentException("Image generators need distinct IDs.", nameof(generators));
        SelectedOption = Generators.FirstOrDefault();
        SelectedSize = ImageSizes[0];
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

    [ObservableProperty] private ImageGeneratorOption? selectedOption;
    [ObservableProperty] private ImageSizeOption? selectedSize;
    [ObservableProperty] private string selectedMediaType = "Provider default";
    [ObservableProperty] private bool isBusy;

    public IImageGenerator? SelectedGenerator => SelectedOption?.Generator;
    public ImageGeneratorDescriptor? SelectedDescriptor => SelectedOption?.Descriptor;
    public bool HasGenerator => SelectedOption is not null;
    public bool SupportsEdits => SelectedDescriptor?.SupportsEdits == true;
    public bool IsIdle => !IsBusy;

    public ImageGenerationOptions? CreateOptions()
    {
        if (SelectedSize is not null && !ImageSizes.Contains(SelectedSize))
            throw new ArgumentException("Choose a supported image size.", nameof(SelectedSize));
        if (!MediaTypes.Contains(SelectedMediaType))
            throw new ArgumentException("Choose a supported image format.", nameof(SelectedMediaType));
        var size = SelectedSize?.Size;
        if (size is null && SelectedMediaType == "Provider default")
            return null;
        return new ImageGenerationOptions
        {
            ImageSize = size,
            MediaType = SelectedMediaType == "Provider default" ? null : SelectedMediaType,
        };
    }

    partial void OnSelectedOptionChanging(ImageGeneratorOption? value)
    {
        if (value is not null && !Generators.Contains(value))
            throw new ArgumentException("The selected image generator is not registered.", nameof(value));
    }

    partial void OnSelectedOptionChanged(ImageGeneratorOption? value)
    {
        OnPropertyChanged(nameof(SelectedGenerator));
        OnPropertyChanged(nameof(SelectedDescriptor));
        OnPropertyChanged(nameof(HasGenerator));
        OnPropertyChanged(nameof(SupportsEdits));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));
}

public sealed record ImageSizeOption(string DisplayName, Size? Size);

public sealed record ImageGeneratorOption(IImageGenerator Generator, int Index)
{
    public ImageGeneratorDescriptor Descriptor { get; } =
        Generator.GetService<ImageGeneratorDescriptor>() ?? throw new InvalidOperationException($"Image generator {Index} did not expose its descriptor.");

    public string AutomationId => $"ImageGenerator{Index}Radio";
}
#pragma warning restore MEAI001
