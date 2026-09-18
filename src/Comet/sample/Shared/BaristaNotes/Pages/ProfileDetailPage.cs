#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes.Pages;

public sealed class ProfileDetailPage : View
{
    int? _effectiveProfileId;
    readonly Func<string?, CancellationToken, Task>? _onMutated;
    PhotoByteOwnership? _stagedAvatar;
    string? _stagedFileName;

    readonly Signal<string> _name = new(string.Empty);
    readonly Signal<string> _context = new(string.Empty);
    readonly Signal<string?> _avatarPath = new(null);
    readonly Signal<bool> _isLoading = new(false);
    readonly Signal<bool> _isSaving = new(false);
    readonly Signal<bool> _photoLoading = new(false);
    readonly Signal<bool> _deleteLoading = new(false);
    readonly Signal<string?> _photoStatus = new(null);
    readonly Signal<string?> _error = new(null);
    readonly Signal<bool> _deleteDialogOpen = new(false);
    readonly Signal<bool> _photoSourceDialogOpen = new(false);
    readonly CancellationTokenSource _lifetimeCancellation = new();
    CancellationTokenSource? _photoRequestCancellation;
    CancellationTokenSource? _savePresentationCancellation;
    long _saveGeneration;
    bool _disposed;

    IUserProfileService Service => BaristaServiceLocator.ProfileService;
    bool IsEdit => _effectiveProfileId is > 0;
    bool IsOperationActive =>
        _isSaving.Value || _photoLoading.Value || _deleteLoading.Value;

    public ProfileDetailPage(
        int? profileId,
        Func<string?, CancellationToken, Task>? onMutated = null,
        byte[]? stagedAvatarBytes = null)
    {
        _effectiveProfileId = profileId;
        _onMutated = onMutated;
        this.BackButtonBehavior(new BackButtonBehavior
        {
            IsVisible = false,
            Command = new ActionCommand(HandleSystemBack),
        });
        if (stagedAvatarBytes is { Length: > 0 })
            _stagedAvatar = new PhotoByteOwnership(stagedAvatarBytes);
        if (IsEdit)
        {
            _isLoading.Value = true;
            _ = LoadAsync();
        }
        else if (_stagedAvatar?.HasBytes == true)
        {
            // Stage the captured photo as a pending avatar
            _ = StageAvatarAsync();
        }
    }

    async Task StageAvatarAsync()
    {
        var stagedAvatar = _stagedAvatar;
        if (stagedAvatar?.HasBytes != true)
            return;
        var imageProcessing = new LocalImageProcessingService();
        var stagedFileName = $"staged_avatar_{DateTime.UtcNow.Ticks}.jpg";
        try
        {
            _photoLoading.Value = true;
            _stagedFileName = stagedFileName;
            using var stream = stagedAvatar.OpenRead();
            var saved = await imageProcessing.SaveImageAsync(stream, stagedFileName);
            if (_disposed || !ReferenceEquals(_stagedAvatar, stagedAvatar))
            {
                await imageProcessing.DeleteImageAsync(stagedFileName);
                return;
            }
            _avatarPath.Value = saved;
        }
        catch (Exception ex)
        {
            if (!_disposed)
                _error.Value = $"Failed to stage photo: {ex.Message}";
        }
        finally
        {
            if (!_disposed)
                _photoLoading.Value = false;
        }
    }

    async Task LoadAsync()
    {
        var lifetimeToken = _lifetimeCancellation.Token;
        try
        {
            var profile = await Service.GetProfileByIdAsync(_effectiveProfileId!.Value);
            if (_disposed || lifetimeToken.IsCancellationRequested)
                return;
            if (profile is null)
            {
                _error.Value = "Profile not found";
                return;
            }

            _name.Value = profile.Name;
            _context.Value = profile.Context ?? string.Empty;
            var avatarPath = await Service.GetProfileImagePathAsync(profile.Id);
            if (_disposed || lifetimeToken.IsCancellationRequested)
                return;
            _avatarPath.Value = avatarPath;
        }
        catch (Exception ex)
        {
            if (!_disposed && !lifetimeToken.IsCancellationRequested)
                _error.Value = $"Failed to load profile: {ex.Message}";
        }
        finally
        {
            if (!_disposed)
                _isLoading.Value = false;
        }
    }

    void CleanupStagedFile()
    {
        var stagedFileName = Interlocked.Exchange(ref _stagedFileName, null);
        if (stagedFileName is null)
            return;
        try
        {
            new LocalImageProcessingService()
                .DeleteImageAsync(stagedFileName)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                $"Failed to remove staged profile photo '{stagedFileName}': {ex.Message}");
        }
    }

    void ReleaseStagedPhoto()
    {
        _stagedAvatar?.Dispose();
        _stagedAvatar = null;
        CleanupStagedFile();
    }

    void Cancel()
    {
        if (_disposed || IsOperationActive)
            return;

        ReleaseStagedPhoto();
        NavigationView.Pop(this);
    }

    void HandleSystemBack()
    {
        if (IsOperationActive)
            return;

        Cancel();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            Interlocked.Increment(ref _saveGeneration);
            _lifetimeCancellation.Cancel();
            _savePresentationCancellation?.Cancel();
            _savePresentationCancellation?.Dispose();
            _savePresentationCancellation = null;
            _photoRequestCancellation?.Cancel();
            _photoRequestCancellation?.Dispose();
            _photoRequestCancellation = null;
            _lifetimeCancellation.Dispose();
            ReleaseStagedPhoto();
        }
        base.Dispose(disposing);
    }

    bool Validate()
    {
        _error.Value = ProfileFormValidation.Validate(_name.Value, _context.Value);
        return _error.Value is null;
    }

    async Task SaveAsync()
    {
        if (_disposed || IsOperationActive || !Validate())
            return;

        var generation = Interlocked.Increment(ref _saveGeneration);
        _savePresentationCancellation?.Dispose();
        var presentationCancellation = new CancellationTokenSource();
        var presentationToken = presentationCancellation.Token;
        _savePresentationCancellation = presentationCancellation;
        var operationAvatar = _stagedAvatar?.Transfer();
        _stagedAvatar?.Dispose();
        _stagedAvatar = null;
        var ownerRefresh = new ProfileOwnerRefreshOperation(_onMutated);
        _deleteDialogOpen.Value = false;
        _photoSourceDialogOpen.Value = false;
        _isSaving.Value = true;
        try
        {
            var name = _name.Value.Trim();
            var context = string.IsNullOrWhiteSpace(_context.Value) ? null : _context.Value;
            var result = await new ProfileSaveOperation(Service).ExecuteAsync(
                _effectiveProfileId,
                name,
                context,
                operationAvatar,
                presentationToken);
            operationAvatar = null;

            ProfileOwnerRefreshResult? refreshResult = null;
            if (result.IsCommitted)
            {
                refreshResult = await ownerRefresh.ExecuteAsync(
                    result.Status == ProfileSaveOperationStatus.Success
                        ? $"Profile '{name}' saved"
                        : null);
            }

            if (!CanPublishSave(generation, presentationToken))
            {
                result.RetryAvatar?.Dispose();
                return;
            }

            if (result.ProfileId > 0)
                _effectiveProfileId = result.ProfileId;

            if (result.Status == ProfileSaveOperationStatus.PhotoFailed)
            {
                _stagedAvatar = result.RetryAvatar;
                _error.Value =
                    $"Profile saved, but the photo was not saved: {result.ErrorMessage ?? "unknown error"}" +
                    OwnerRefreshFailureSuffix(refreshResult);
                return;
            }

            if (result.Status == ProfileSaveOperationStatus.CompensationFailed)
            {
                _stagedAvatar = result.RetryAvatar;
                _error.Value =
                    "The profile was created without its photo, and automatic rollback failed. " +
                    $"The saved profile remains available. {result.ErrorMessage}" +
                    OwnerRefreshFailureSuffix(refreshResult);
                return;
            }

            if (result.Status == ProfileSaveOperationStatus.Failed)
            {
                _stagedAvatar = result.RetryAvatar;
                _error.Value = $"Failed to save: {result.ErrorMessage}";
                return;
            }

            if (result.Status == ProfileSaveOperationStatus.ReadbackFailed)
            {
                _error.Value =
                    "The profile and photo were saved, but the photo preview could not be refreshed. " +
                    result.ErrorMessage +
                    OwnerRefreshFailureSuffix(refreshResult);
                return;
            }

            if (result.Status != ProfileSaveOperationStatus.Success)
                return;

            CleanupStagedFile();
            if (result.AvatarPath is not null)
                _avatarPath.Value = result.AvatarPath;

            if (refreshResult?.Status == ProfileOwnerRefreshStatus.Failed)
            {
                _error.Value =
                    "The profile was saved, but the profile list could not be refreshed. " +
                    refreshResult.ErrorMessage;
                return;
            }

            if (CanPublishSave(generation, presentationToken))
                NavigationView.Pop(this);
        }
        catch (Exception ex)
        {
            if (CanPublishSave(generation, presentationToken))
                _error.Value = $"Profile was saved, but the screen could not finish: {ex.Message}";
        }
        finally
        {
            operationAvatar?.Dispose();
            if (ReferenceEquals(
                _savePresentationCancellation,
                presentationCancellation))
            {
                _savePresentationCancellation = null;
                presentationCancellation.Dispose();
                if (!_disposed && generation == Volatile.Read(ref _saveGeneration))
                    _isSaving.Value = false;
            }
        }
    }

    bool CanPublishSave(
        long generation,
        CancellationToken presentationToken) =>
        !_disposed &&
        generation == Volatile.Read(ref _saveGeneration) &&
        !presentationToken.IsCancellationRequested;

    async Task DeleteAsync()
    {
        if (_disposed || IsOperationActive || !IsEdit)
            return;

        _deleteDialogOpen.Value = false;
        _photoSourceDialogOpen.Value = false;
        _deleteLoading.Value = true;
        var ownerRefresh = new ProfileOwnerRefreshOperation(_onMutated);
        var committed = false;
        try
        {
            await Service.DeleteProfileAsync(_effectiveProfileId!.Value);
            committed = true;
            var refresh = await ownerRefresh.ExecuteAsync(
                $"Profile '{_name.Value}' deleted");
            if (_disposed)
                return;
            if (refresh.Status == ProfileOwnerRefreshStatus.Failed)
            {
                _error.Value =
                    "The profile was deleted, but the profile list could not be refreshed. " +
                    refresh.ErrorMessage;
                return;
            }
            NavigationView.Pop(this);
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                _error.Value = committed
                    ? $"The profile was deleted, but the screen could not finish: {ex.Message}"
                    : $"Failed to delete: {ex.Message}";
            }
        }
        finally
        {
            if (!_disposed)
                _deleteLoading.Value = false;
        }
    }

    void OpenPhotoSourceDialog()
    {
        if (_disposed || IsOperationActive || !IsEdit)
            return;

        _photoStatus.Value = null;
        _error.Value = null;
        _photoSourceDialogOpen.Value = true;
    }

    async Task PickPhotoAsync(PhotoSource source)
    {
        if (_disposed || IsOperationActive || !IsEdit)
            return;

        _photoRequestCancellation?.Cancel();
        _photoRequestCancellation?.Dispose();
        _photoRequestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        var requestCancellation = _photoRequestCancellation;
        var requestToken = requestCancellation.Token;

        _photoSourceDialogOpen.Value = false;
        _photoLoading.Value = true;
        _photoStatus.Value = null;
        _error.Value = null;
        try
        {
            var selection = source == PhotoSource.Camera
                ? await ProfilePhotoMedia.CaptureAsync(requestToken)
                : await ProfilePhotoMedia.PickAsync(requestToken);
            if (_disposed || requestToken.IsCancellationRequested)
                return;

            if (selection.Status != PhotoOperationStatus.Success || selection.Photo is null)
            {
                PublishPhotoOutcome(source, selection);
                return;
            }

            await using var imageStream = new MemoryStream(
                selection.Photo.Bytes,
                writable: false);
            var mutation = await new ProfilePhotoMutationOperation(Service).UpdateAsync(
                _effectiveProfileId!.Value,
                imageStream);
            ProfileOwnerRefreshResult? refresh = null;
            if (mutation.IsCommitted)
            {
                refresh = await new ProfileOwnerRefreshOperation(_onMutated)
                    .ExecuteAsync(null);
            }

            if (_disposed)
                return;
            if (mutation.Status == ProfilePhotoMutationStatus.MutationFailed)
            {
                PublishPhotoOutcome(
                    source,
                    PhotoOperationResult.Error(
                        "profile_photo_update_failed",
                        mutation.ErrorMessage ?? "The profile photo could not be saved."));
                return;
            }
            if (mutation.Status == ProfilePhotoMutationStatus.ReadbackFailed)
            {
                _error.Value =
                    "The photo was saved, but its preview could not be refreshed. " +
                    mutation.ErrorMessage +
                    OwnerRefreshFailureSuffix(refresh);
                return;
            }
            if (refresh?.Status == ProfileOwnerRefreshStatus.Failed)
            {
                _error.Value =
                    "The photo was saved, but the profile list could not be refreshed. " +
                    refresh.ErrorMessage;
                return;
            }

            PublishOnMainThread(() =>
            {
                _avatarPath.Value = mutation.AvatarPath;
                _photoStatus.Value = "Photo updated.";
                _error.Value = null;
            });
        }
        catch (OperationCanceledException) when (!_disposed)
        {
            PublishPhotoOutcome(source, PhotoOperationResult.Cancelled());
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Profile photo workflow failed: {ex}");
            PublishPhotoOutcome(
                source,
                PhotoOperationResult.Error(
                    "profile_photo_failed",
                    "The profile photo could not be updated."));
        }
        finally
        {
            if (ReferenceEquals(_photoRequestCancellation, requestCancellation))
            {
                _photoRequestCancellation = null;
                requestCancellation.Dispose();
            }
            PublishOnMainThread(() => _photoLoading.Value = false);
        }
    }

    void PublishPhotoOutcome(PhotoSource source, PhotoOperationResult result)
    {
        var feedback = PhotoOperationFeedback.FromResult(source, result);
        PublishOnMainThread(() =>
        {
            _photoStatus.Value = feedback.Message;
            _error.Value = null;
        });
    }

    void PublishOnMainThread(Action action)
    {
        if (_disposed)
            return;
        ThreadHelper.RunOnMainThread(() =>
        {
            if (!_disposed)
                action();
        });
    }

    async Task RemovePhotoAsync()
    {
        if (_disposed || IsOperationActive || !IsEdit ||
            string.IsNullOrWhiteSpace(_avatarPath.Value))
            return;

        _photoLoading.Value = true;
        _photoStatus.Value = null;
        _error.Value = null;
        try
        {
            if (_stagedAvatar?.HasBytes == true)
                ReleaseStagedPhoto();
            var mutation = await new ProfilePhotoMutationOperation(Service).RemoveAsync(
                _effectiveProfileId!.Value);
            ProfileOwnerRefreshResult? refresh = null;
            if (mutation.IsCommitted)
            {
                refresh = await new ProfileOwnerRefreshOperation(_onMutated)
                    .ExecuteAsync(null);
            }

            if (_disposed)
                return;
            if (mutation.Status == ProfilePhotoMutationStatus.MutationFailed)
            {
                _error.Value = $"Failed to remove image: {mutation.ErrorMessage}";
                return;
            }
            if (mutation.Status == ProfilePhotoMutationStatus.ReadbackFailed)
            {
                _error.Value =
                    "The photo was removed, but the profile preview could not be refreshed. " +
                    mutation.ErrorMessage +
                    OwnerRefreshFailureSuffix(refresh);
                return;
            }
            if (refresh?.Status == ProfileOwnerRefreshStatus.Failed)
            {
                _error.Value =
                    "The photo was removed, but the profile list could not be refreshed. " +
                    refresh.ErrorMessage;
                return;
            }

            _avatarPath.Value = mutation.AvatarPath;
            _photoStatus.Value = mutation.Status == ProfilePhotoMutationStatus.Success
                ? "Photo removed."
                : "There was no saved photo to remove.";
        }
        catch (Exception ex)
        {
            if (!_disposed)
                _error.Value = $"Failed to remove image: {ex.Message}";
        }
        finally
        {
            if (!_disposed)
                _photoLoading.Value = false;
        }
    }

    [Body]
    View body()
    {
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);
        var content = BaristaEdgeFrame.Build(
            HeaderView(safeArea),
            BodyView(),
            BottomActions(),
            "profile_detail_frame");

        return new Grid
        {
            content,
            DeleteDialog(),
            PhotoSourceDialog(),
        }
        .AutomationId("profile_form_page");
    }

    View HeaderView(Thickness safeArea)
    {
        var label = IsEdit ? "EDIT PROFILE" : "NEW PROFILE";
        var title = IsEdit ? (_name.Value.Length == 0 ? "Loading…" : _name.Value) : "Add profile";
        var titleSize = title.Length switch
        {
            <= 12 => 28,
            <= 20 => 22,
            <= 28 => 18,
            _ => 16,
        };

        return new Grid(columns: new object[] { "*" }, rows: new object[] { "Auto", "*" })
        {
            new Text(label).SectionLabel().Cell(row: 0),
            new Text(title)
                .FontFamily("ManropeSemibold")
                .FontSize(titleSize)
                .Color(CoffeeTheme.TextPrimary)
                .MaxLines(2)
                .Alignment(Comet.Alignment.BottomLeading)
                .Cell(row: 1),
        }
        .Padding(BaristaSafeAreaLayout.HeaderPadding(safeArea))
        .MinimumHeight(BaristaSafeAreaLayout.HeaderMinimumHeight(safeArea))
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId("profile_header");
    }

    View BodyView()
    {
        if (_isLoading.Value)
        {
            return new ActivityIndicator()
                .Center()
                .Background(CoffeeTheme.SurfaceColor)
                .AutomationId("profile_loading");
        }

        var form = new VStack(spacing: CoffeeSpacing.Divider)
        {
            NameTile(),
            PhotoTile(),
            ContextTile(),
        };
        if (_error.Value is { } error)
            form.Add(ErrorTile(error));
        form.Add(new Grid().Frame(height: CoffeeSpacing.L).Background(CoffeeTheme.SurfaceColor));

        return new ScrollView { form }
            .Background(CoffeeTheme.SurfaceColor)
            .AutomationId("profile_form_scroll");
    }

    View NameTile()
        => new Grid(columns: new object[] { "*" }, rows: new object[] { "Auto", "*" })
        {
            new Text("NAME").SectionLabel().Cell(row: 0),
            SignalExtensions.TextField(_name, "Profile name")
                .Borderless()
                .IsEnabled(!IsOperationActive)
                .FontFamily("ManropeSemibold")
                .FontSize(22)
                .Color(CoffeeTheme.TextPrimary)
                .AutomationId("ProfileNameEntry")
                .Cell(row: 1),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 14))
        .MinimumHeight(100)
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId("profile_name_tile");

    View PhotoTile()
    {
        var stack = new VStack(spacing: 10)
        {
            new Text("PHOTO").SectionLabel(),
        };

        if (IsEdit)
        {
            stack.Add(new ProfileImagePicker(
                _avatarPath.Value,
                120,
                _photoLoading,
                OpenPhotoSourceDialog,
                () => _ = RemovePhotoAsync(),
                _photoStatus.Value,
                isEnabled: !IsOperationActive));
            if (_stagedAvatar?.HasBytes == true)
            {
                stack.Add(new Text("Photo will be saved when you save the profile")
                    .FontSize(13)
                    .SecondaryText()
                    .AutomationId("profile_staged_photo_hint"));
            }
        }
        else if (_stagedAvatar?.HasBytes == true && !string.IsNullOrWhiteSpace(_avatarPath.Value))
        {
            // Staged preview from captured photo
            stack.Add(new ProfileAvatar(_avatarPath.Value, 120, "StagedProfileAvatar"));
            stack.Add(new Text("Photo will be saved with the profile")
                .FontSize(13).SecondaryText().AutomationId("profile_staged_photo_hint"));
        }
        else
        {
            stack.Add(new Text("Save the profile first to add a photo")
                .FontSize(14)
                .SecondaryText()
                .AutomationId("profile_photo_hint"));
        }

        return stack
            .Padding(new Thickness(CoffeeSpacing.M, 14))
            .Background(CoffeeTheme.SurfaceColor)
            .AutomationId("profile_photo_tile");
    }

    View ContextTile()
    {
        var charCount = _context.Value.Length;
        return new VStack(spacing: CoffeeSpacing.S)
        {
            new Text("ABOUT THIS PERSON").SectionLabel(),
            new Text("Preferences, history, notes the assistant can read and learn from.")
                .SecondaryText(),
            SignalExtensions.TextEditor(_context)
                .Placeholder("e.g. Likes single-origin pour overs in the morning. Sensitive to bitter notes.")
                .IsEnabled(!IsOperationActive)
                .SourceInputChrome()
                .SourceText()
                .Frame(height: 140)
                .AutomationId("ProfileContextEditor"),
            new Text($"{charCount}/2000")
                .FontSize(11)
                .Color(charCount > 2000 ? CoffeeTheme.Error : CoffeeTheme.TextSecondary)
                .Trailing()
                .AutomationId("profile_context_count"),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 14))
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId("profile_context_tile");
    }

    View ErrorTile(string message)
        => new Grid(columns: new object[] { "*" }, rows: new object[] { "Auto", "Auto" })
        {
            new Text("ERROR").SectionLabel().Color(CoffeeTheme.SurfaceColor.WithAlpha(.8f)).Cell(row: 0),
            new Text(message)
                .FontFamily("ManropeSemibold")
                .FontSize(16)
                .Color(CoffeeTheme.SurfaceColor)
                .Cell(row: 1),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 12))
        .MinimumHeight(60)
        .Background(CoffeeTheme.Error)
        .AutomationId("profile_error");

    View BottomActions()
        => IsEdit
            ? new ProfileFixedActionRow(
                "profile_form_actions",
                new("profile_cancel", "CANCEL", Cancel, enabled: !IsOperationActive),
                new("profile_delete", "DELETE", OpenDeleteDialog, danger: true, enabled: !IsOperationActive),
                new("profile_save", _isSaving.Value ? "SAVING…" : "SAVE", () => _ = SaveAsync(), inverted: true, enabled: !IsOperationActive))
            : new ProfileFixedActionRow(
                "profile_form_actions",
                new("profile_cancel", "CANCEL", Cancel, enabled: !IsOperationActive),
                new("profile_save", _isSaving.Value ? "SAVING…" : "ADD", () => _ = SaveAsync(), inverted: true, enabled: !IsOperationActive));

    void OpenDeleteDialog()
    {
        if (_disposed || IsOperationActive || !IsEdit)
            return;

        _deleteDialogOpen.Value = true;
    }

    static string OwnerRefreshFailureSuffix(ProfileOwnerRefreshResult? refresh) =>
        refresh?.Status == ProfileOwnerRefreshStatus.Failed
            ? $" The profile list also could not be refreshed: {refresh.ErrorMessage}"
            : string.Empty;

    View DeleteDialog()
        => new ProfileConfirmationOverlay(
            _deleteDialogOpen,
            "Delete Profile?",
            $"Are you sure you want to delete '{_name.Value}'? This action cannot be undone.",
            "DELETE",
            () => _ = DeleteAsync(),
            "CANCEL",
            () =>
            {
                if (!IsOperationActive)
                    _deleteDialogOpen.Value = false;
            },
            "profile_delete_dialog",
            danger: true,
            isEnabled: !IsOperationActive);

    View PhotoSourceDialog()
        => new ProfilePhotoSourceOverlay(
            _photoSourceDialogOpen,
            () => _ = PickPhotoAsync(PhotoSource.Camera),
            () => _ = PickPhotoAsync(PhotoSource.Gallery),
            isEnabled: !IsOperationActive);

    sealed class ActionCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
