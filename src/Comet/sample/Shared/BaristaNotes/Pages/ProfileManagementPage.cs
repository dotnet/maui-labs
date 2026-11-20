#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes.Pages;

public sealed class ProfileManagementPage : BaristaManagementPage
{
    readonly Action _openNewDrink;
    readonly Action _openActivity;
    readonly Action _openSettings;
    readonly Signal<ProfileManagementSnapshot> _state = new(ProfileManagementSnapshot.Empty);
    readonly Signal<string?> _success = new(null);
    readonly ProfileFeedbackController _successFeedback = new();
    ListView<UserProfileDto>? _profileList;
    int _loadVersion;

    IUserProfileService Service => BaristaServiceLocator.ProfileService;

    public ProfileManagementPage(
        Action? openNewDrink = null,
        Action? openActivity = null,
        Action? openSettings = null)
    {
        _openNewDrink = openNewDrink ?? (() => { });
        _openActivity = openActivity ?? (() => { });
        _openSettings = openSettings ?? (() => NavigationView.Pop(this));
        _ = LoadAsync();
    }

    async Task LoadAsync(
        bool showLoading = true,
        CancellationToken cancellationToken = default)
    {
        var loadVersion = Interlocked.Increment(ref _loadVersion);
        if (showLoading)
        {
            _state.Value = _state.Value with
            {
                IsLoading = true,
                ErrorMessage = null,
            };
        }

        try
        {
            var snapshot = await new ProfileManagementSnapshotLoader(Service).LoadAsync();
            if (cancellationToken.IsCancellationRequested ||
                loadVersion != Volatile.Read(ref _loadVersion))
                return;

            _state.Value = snapshot;
            _profileList?.ReloadData();
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested ||
                loadVersion != Volatile.Read(ref _loadVersion))
                return;

            _state.Value = _state.Value with
            {
                IsLoading = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    void OpenProfile(int? profileId)
        => NavigationView.Navigate(this, new ProfileDetailPage(profileId, RefreshAfterMutation));

    async Task RefreshAfterMutation(
        string? successMessage,
        CancellationToken cancellationToken)
    {
        await LoadAsync(
            showLoading: false,
            cancellationToken: cancellationToken);
        if (cancellationToken.IsCancellationRequested ||
            string.IsNullOrWhiteSpace(successMessage))
            return;

        _ = _successFeedback.ShowAsync(successMessage, message => _success.Value = message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _successFeedback.Cancel();
        base.Dispose(disposing);
    }

    [Body]
    View body()
    {
        var state = _state.Value;
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);
        var page = BaristaEdgeFrame.Build(
            HeaderView(state, safeArea),
            BodyView(state),
            new FixedBottomActionRow(
                new BottomAction(
                    "profile_nav_new_drink",
                    CoffeeIcons.Coffee,
                    _openNewDrink),
                new BottomAction(
                    "profile_nav_activity",
                    CoffeeIcons.Feed,
                    _openActivity),
                new BottomAction(
                    "profile_nav_settings",
                    CoffeeIcons.Settings,
                    _openSettings),
                new BottomAction(
                    "profile_nav_add",
                    CoffeeIcons.Add,
                    () => OpenProfile(null),
                    Inverted: true))
                .AutomationId("profile_management_actions"),
            "profile_management_frame");

        var root = new Grid(
            columns: new object[]
            {
                ProfileFeedbackContract.HorizontalMargin,
                "*",
                ProfileFeedbackContract.HorizontalMargin,
            },
            rows: new object[]
            {
                "*",
                ProfileFeedbackContract.Height,
                ProfileFeedbackContract.BottomClearance,
            })
        {
            page.Cell(row: 0, column: 0, rowSpan: 3, colSpan: 3)
                .Background(CoffeeTheme.OutlineColor)
                .IgnoreSafeArea(),
        };

        if (_success.Value is { } successMessage)
            root.Add(new ProfileSuccessToast(successMessage).Cell(row: 1, column: 1));

        return root.AutomationId("profile_management_page");
    }

    View HeaderView(ProfileManagementSnapshot state, Thickness safeArea)
    {
        var count = state.Profiles.Count;
        var countText = count == 1 ? "1 profile" : $"{count} profiles";
        return BaristaPageHeader.Build("PROFILES", countText, safeArea, "profiles_header");
    }

    View BodyView(ProfileManagementSnapshot state)
    {
        if (state.IsLoading)
        {
            return new ActivityIndicator()
                .Center()
                .Background(CoffeeTheme.SurfaceColor)
                .AutomationId("profiles_loading");
        }

        if (state.ErrorMessage is { } error)
        {
            return new VStack(spacing: 12)
            {
                new Text("ERROR").SectionLabel(),
                new Text(error).SourceText().FontSize(CoffeeFontSizes.BodyMedium),
                new Button("RETRY", () => _ = LoadAsync())
                    .TextButton()
                    .Color(CoffeeTheme.PrimaryColor)
                    .CornerRadius(0)
                    .AutomationId("profiles_retry"),
            }
            .Center()
            .Padding(new Thickness(CoffeeSpacing.L))
            .Background(CoffeeTheme.SurfaceColor)
            .AutomationId("profiles_error");
        }

        if (state.Profiles.Count == 0)
        {
            return new VStack(spacing: 12)
            {
                new Text("NO PROFILES").SectionLabel(),
                new Text("Add household members or coffee personas")
                    .SourceText()
                    .FontSize(CoffeeFontSizes.BodyMedium),
            }
            .Center()
            .Padding(new Thickness(CoffeeSpacing.XL))
            .Background(CoffeeTheme.SurfaceColor)
            .AutomationId("profiles_empty");
        }

        var list = new ListView<UserProfileDto>(() => _state.Value.Profiles)
        {
            ViewFor = ProfileRow,
        };
        _profileList = list;
        return list
            .Background(CoffeeTheme.OutlineColor)
            .AutomationId("profiles_list");
    }

    View ProfileRow(UserProfileDto profile)
    {
        _state.Value.AvatarPaths.TryGetValue(profile.Id, out var avatarPath);
        return ProfileListRow.Build(
            profile.Name,
            new ProfileAvatar(avatarPath, 48, $"profile_avatar_{profile.Id}"),
            $"profile_row_{profile.Id}",
            () => OpenProfile(profile.Id));
    }
}
