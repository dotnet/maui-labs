using AppKit;
using Foundation;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class ShellHandler
{
    ShellContentContainer? _contentContainer;
    ShellTabBarCoordinator? _tabCoordinator;

    void ConnectTabs()
    {
        if (_shell == null || _contentContainer == null)
            return;

        _tabCoordinator = new ShellTabBarCoordinator(_shell, UpdateTabs);
        _contentContainer.Tabs.Activated += OnTabActivated;
        UpdateTabs();
    }

    void DisconnectTabs()
    {
        if (_contentContainer != null)
            _contentContainer.Tabs.Activated -= OnTabActivated;
        _tabCoordinator?.Dispose();
        _tabCoordinator = null;
    }

    void UpdateTabs()
    {
        if (!NSThread.IsMain)
        {
            NSApplication.SharedApplication.InvokeOnMainThread(UpdateTabs);
            return;
        }
        if (_tabCoordinator != null && _contentContainer != null)
            _contentContainer.UpdateTabs(_tabCoordinator);
    }

    void OnTabActivated(object? sender, EventArgs e)
    {
        // The CurrentItem mapper schedules the page refresh after the model updates.
        if (_contentContainer != null)
            _tabCoordinator?.Select((int)_contentContainer.Tabs.SelectedSegment);
    }
}
