#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using Xunit;

namespace Comet.Tests.BaristaNotes.Domain;

public sealed class BaristaNotesProfileSaveOperationTests
{
    [Fact]
    public void ProfileDetail_SaveLifecycle_DisablesDismissalAndHasSingleCompletionPath()
    {
        var root = FindCometRoot();
        if (root is null)
            return;

        var page = File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/Pages/ProfileDetailPage.cs"));
        var components = File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/Components/ProfileComponents.cs"));
        Assert.Contains("this.BackButtonBehavior(new BackButtonBehavior", page);
        Assert.Contains("void HandleSystemBack()", page);
        Assert.Contains("if (IsOperationActive)", page);
        Assert.Contains("if (_disposed || IsOperationActive)", page);
        Assert.Contains("danger: true, enabled: !IsOperationActive", page);
        Assert.Contains("isEnabled: !IsOperationActive", page);
        Assert.Contains("new ProfileOwnerRefreshOperation(_onMutated)", page);
        Assert.Contains(".IsEnabled(action.enabled)", components);
        Assert.Contains(".InputTransparent(!action.enabled)", components);
        Assert.Contains(".IsEnabled(_isEnabled)", components);
    }

    [Fact]
    public async Task DelayedCreate_CancelledPresentation_FinishesCommittedPhoto()
    {
        var service = new ControllableProfileService();
        var originalOwnership = new PhotoByteOwnership([1, 2, 3, 4]);
        var operationOwnership = originalOwnership.Transfer();
        originalOwnership.Dispose();
        using var presentation = new CancellationTokenSource();
        var operation = new ProfileSaveOperation(service);

        var saving = operation.ExecuteAsync(
            null,
            "David",
            "Likes espresso",
            operationOwnership,
            presentation.Token);
        await service.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        presentation.Cancel();
        service.CreateCompletion.TrySetResult(Profile(42));

        var result = await saving.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ProfileSaveOperationStatus.Success, result.Status);
        Assert.Equal(ProfileSaveTransactionState.Completed, result.TransactionState);
        Assert.True(result.IsCommitted);
        Assert.Equal(1, service.CreateCalls);
        Assert.Equal(1, service.ImageCalls);
        Assert.Equal([1, 2, 3, 4], service.UploadedBytes);
        Assert.Equal(0, service.DeleteCalls);
        Assert.False(operationOwnership!.HasBytes);
        Assert.Throws<ObjectDisposedException>(() => originalOwnership.OpenRead());
    }

    [Fact]
    public async Task DelayedUpdate_CancelledPresentation_FinishesCommittedPhoto()
    {
        var service = new ControllableProfileService
        {
            DelayUpdate = true,
        };
        var ownership = new PhotoByteOwnership([9, 8, 7]);
        using var presentation = new CancellationTokenSource();
        var operation = new ProfileSaveOperation(service);

        var saving = operation.ExecuteAsync(
            7,
            "Updated",
            null,
            ownership,
            presentation.Token);
        await service.UpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        presentation.Cancel();
        service.UpdateCompletion.TrySetResult(Profile(7));

        var result = await saving.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ProfileSaveOperationStatus.Success, result.Status);
        Assert.Equal(ProfileSaveTransactionState.Completed, result.TransactionState);
        Assert.True(result.IsCommitted);
        Assert.Equal(1, service.UpdateCalls);
        Assert.Equal(1, service.ImageCalls);
        Assert.Equal(0, service.DeleteCalls);
        Assert.False(ownership.HasBytes);
    }

    [Fact]
    public async Task Create_PhotoFailureWhileDisposed_CompensatesCreatedProfile()
    {
        var service = new ControllableProfileService
        {
            ImageResults = new Queue<ProfileImageUpdateResult>(
                [ProfileImageUpdateResult.FailureResult("upload failed")]),
        };
        service.CreateCompletion.TrySetResult(Profile(51));
        using var presentation = new CancellationTokenSource();
        presentation.Cancel();
        var ownership = new PhotoByteOwnership([5, 1]);

        var result = await new ProfileSaveOperation(service).ExecuteAsync(
            null,
            "Dismissed",
            null,
            ownership,
            presentation.Token);

        Assert.Equal(ProfileSaveOperationStatus.Abandoned, result.Status);
        Assert.Equal(ProfileSaveTransactionState.Compensated, result.TransactionState);
        Assert.False(result.IsCommitted);
        Assert.Equal([51], service.DeletedProfileIds);
        Assert.False(ownership.HasBytes);
    }

    [Fact]
    public async Task Create_PhotoFailureAndRollbackFailure_ReturnsCommittedCompensationFailure()
    {
        var service = new ControllableProfileService
        {
            DeleteException = new InvalidOperationException("rollback unavailable"),
            ImageResults = new Queue<ProfileImageUpdateResult>(
                [ProfileImageUpdateResult.FailureResult("upload failed")]),
        };
        service.CreateCompletion.TrySetResult(Profile(52));
        using var presentation = new CancellationTokenSource();
        presentation.Cancel();
        var ownership = new PhotoByteOwnership([5, 2]);

        var result = await new ProfileSaveOperation(service).ExecuteAsync(
            null,
            "Visible profile",
            null,
            ownership,
            presentation.Token);

        Assert.Equal(ProfileSaveOperationStatus.CompensationFailed, result.Status);
        Assert.Equal(ProfileSaveTransactionState.CompensationFailed, result.TransactionState);
        Assert.True(result.IsCommitted);
        Assert.Equal(52, result.ProfileId);
        Assert.Equal([52], service.DeletedProfileIds);
        Assert.Contains("rollback unavailable", result.ErrorMessage);
        Assert.True(result.RetryAvatar?.HasBytes);
        result.RetryAvatar?.Dispose();
        Assert.False(ownership.HasBytes);
    }

    [Fact]
    public async Task Create_PhotoFailure_ReturnsOwnershipAndRetryUpdatesSameProfile()
    {
        var service = new ControllableProfileService
        {
            ImageResults = new Queue<ProfileImageUpdateResult>(
            [
                ProfileImageUpdateResult.FailureResult("transient"),
                ProfileImageUpdateResult.SuccessResult("avatar.jpg"),
            ]),
        };
        service.CreateCompletion.TrySetResult(Profile(73));
        var ownership = new PhotoByteOwnership([7, 3]);
        var operation = new ProfileSaveOperation(service);

        var first = await operation.ExecuteAsync(
            null,
            "Retry",
            null,
            ownership,
            CancellationToken.None);

        Assert.Equal(ProfileSaveOperationStatus.PhotoFailed, first.Status);
        Assert.Equal(ProfileSaveTransactionState.ProfileCommitted, first.TransactionState);
        Assert.True(first.IsCommitted);
        Assert.Equal(73, first.ProfileId);
        Assert.True(first.RetryAvatar?.HasBytes);

        var retry = await operation.ExecuteAsync(
            first.ProfileId,
            "Retry",
            null,
            first.RetryAvatar,
            CancellationToken.None);

        Assert.Equal(ProfileSaveOperationStatus.Success, retry.Status);
        Assert.Equal(ProfileSaveTransactionState.Completed, retry.TransactionState);
        Assert.Equal("avatar.jpg", retry.AvatarPath);
        Assert.Equal(1, service.CreateCalls);
        Assert.Equal(1, service.UpdateCalls);
        Assert.Equal(2, service.ImageCalls);
        Assert.False(ownership.HasBytes);
    }

    [Fact]
    public async Task Create_PhotoCommitThenReadbackFailure_DoesNotDeleteProfile()
    {
        var service = new ControllableProfileService
        {
            DelayReadback = true,
        };
        service.CreateCompletion.TrySetResult(Profile(81));
        using var presentation = new CancellationTokenSource();
        var ownership = new PhotoByteOwnership([8, 1]);

        var saving = new ProfileSaveOperation(service).ExecuteAsync(
            null,
            "Committed",
            null,
            ownership,
            presentation.Token);
        await service.ReadbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        presentation.Cancel();
        service.ReadbackCompletion.TrySetException(
            new IOException("path unavailable"));

        var result = await saving.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ProfileSaveOperationStatus.ReadbackFailed, result.Status);
        Assert.Equal(ProfileSaveTransactionState.PhotoCommitted, result.TransactionState);
        Assert.True(result.IsCommitted);
        Assert.Equal(0, service.DeleteCalls);
        Assert.False(ownership.HasBytes);
    }

    [Fact]
    public async Task PhotoUpdate_DelayedCommitThenReadbackFailure_RemainsCommitted()
    {
        var service = new ControllableProfileService
        {
            DelayImage = true,
            ReadbackException = new IOException("path unavailable"),
        };
        using var image = new MemoryStream([4, 2]);
        var operation = new ProfilePhotoMutationOperation(service);

        var updating = operation.UpdateAsync(42, image);
        await service.ImageStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.ImageCompletion.TrySetResult(
            ProfileImageUpdateResult.SuccessResult("avatar.jpg"));

        var result = await updating.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ProfilePhotoMutationStatus.ReadbackFailed, result.Status);
        Assert.Equal(
            ProfilePhotoTransactionState.MutationCommitted,
            result.TransactionState);
        Assert.True(result.IsCommitted);
        Assert.Equal(0, service.DeleteCalls);
    }

    [Fact]
    public async Task PhotoRemove_DelayedMutation_CompletesAsAtomicCommit()
    {
        var service = new ControllableProfileService
        {
            DelayRemove = true,
        };
        var operation = new ProfilePhotoMutationOperation(service);

        var removing = operation.RemoveAsync(42);
        await service.RemoveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.RemoveCompletion.TrySetResult(true);

        var result = await removing.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ProfilePhotoMutationStatus.Success, result.Status);
        Assert.Equal(ProfilePhotoTransactionState.Completed, result.TransactionState);
        Assert.True(result.IsCommitted);
        Assert.Equal("avatar.jpg", result.AvatarPath);
        Assert.Equal(1, service.RemoveCalls);
    }

    [Fact]
    public async Task OwnerRefresh_ConcurrentCompletionAttempts_InvokeOwnerExactlyOnce()
    {
        var calls = 0;
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = new ProfileOwnerRefreshOperation(async (_, _) =>
        {
            Interlocked.Increment(ref calls);
            await release.Task;
        });

        var first = refresh.ExecuteAsync("saved");
        var second = refresh.ExecuteAsync("saved");
        release.TrySetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Contains(results, result => result.Status == ProfileOwnerRefreshStatus.Success);
        Assert.Contains(
            results,
            result => result.Status == ProfileOwnerRefreshStatus.AlreadyCompleted);
    }

    [Fact]
    public async Task ConcurrentPresentationCancellation_DoesNotDuplicateMutation()
    {
        var service = new ControllableProfileService();
        using var presentation = new CancellationTokenSource();
        var operation = new ProfileSaveOperation(service);

        var saving = operation.ExecuteAsync(
            null,
            "Only once",
            null,
            null,
            presentation.Token);
        await service.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        presentation.Cancel();
        presentation.Cancel();
        service.CreateCompletion.TrySetResult(Profile(91));

        var result = await saving.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ProfileSaveOperationStatus.Success, result.Status);
        Assert.Equal(ProfileSaveTransactionState.Completed, result.TransactionState);
        Assert.True(result.IsCommitted);
        Assert.Equal(1, service.CreateCalls);
        Assert.Equal(0, service.UpdateCalls);
        Assert.Equal(0, service.ImageCalls);
    }

    static UserProfileDto Profile(int id) => new()
    {
        Id = id,
        Name = $"Profile {id}",
    };

    static string? FindCometRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && directory is not null; i++)
        {
            if (File.Exists(System.IO.Path.Combine(directory, "global.json")) &&
                Directory.Exists(System.IO.Path.Combine(directory, "sample")))
            {
                return directory;
            }
            directory = System.IO.Path.GetDirectoryName(directory);
        }
        return null;
    }

    sealed class ControllableProfileService : IUserProfileService
    {
        public TaskCompletionSource<UserProfileDto> CreateCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<UserProfileDto> UpdateCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CreateStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource UpdateStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Queue<ProfileImageUpdateResult> ImageResults { get; init; } =
            new([ProfileImageUpdateResult.SuccessResult("avatar.jpg")]);
        public TaskCompletionSource<ProfileImageUpdateResult> ImageCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ImageStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string?> ReadbackCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadbackStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> RemoveCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RemoveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<int> DeletedProfileIds { get; } = [];
        public byte[] UploadedBytes { get; private set; } = [];
        public bool DelayUpdate { get; init; }
        public bool DelayImage { get; init; }
        public bool DelayReadback { get; init; }
        public bool DelayRemove { get; init; }
        public Exception? DeleteException { get; init; }
        public Exception? ReadbackException { get; init; }
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int ImageCalls { get; private set; }
        public int RemoveCalls { get; private set; }
        public int DeleteCalls => DeletedProfileIds.Count;

        public Task<List<UserProfileDto>> GetAllProfilesAsync() =>
            Task.FromResult(new List<UserProfileDto>());

        public Task<UserProfileDto?> GetProfileByIdAsync(int id) =>
            Task.FromResult<UserProfileDto?>(null);

        public Task<UserProfileDto> CreateProfileAsync(CreateUserProfileDto dto)
        {
            CreateCalls++;
            CreateStarted.TrySetResult();
            return CreateCompletion.Task;
        }

        public Task<UserProfileDto> UpdateProfileAsync(int id, UpdateUserProfileDto dto)
        {
            UpdateCalls++;
            UpdateStarted.TrySetResult();
            return DelayUpdate
                ? UpdateCompletion.Task
                : Task.FromResult(Profile(id));
        }

        public Task DeleteProfileAsync(int id)
        {
            DeletedProfileIds.Add(id);
            if (DeleteException is not null)
                return Task.FromException(DeleteException);
            return Task.CompletedTask;
        }

        public async Task<ProfileImageUpdateResult> UpdateProfileImageAsync(
            int profileId,
            Stream imageStream)
        {
            ImageCalls++;
            using var copy = new MemoryStream();
            await imageStream.CopyToAsync(copy);
            UploadedBytes = copy.ToArray();
            ImageStarted.TrySetResult();
            if (DelayImage)
                return await ImageCompletion.Task;
            return ImageResults.Dequeue();
        }

        public Task<bool> RemoveProfileImageAsync(int profileId)
        {
            RemoveCalls++;
            RemoveStarted.TrySetResult();
            return DelayRemove
                ? RemoveCompletion.Task
                : Task.FromResult(false);
        }

        public Task<string?> GetProfileImagePathAsync(int profileId)
        {
            ReadbackStarted.TrySetResult();
            if (DelayReadback)
                return ReadbackCompletion.Task;
            return ReadbackException is null
                ? Task.FromResult<string?>("avatar.jpg")
                : Task.FromException<string?>(ReadbackException);
        }
    }
}
