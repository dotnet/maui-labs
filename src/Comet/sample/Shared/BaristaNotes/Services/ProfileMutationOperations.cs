#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

internal enum ProfilePhotoMutationStatus
{
    Success,
    NoChange,
    MutationFailed,
    ReadbackFailed,
}

internal enum ProfilePhotoTransactionState
{
    NotStarted,
    MutationCommitted,
    Completed,
}

internal sealed record ProfilePhotoMutationResult(
    ProfilePhotoMutationStatus Status,
    ProfilePhotoTransactionState TransactionState,
    string? AvatarPath = null,
    string? ErrorMessage = null)
{
    public bool IsCommitted =>
        TransactionState is not ProfilePhotoTransactionState.NotStarted;
}

internal sealed class ProfilePhotoMutationOperation(IUserProfileService service)
{
    readonly IUserProfileService _service =
        service ?? throw new ArgumentNullException(nameof(service));

    public async Task<ProfilePhotoMutationResult> UpdateAsync(
        int profileId,
        Stream imageStream)
    {
        ArgumentNullException.ThrowIfNull(imageStream);

        ProfileImageUpdateResult update;
        try
        {
            update = await _service.UpdateProfileImageAsync(profileId, imageStream);
        }
        catch (Exception ex)
        {
            return new(
                ProfilePhotoMutationStatus.MutationFailed,
                ProfilePhotoTransactionState.NotStarted,
                ErrorMessage: ex.Message);
        }

        if (!update.Success)
        {
            return new(
                ProfilePhotoMutationStatus.MutationFailed,
                ProfilePhotoTransactionState.NotStarted,
                ErrorMessage: update.ErrorMessage ?? "The profile photo could not be saved.");
        }

        return await ReadCommittedPathAsync(profileId);
    }

    public async Task<ProfilePhotoMutationResult> RemoveAsync(int profileId)
    {
        bool removed;
        try
        {
            removed = await _service.RemoveProfileImageAsync(profileId);
        }
        catch (Exception ex)
        {
            return new(
                ProfilePhotoMutationStatus.MutationFailed,
                ProfilePhotoTransactionState.NotStarted,
                ErrorMessage: ex.Message);
        }

        if (!removed)
        {
            return new(
                ProfilePhotoMutationStatus.NoChange,
                ProfilePhotoTransactionState.NotStarted);
        }

        return await ReadCommittedPathAsync(profileId);
    }

    async Task<ProfilePhotoMutationResult> ReadCommittedPathAsync(int profileId)
    {
        try
        {
            var avatarPath = await _service.GetProfileImagePathAsync(profileId);
            return new(
                ProfilePhotoMutationStatus.Success,
                ProfilePhotoTransactionState.Completed,
                avatarPath);
        }
        catch (Exception ex)
        {
            return new(
                ProfilePhotoMutationStatus.ReadbackFailed,
                ProfilePhotoTransactionState.MutationCommitted,
                ErrorMessage: ex.Message);
        }
    }
}

internal enum ProfileOwnerRefreshStatus
{
    NotConfigured,
    Success,
    Failed,
    AlreadyCompleted,
}

internal sealed record ProfileOwnerRefreshResult(
    ProfileOwnerRefreshStatus Status,
    string? ErrorMessage = null);

internal sealed class ProfileOwnerRefreshOperation(
    Func<string?, CancellationToken, Task>? refreshOwner)
{
    readonly Func<string?, CancellationToken, Task>? _refreshOwner = refreshOwner;
    int _completed;

    public async Task<ProfileOwnerRefreshResult> ExecuteAsync(string? successMessage)
    {
        if (_refreshOwner is null)
            return new(ProfileOwnerRefreshStatus.NotConfigured);
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return new(ProfileOwnerRefreshStatus.AlreadyCompleted);

        try
        {
            await _refreshOwner(successMessage, CancellationToken.None);
            return new(ProfileOwnerRefreshStatus.Success);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "A committed profile mutation could not refresh its owner ({0}).",
                ex.GetType().Name);
            return new(ProfileOwnerRefreshStatus.Failed, ex.Message);
        }
    }
}
