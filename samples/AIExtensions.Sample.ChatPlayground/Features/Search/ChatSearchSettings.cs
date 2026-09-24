using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIExtensions.Sample.ChatPlayground.Features.Search;

/// <summary>Shares one selected search method and its options between the playground and replay picker.</summary>
public sealed partial class ChatSearchSettings : ObservableObject
{
    private ChatSearchDescriptor _selectedMode = ChatSearchDescriptor.Contains;

    public ChatSearchSettings(IReadOnlyList<ChatSearchDescriptor> searchModes)
    {
        ArgumentNullException.ThrowIfNull(searchModes);
        SearchModes = searchModes;
    }

    public IReadOnlyList<ChatSearchDescriptor> SearchModes { get; }

    public ChatSearchDescriptor SelectedMode
    {
        get => _selectedMode;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SearchModes.Contains(value))
                throw new ArgumentException("The selected search method is not registered.", nameof(value));
            SetProperty(ref _selectedMode, value);
        }
    }

    [ObservableProperty] private string dimensions = string.Empty;

    public int? SelectedDimensions
    {
        get
        {
            if (SelectedMode.Id == ChatSearchDescriptor.ContainsId || string.IsNullOrWhiteSpace(Dimensions))
                return null;
            if (!int.TryParse(Dimensions, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
                throw new ArgumentException("Dimensions must be a positive integer, or blank to use the model default.");
            return value;
        }
    }
}
