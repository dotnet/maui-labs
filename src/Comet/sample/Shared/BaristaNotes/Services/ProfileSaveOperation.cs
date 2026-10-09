#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

internal enum ProfileSaveOperationStatus
{
    Success,
    ReadbackFailed,
    PhotoFailed,
    Failed,
    Abandoned,
    CompensationFailed,
}

internal enum ProfileSaveTransactionState
{
    NotStarted,
    ProfileCommitted,
    PhotoCommitted,
    Completed,
    Compensated,
    CompensationFailed,
}

internal sealed record ProfileSaveOperationResult(
    ProfileSaveOperationStatus Status,
    ProfileSaveTransactionState TransactionState,
    int ProfileId,
    string? AvatarPath = null,
    string? ErrorMessage = null,
    PhotoByteOwnership? RetryAvatar = null)
{
    public bool IsCommitted => TransactionState is
        ProfileSaveTransactionState.ProfileCommitted or
        ProfileSaveTransactionState.PhotoCommitted or
        ProfileSaveTransactionState.Completed or
        ProfileSaveTransactionState.CompensationFailed;
}

internal sealed class ProfileSaveOperation(IUserProfileService service)
{
    readonly IUserProfileService _service =
        service ?? throw new ArgumentNullException(nameof(service));

    public async Task<ProfileSaveOperationResult> ExecuteAsync(
        int? profileId,
        string name,
        string? context,
        PhotoByteOwnership? avatar,
        CancellationToken presentationCancellation)
    {
        var effectiveProfileId = profileId.GetValueOrDefault();
        var createdProfile = false;
        var returnAvatarForRetry = false;
        var transactionState = ProfileSaveTransactionState.NotStarted;

        try
        {
            try
            {
                if (profileId is > 0)
                {
                    await _service.UpdateProfileAsync(profileId.Value, new UpdateUserProfileDto
                    {
                        Name = name,
                        Context = context ?? string.Empty,
                    });
                    effectiveProfileId = profileId.Value;
                }
                else
                {
                    var created = await _service.CreateProfileAsync(new CreateUserProfileDto
                    {
                        Name = name,
                        Context = context,
                    });
                    effectiveProfileId = created.Id;
                    createdProfile = true;
                }
            }
            catch (Exception ex)
            {
                return new(
                    presentationCancellation.IsCancellationRequested
                        ? ProfileSaveOperationStatus.Abandoned
                        : ProfileSaveOperationStatus.Failed,
                    ProfileSaveTransactionState.NotStarted,
                    effectiveProfileId,
                    ErrorMessage: presentationCancellation.IsCancellationRequested
                        ? null
                        : ex.Message);
            }

            transactionState = ProfileSaveTransactionState.ProfileCommitted;

            if (avatar?.HasBytes == true)
            {
                ProfileImageUpdateResult imageResult;
                try
                {
                    using var avatarStream = avatar.OpenRead();
                    imageResult = await _service.UpdateProfileImageAsync(
                        effectiveProfileId,
                        avatarStream);
                }
                catch (Exception ex)
                {
                    return await HandlePhotoFailureAsync(ex.Message);
                }

                if (!imageResult.Success)
                    return await HandlePhotoFailureAsync(
                        imageResult.ErrorMessage ?? "unknown error");

                transactionState = ProfileSaveTransactionState.PhotoCommitted;
            }

            string? avatarPath = null;
            if (avatar is not null)
            {
                try
                {
                    avatarPath = await _service.GetProfileImagePathAsync(effectiveProfileId);
                }
                catch (Exception ex)
                {
                    return new(
                        ProfileSaveOperationStatus.ReadbackFailed,
                        transactionState,
                        effectiveProfileId,
                        ErrorMessage: ex.Message);
                }
            }

            return new(
                ProfileSaveOperationStatus.Success,
                ProfileSaveTransactionState.Completed,
                effectiveProfileId,
                avatarPath);

            async Task<ProfileSaveOperationResult> HandlePhotoFailureAsync(
                string errorMessage)
            {
                if (presentationCancellation.IsCancellationRequested && createdProfile)
                {
                    var compensationError = await CompensateCreatedProfileAsync(
                        effectiveProfileId);
                    if (compensationError is null)
                    {
                        transactionState = ProfileSaveTransactionState.Compensated;
                        return new(
                            ProfileSaveOperationStatus.Abandoned,
                            transactionState,
                            effectiveProfileId);
                    }

                    transactionState = ProfileSaveTransactionState.CompensationFailed;
                    returnAvatarForRetry = true;
                    return new(
                        ProfileSaveOperationStatus.CompensationFailed,
                        transactionState,
                        effectiveProfileId,
                        ErrorMessage:
                            $"The photo failed: {errorMessage}. " +
                            $"The created profile could not be rolled back: {compensationError.Message}",
                        RetryAvatar: avatar);
                }

                returnAvatarForRetry = true;
                return new(
                    ProfileSaveOperationStatus.PhotoFailed,
                    transactionState,
                    effectiveProfileId,
                    ErrorMessage: errorMessage,
                    RetryAvatar: avatar);
            }
        }
        finally
        {
            if (!returnAvatarForRetry)
                avatar?.Dispose();
        }
    }

    async Task<Exception?> CompensateCreatedProfileAsync(int profileId)
    {
        try
        {
            await _service.DeleteProfileAsync(profileId);
            return null;
        }
        catch (Exception ex)
        {
            Trace.TraceError(
                "Failed to compensate an abandoned profile create ({0}).",
                ex.GetType().Name);
            return ex;
        }
    }
}
