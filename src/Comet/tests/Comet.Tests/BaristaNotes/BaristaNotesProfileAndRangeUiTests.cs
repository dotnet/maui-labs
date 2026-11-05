#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CometSamples.BaristaNotes.Components;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using Xunit;

namespace Comet.Tests.BaristaNotes;

public sealed class BaristaNotesProfileAndRangeUiTests
{
    [Fact]
    public async Task ProfileFeedback_IsPresentWhileActiveAndFullyAbsentAfterSourceDuration()
    {
        var delay = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var requestedDuration = 0;
        string? visibleMessage = null;
        var publishedStates = new List<string?>();
        var controller = new ProfileFeedbackController(duration =>
        {
            requestedDuration = duration;
            return delay.Task;
        });

        var showing = controller.ShowAsync("Profile saved", message =>
        {
            visibleMessage = message;
            publishedStates.Add(message);
        });

        Assert.Equal("Profile saved", visibleMessage);
        Assert.Equal(new string?[] { "Profile saved" }, publishedStates);
        Assert.Equal(2000, requestedDuration);
        Assert.Equal(56, ProfileFeedbackContract.Height);
        Assert.Equal(16, ProfileFeedbackContract.HorizontalMargin);
        Assert.Equal(88, ProfileFeedbackContract.BottomClearance);

        delay.SetResult(true);
        await showing;

        Assert.Null(visibleMessage);
        Assert.Equal(new string?[] { "Profile saved", null }, publishedStates);
    }

    [Fact]
    public async Task ProfileFeedback_OlderDismissalDoesNotHideNewerMessage()
    {
        var delays = new Queue<TaskCompletionSource<bool>>();
        var controller = new ProfileFeedbackController(_ =>
        {
            var delay = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            delays.Enqueue(delay);
            return delay.Task;
        });
        string? visibleMessage = null;

        var first = controller.ShowAsync("First", message => visibleMessage = message);
        var second = controller.ShowAsync("Second", message => visibleMessage = message);

        delays.Dequeue().SetResult(true);
        await first;
        Assert.Equal("Second", visibleMessage);

        delays.Dequeue().SetResult(true);
        await second;
        Assert.Null(visibleMessage);
    }

    [Fact]
    public async Task ProfileMutationRefresh_PublishesOneCoherentSnapshotWithoutFeedbackDelay()
    {
        var service = new SnapshotProfileService(
            new UserProfileDto { Id = 1, Name = "David" },
            new UserProfileDto { Id = 2, Name = "Delete me" });
        service.AvatarPaths[1] = "david.jpg";
        service.AvatarPaths[2] = "delete-me.jpg";
        var loader = new ProfileManagementSnapshotLoader(service);

        await service.DeleteProfileAsync(2);
        var snapshot = await loader.LoadAsync();

        var profile = Assert.Single(snapshot.Profiles);
        Assert.Equal(1, profile.Id);
        Assert.Equal("David", profile.Name);
        Assert.Equal("david.jpg", Assert.Single(snapshot.AvatarPaths).Value);
        Assert.False(snapshot.AvatarPaths.ContainsKey(2));
        Assert.False(snapshot.IsLoading);
        Assert.Null(snapshot.ErrorMessage);
    }

    [Theory]
    [InlineData("", "", "Profile name is required")]
    [InlineData("   ", "", "Profile name is required")]
    [InlineData(
        "123456789012345678901234567890123456789012345678901",
        "",
        "Profile name must be 50 characters or less")]
    public void ProfileValidation_MatchesSourceNameRules(
        string name,
        string context,
        string expected)
    {
        Assert.Equal(expected, ProfileFormValidation.Validate(name, context));
    }

    [Fact]
    public void ProfileValidation_RejectsContextBeyondSourceLimit()
    {
        Assert.Equal(
            "About this person must be 2000 characters or less",
            ProfileFormValidation.Validate("David", new string('x', 2001)));
        Assert.Null(ProfileFormValidation.Validate("David", new string('x', 2000)));
    }

    [Fact]
    public async Task ProfilePhotoMedia_ReportsExplicitUnavailableAndForwardsCamera()
    {
        ProfilePhotoMedia.CaptureFromCamera = null;
        var unavailable = await ProfilePhotoMedia.CaptureAsync();
        Assert.Equal(PhotoOperationStatus.Unavailable, unavailable.Status);
        Assert.Equal("camera_not_connected", unavailable.ErrorCode);

        var expected = PhotoOperationResult.Cancelled();
        CancellationToken received = default;
        using var cancellation = new CancellationTokenSource();
        ProfilePhotoMedia.CaptureFromCamera = token =>
        {
            received = token;
            return Task.FromResult(expected);
        };
        try
        {
            var result = await ProfilePhotoMedia.CaptureAsync(cancellation.Token);
            Assert.Same(expected, result);
            Assert.Equal(cancellation.Token, received);
        }
        finally
        {
            ProfilePhotoMedia.CaptureFromCamera = null;
        }
    }

    [Fact]
    public void EqualRange_IsImmediatelyInvalidWithSourceMessage()
    {
        var definition = BrewMethodValueRangeCatalog.GetDefinition(
            BrewMethod.Espresso,
            DrinkValueMetric.DoseIn);
        var unit = DrinkValueRangeFormatting.GetEditorUnit(
            DrinkValueMetric.DoseIn,
            BrewMethod.Espresso);

        var valid = ValueRangeEditorValidation.TryGetCanonicalRange(
            DrinkValueMetric.DoseIn,
            definition,
            unit,
            "30",
            "30",
            "5",
            "30",
            5,
            30,
            out _,
            out var error,
            CultureInfo.InvariantCulture);

        Assert.False(valid);
        Assert.Equal("Minimum must be less than maximum.", error);
    }

    [Fact]
    public void ValidRange_SavesAndRecommendedRestoresFallbackThenAuto()
    {
        var service = CreateRangeService();
        var definition = BrewMethodValueRangeCatalog.GetDefinition(
            BrewMethod.Espresso,
            DrinkValueMetric.DoseIn);
        var unit = DrinkValueRangeFormatting.GetEditorUnit(
            DrinkValueMetric.DoseIn,
            BrewMethod.Espresso);

        Assert.True(ValueRangeEditorValidation.TryGetCanonicalRange(
            DrinkValueMetric.DoseIn,
            definition,
            unit,
            "20",
            "25",
            "5",
            "30",
            5,
            30,
            out var range,
            out var error,
            CultureInfo.InvariantCulture));
        Assert.Null(error);

        service.SaveOverride(
            DrinkValueMetric.DoseIn,
            BrewMethod.Espresso,
            range.Minimum,
            range.Maximum);
        Assert.Equal(
            ValueRangeSource.Custom,
            service.Resolve(DrinkValueMetric.DoseIn, BrewMethod.Espresso).Source);

        service.RemoveOverride(DrinkValueMetric.DoseIn, BrewMethod.Espresso);
        Assert.Equal(
            ValueRangeSource.AutoFallback,
            service.Resolve(DrinkValueMetric.DoseIn, BrewMethod.Espresso).Source);

        service.SetMode(DrinkValueMetric.DoseIn, ValueRangeMode.Auto);
        Assert.Equal(
            ValueRangeSource.Auto,
            service.Resolve(DrinkValueMetric.DoseIn, BrewMethod.Espresso).Source);
    }

    [Fact]
    public void ProfileAndRangePages_ExposeSourceActionsOnActionableControls()
    {
        var root = FindCometRoot();
        Assert.NotNull(root);

        var profileComponents = File.ReadAllText(System.IO.Path.Combine(
            root!,
            "sample/Shared/BaristaNotes/Components/ProfileComponents.cs"));
        var profilePage = File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/Pages/ProfileDetailPage.cs"));
        var managementPage = File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/Pages/ProfileManagementPage.cs"));
        var rangePage = File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/Pages/ValueRangeEditorPage.cs"));
        var rangeSettingsPage = File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/Pages/ValueRangeSettingsPage.cs"));
        var coffeeSpacing = File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/Styles/CoffeeSpacing.cs"));

        Assert.Contains("\"profile_management_actions\"", managementPage);
        Assert.Contains("\"profile_form_actions\"", profilePage);
        Assert.Contains("public const float ActionRowHeight = 72f;", coffeeSpacing);
        Assert.Contains("BaristaEdgeFrame.Build(", managementPage);
        Assert.Contains("rows: new object[] { \"Auto\" }", profileComponents);
        Assert.Contains(".MinimumHeight(CoffeeSpacing.ActionRowHeight)", profileComponents);
        Assert.DoesNotContain(".Frame(height: CoffeeSpacing.ActionRowHeight)", profileComponents);
        Assert.Contains("showLoading: false,", managementPage);
        Assert.Contains("_profileList?.ReloadData()", managementPage);
        Assert.Contains("if (_success.Value is { } successMessage)", managementPage);
        Assert.Contains("new ProfileSuccessToast(successMessage)", managementPage);
        Assert.DoesNotContain("new ProfileSuccessToast(_success)", managementPage);
        Assert.DoesNotContain("Frame(width: 0, height: 0)", profileComponents);
        Assert.Contains("$\"Profile '{name}' saved\"", profilePage);
        Assert.Contains("$\"Profile '{_name.Value}' deleted\"", profilePage);
        Assert.Contains("var presentationToken = presentationCancellation.Token", profilePage);
        Assert.Contains("_lifetimeCancellation.Token", profilePage);
        Assert.Contains("cancellationToken.IsCancellationRequested", managementPage);
        Assert.DoesNotContain(".AutomationId(\"profile_fixed_actions\")", profileComponents);
        Assert.Contains("_effectiveProfileId = result.ProfileId", profilePage);
        Assert.Contains("Profile saved, but the photo was not saved:", profilePage);
        Assert.Contains("if (_disposed || IsOperationActive)", profilePage);
        Assert.Contains("enabled: !IsOperationActive", profilePage);
        Assert.Contains("void HandleSystemBack()", profilePage);
        Assert.Contains("new ProfileSaveOperation(Service).ExecuteAsync(", profilePage);
        Assert.Contains("CanPublishSave(generation, presentationToken)", profilePage);
        Assert.Contains("PublishPhotoOutcome(source, PhotoOperationResult.Cancelled())", profilePage);
        Assert.Contains("PhotoOperationFeedback.FromResult(source, result)", profilePage);
        Assert.Contains("\"Photo updated.\"", profilePage);
        Assert.Contains("\"Photo removed.\"", profilePage);
        Assert.Contains("$\"{_automationId}_confirm\"", profileComponents);
        Assert.Contains("$\"{_automationId}_dismiss\"", profileComponents);
        Assert.Contains("\"profile_photo_camera\"", profileComponents);
        Assert.Contains("\"profile_photo_gallery\"", profileComponents);
        Assert.Contains("new Button(\"CAMERA\", _camera)", profileComponents);
        Assert.Contains("new Button(\"GALLERY\", _gallery)", profileComponents);
        Assert.Contains(".AutomationId(\"RangeValidationError\")", rangePage);
        Assert.Contains("new RangeTextField(value, placeholder, onChanged)", rangePage);
        Assert.Contains("_onChanged(text)", rangePage);
        Assert.Contains("_discardDialogOpen.Value = true", rangePage);
        Assert.Contains("_allowNavigation = true", rangePage);
        Assert.Contains(".AutomationId(\"RangeDiscardConfirm\")", rangePage);
        Assert.Contains(".AutomationId(\"RangeDiscardKeepEditing\")", rangePage);
        Assert.Contains(".AutomationId(\"RangeRecommendedConfirm\")", rangePage);
        Assert.Contains(".AutomationId(\"UseRecommendedRange\")", rangePage);
        Assert.Contains(".AutomationId(\"RangeResetAll\")", rangeSettingsPage);
        Assert.Contains(".AutomationId(\"RangeResetConfirm\")", rangeSettingsPage);
        Assert.Contains(".AutomationId(\"RangeResetCancel\")", rangeSettingsPage);
    }

    [Fact]
    public void ProfileFlows_UseSourcePickerFormAndManagementFooter()
    {
        var root = FindCometRoot();
        Assert.NotNull(root);

        var shotPage = File.ReadAllText(System.IO.Path.Combine(
            root!,
            "sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs"));
        var profilePage = File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/Pages/ProfileDetailPage.cs"));
        var managementPage = File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/Pages/ProfileManagementPage.cs"));

        Assert.DoesNotContain("CreatePerson", shotPage);
        Assert.DoesNotContain("picker_add_maker", shotPage);
        Assert.DoesNotContain("picker_add_recipient", shotPage);
        Assert.DoesNotContain("picker_made_by_none", shotPage);
        Assert.DoesNotContain("picker_made_for_none", shotPage);
        Assert.Contains("DrinkPickerKind.People => \"MADE BY / FOR\"", shotPage);
        Assert.Contains("\"picker_people_direction\"", shotPage);
        Assert.Contains(".FontSize(12)", shotPage);
        Assert.Contains("nameof(ITextStyle.CharacterSpacing), 3d", shotPage);
        Assert.Contains(".FontSize(11)", shotPage);
        Assert.Contains("new Thickness(0, 0, 0, safeArea.Bottom)", shotPage);
        Assert.Contains("isSelected ? 72 : 52", shotPage);
        Assert.Contains("isSelected ? 18 : 14", shotPage);
        Assert.Contains("highlighted: isSelected", shotPage);
        Assert.Contains("highlighted: isSelected)\n                        .Center()", shotPage);
        Assert.Contains("new ProfileDetailPage(null, OnProfileCreatedFromShotAsync)", shotPage);

        Assert.Contains(".FontFamily(\"ManropeSemibold\")", profilePage);
        Assert.Contains(".FontSize(22)", profilePage);
        Assert.Contains("SignalExtensions.TextEditor(_context)", profilePage);

        Assert.Contains("\"profile_nav_new_drink\"", managementPage);
        Assert.Contains("\"profile_nav_activity\"", managementPage);
        Assert.Contains("\"profile_nav_settings\"", managementPage);
        Assert.Contains("\"profile_nav_add\"", managementPage);
        Assert.Contains("CoffeeIcons.Add", managementPage);
        Assert.Contains("Inverted: true", managementPage);
        Assert.DoesNotContain("\"profiles_back\"", managementPage);
    }

    sealed class SnapshotProfileService : IUserProfileService
    {
        readonly List<UserProfileDto> _profiles;

        public SnapshotProfileService(params UserProfileDto[] profiles) =>
            _profiles = profiles.ToList();

        public Dictionary<int, string?> AvatarPaths { get; } = new();

        public Task<List<UserProfileDto>> GetAllProfilesAsync() =>
            Task.FromResult(_profiles.ToList());

        public Task DeleteProfileAsync(int id)
        {
            _profiles.RemoveAll(profile => profile.Id == id);
            AvatarPaths.Remove(id);
            return Task.CompletedTask;
        }

        public Task<string?> GetProfileImagePathAsync(int profileId) =>
            Task.FromResult(AvatarPaths.GetValueOrDefault(profileId));

        public Task<UserProfileDto?> GetProfileByIdAsync(int id) =>
            Task.FromResult(_profiles.SingleOrDefault(profile => profile.Id == id));

        public Task<UserProfileDto> CreateProfileAsync(CreateUserProfileDto dto) =>
            throw new NotSupportedException();

        public Task<UserProfileDto> UpdateProfileAsync(int id, UpdateUserProfileDto dto) =>
            throw new NotSupportedException();

        public Task<ProfileImageUpdateResult> UpdateProfileImageAsync(int profileId, Stream imageStream) =>
            throw new NotSupportedException();

        public Task<bool> RemoveProfileImageAsync(int profileId) =>
            throw new NotSupportedException();
    }

    static InMemoryDrinkValueRangeService CreateRangeService()
    {
        return new InMemoryDrinkValueRangeService(
            new InMemoryPreferencesService());
    }

    static string? FindCometRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var index = 0; index < 10 && directory is not null; index++)
        {
            if (File.Exists(System.IO.Path.Combine(directory, "global.json"))
                && Directory.Exists(System.IO.Path.Combine(directory, "sample")))
                return directory;
            directory = System.IO.Path.GetDirectoryName(directory);
        }
        return null;
    }
}
