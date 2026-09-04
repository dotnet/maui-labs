using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// Top-level view model for <see cref="Pages.MainPage"/>.
/// Owns the new-session action and the cart header button.
/// Navigation AI tools have moved to <see cref="AINavigationService"/>.
/// </summary>
public sealed partial class MainViewModel(CurrentCart currentCart) : ObservableObject
{
    private bool _initialized;

    /// <summary>
    /// Called once from <see cref="Pages.MainPage.OnAppearing"/>.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        StartNewSession();
    }

    [RelayCommand]
    private void StartNewSession()
    {
        currentCart.Clear();
        WeakReferenceMessenger.Default.Send(new StartNewChatSessionMessage());
    }

    [RelayCommand]
    private async Task ShowCartAsync()
    {
        await Shell.Current.GoToAsync("cart");
    }
}
