#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Comet;
using CometSamples.BaristaNotes.Pages;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometBaristaNotes.Services.Voice;

namespace CometSamples.BaristaNotes;

/// <summary>
/// App-scoped <see cref="IBaristaVoiceCallbacks"/> that delegates field updates
/// and commits to the active <see cref="ShotLoggingPage"/>, and handles navigation
/// through the <see cref="BaristaNavigationCoordinator"/>.
/// </summary>
public sealed class AppVoiceCallbacks : IBaristaVoiceCallbacks
{
    readonly BaristaNavigationCoordinator _navigation;
    readonly Action<VoiceOverlayState>? _onStateChanged;
    ShotLoggingPage? _activePage;

    public AppVoiceCallbacks(
        BaristaNavigationCoordinator navigation,
        Action<VoiceOverlayState>? onStateChanged = null)
    {
        _navigation = navigation;
        _onStateChanged = onStateChanged;
    }

    public void SetActivePage(ShotLoggingPage? page) => _activePage = page;

    ShotLoggingPage? ResolvePage() =>
        _activePage ?? _navigation.ActiveShotEditor;

    public void OnVoiceStateChanged(VoiceOverlayState state)
    {
        _onStateChanged?.Invoke(state);
    }

    public void ApplyNewDrinkFields(VoiceFieldUpdates updates)
    {
        var page = ResolvePage();
        if (page is null || !updates.HasChanges)
            return;
        page.ApplyVoiceFields(updates);
    }

    public async Task<VoiceToolResultDto> CommitNewDrinkAsync(
        VoiceFieldUpdates updates,
        CancellationToken cancellationToken)
    {
        var page = ResolvePage();
        if (page is null)
            return new VoiceToolResultDto(false, "No active drink editor.");
        return await page.CommitVoiceAsync(updates, cancellationToken);
    }

    public async Task<VoiceNavigationResult> NavigateAsync(
        VoiceNavigationRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanNavigate(request))
        {
            return new VoiceNavigationResult(
                VoiceNavigationOutcome.DestinationUnavailable,
                "I couldn't open that destination.");
        }

        string? message = null;
        var outcome = await _navigation.RequestNavigationAsync(
            () => message = NavigateCore(request),
            cancellationToken);
        return new VoiceNavigationResult(outcome, message);
    }

    static bool CanNavigate(VoiceNavigationRequest request) =>
        request.Destination switch
        {
            "new-drink" or "activity" or "settings" or "beans"
                or "equipment" or "profiles" => true,
            "bags" => request.EntityId.HasValue && request.ParentEntityId.HasValue,
            _ => false,
        };

    string? NavigateCore(VoiceNavigationRequest request)
    {
        switch (request.Destination)
        {
            case "new-drink":
                _navigation.SwitchTo(BaristaSection.NewDrink);
                return null;
            case "activity":
                _navigation.SwitchTo(BaristaSection.Activity);
                if (!_navigation.ApplyActivityPeriodFilter(request.Period, request.Filter)
                    && !string.IsNullOrWhiteSpace(request.Filter))
                {
                    return $"Navigated to Activity, but the filter '{request.Filter}' was not recognized.";
                }
                return null;
            case "settings":
                _navigation.SwitchTo(BaristaSection.Settings);
                return null;
            case "beans":
                _navigation.OpenBeanManagement(request.EntityId);
                return null;
            case "bags" when request.EntityId.HasValue && request.ParentEntityId.HasValue:
                _navigation.OpenBagManagement(
                    request.EntityId.Value,
                    request.ParentEntityId.Value,
                    request.ParentEntityName ?? string.Empty);
                return null;
            case "equipment":
                _navigation.OpenEquipmentManagement(request.EntityId);
                return null;
            case "profiles":
                _navigation.OpenProfileManagement(request.EntityId);
                return null;
            default:
                return "I couldn't open that destination.";
        }
    }

    public void ActivateNewDrinkVoice()
    {
        _navigation.RequestNavigation(() =>
        {
            if (_navigation.Section.Value != BaristaSection.NewDrink)
                _navigation.SwitchTo(BaristaSection.NewDrink);
            _navigation.VoiceVisible.Value = true;
            _navigation.VoiceCollapsed.Value = false;
        });
    }
}
