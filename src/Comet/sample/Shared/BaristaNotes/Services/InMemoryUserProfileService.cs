using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public sealed class InMemoryUserProfileService : IUserProfileService
{
    private readonly IBaristaDataStore _store;
    private readonly IDataChangeNotifier _notifier;
    private readonly IImageProcessingService _imageProcessingService;
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);
    private readonly Task _startupCleanup;

    public InMemoryUserProfileService(
        IBaristaDataStore store,
        IDataChangeNotifier notifier,
        IImageProcessingService imageProcessingService)
    {
        _store = store;
        _notifier = notifier;
        _imageProcessingService = imageProcessingService
            ?? throw new ArgumentNullException(nameof(imageProcessingService));
        _startupCleanup = RetryPendingAvatarCleanupCoreAsync();
    }

    public async Task<List<UserProfileDto>> GetAllProfilesAsync()
    {
        await PrepareOperationAsync();
        return _store.Profiles
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.Name)
            .Select(MapToDto)
            .ToList();
    }

    public async Task<UserProfileDto?> GetProfileByIdAsync(int id)
    {
        await PrepareOperationAsync();
        var p = _store.Profiles.FirstOrDefault(x => x.Id == id && !x.IsDeleted);
        return p == null ? null : MapToDto(p);
    }

    public async Task<UserProfileDto> CreateProfileAsync(CreateUserProfileDto dto)
    {
        await PrepareOperationAsync();
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Name is required");
        if (dto.Name.Length > 50)
            throw new ArgumentException("Name must be 50 characters or less");
        if (dto.AvatarPath?.Length > 500)
            throw new ArgumentException("Avatar path must be 500 characters or less");
        if (dto.Context?.Length > 2000)
            throw new ArgumentException("Context must be 2000 characters or less");

        var profile = _store.ExecuteMutation(() =>
        {
            var created = new UserProfile
            {
                Id = _store.NextProfileId(),
                Name = dto.Name.Trim(),
                AvatarPath = dto.AvatarPath,
                Context = dto.Context,
                CreatedAt = DateTime.UtcNow,
                SyncId = Guid.NewGuid(),
                LastModifiedAt = DateTime.UtcNow,
            };
            _store.Profiles.Add(created);
            _store.SaveChanges();
            return created;
        });
        NotifyProfileChanged(DataChangeType.ProfileCreated, profile);
        return MapToDto(profile);
    }

    public async Task<UserProfileDto> UpdateProfileAsync(int id, UpdateUserProfileDto dto)
    {
        await PrepareOperationAsync();
        if (dto.Context?.Length > 2000)
            throw new ArgumentException("Context must be 2000 characters or less");

        var mutation = _store.ExecuteMutation(() =>
        {
            var profile = _store.Profiles.FirstOrDefault(x => x.Id == id && !x.IsDeleted)
                ?? throw new KeyNotFoundException($"Profile {id} not found");
            var previousName = profile.Name;
            var previousAvatarPath = profile.AvatarPath;
            var previousContext = profile.Context;
            var previousModified = profile.LastModifiedAt;
            if (dto.Name != null) profile.Name = dto.Name.Trim();
            var avatarChanged = false;
            if (dto.AvatarPath != null)
            {
                var replacement = string.IsNullOrWhiteSpace(dto.AvatarPath) ? null : dto.AvatarPath;
                avatarChanged = !string.Equals(profile.AvatarPath, replacement, StringComparison.Ordinal);
                profile.AvatarPath = replacement;
            }
            if (dto.Context != null)
                profile.Context = dto.Context.Length == 0 ? null : dto.Context;
            profile.LastModifiedAt = DateTime.UtcNow;
            try
            {
                _store.SaveChanges();
            }
            catch
            {
                profile.Name = previousName;
                profile.AvatarPath = previousAvatarPath;
                profile.Context = previousContext;
                profile.LastModifiedAt = previousModified;
                throw;
            }
            return (Profile: profile, PreviousAvatarPath: previousAvatarPath, AvatarChanged: avatarChanged);
        });
        var p = mutation.Profile;
        if (mutation.AvatarChanged)
            await TryDeleteOwnedAvatarAsync(p.Id, mutation.PreviousAvatarPath);
        NotifyProfileChanged(DataChangeType.ProfileUpdated, p);
        return MapToDto(p);
    }

    public async Task DeleteProfileAsync(int id)
    {
        await PrepareOperationAsync();
        var p = _store.ExecuteMutation(() =>
        {
            var profile = _store.Profiles.FirstOrDefault(x => x.Id == id && !x.IsDeleted)
                ?? throw new KeyNotFoundException($"Profile {id} not found");
            var previousDeleted = profile.IsDeleted;
            var previousModified = profile.LastModifiedAt;
            profile.IsDeleted = true;
            profile.LastModifiedAt = DateTime.UtcNow;
            try
            {
                _store.SaveChanges();
            }
            catch
            {
                profile.IsDeleted = previousDeleted;
                profile.LastModifiedAt = previousModified;
                throw;
            }
            return profile;
        });
        await TryDeleteOwnedAvatarAsync(p.Id, p.AvatarPath);
    }

    public async Task<ProfileImageUpdateResult> UpdateProfileImageAsync(int profileId, Stream imageStream)
    {
        await PrepareOperationAsync();
        var profile = _store.Profiles.FirstOrDefault(item => item.Id == profileId && !item.IsDeleted);
        if (profile is null)
            return ProfileImageUpdateResult.FailureResult("Profile not found");

        string? savedFilename = null;
        var previousAvatarPath = profile.AvatarPath;
        var previousModified = profile.LastModifiedAt;
        try
        {
            using var downsampled = await _imageProcessingService.DownsampleAsync(imageStream, 400, 85);
            if (downsampled is null)
                return ProfileImageUpdateResult.FailureResult(
                    "Image format is unsupported or cannot be decoded");
            Stream working = downsampled;
            working.Position = 0;
            var validation = await _imageProcessingService.ValidateImageAsync(working);
            if (!validation.IsValid)
                return ProfileImageUpdateResult.FailureResult(validation.ErrorMessage ?? "Invalid image");

            working.Position = 0;
            var shortId = Guid.NewGuid().ToString("N")[..8];
            savedFilename = $"profile_avatar_{profileId}_{shortId}.jpg";
            await _imageProcessingService.SaveImageAsync(working, savedFilename);

            var saved = _store.ExecuteMutation(() =>
            {
                profile.AvatarPath = savedFilename;
                profile.LastModifiedAt = DateTime.UtcNow;
                try
                {
                    _store.SaveChanges();
                    return true;
                }
                catch
                {
                    profile.AvatarPath = previousAvatarPath;
                    profile.LastModifiedAt = previousModified;
                    return false;
                }
            });
            if (!saved)
            {
                await TryDeleteOwnedAvatarAsync(profileId, savedFilename);
                return ProfileImageUpdateResult.FailureResult("Failed to save image");
            }

            await TryDeleteOwnedAvatarAsync(profile.Id, previousAvatarPath);
            NotifyProfileChanged(DataChangeType.ProfileUpdated, profile);
            return ProfileImageUpdateResult.SuccessResult(savedFilename);
        }
        catch
        {
            if (savedFilename is not null)
                await TryDeleteOwnedAvatarAsync(profileId, savedFilename);
            return ProfileImageUpdateResult.FailureResult("Failed to save image");
        }
    }

    public async Task<bool> RemoveProfileImageAsync(int profileId)
    {
        await PrepareOperationAsync();
        var mutation = _store.ExecuteMutation(() =>
        {
            var profile = _store.Profiles.FirstOrDefault(item => item.Id == profileId && !item.IsDeleted);
            if (profile is null || string.IsNullOrEmpty(profile.AvatarPath))
                return (Profile: (UserProfile?)null, PreviousAvatarPath: (string?)null);
            var previousAvatarPath = profile.AvatarPath;
            var previousModified = profile.LastModifiedAt;
            profile.AvatarPath = null;
            profile.LastModifiedAt = DateTime.UtcNow;
            try
            {
                _store.SaveChanges();
            }
            catch
            {
                profile.AvatarPath = previousAvatarPath;
                profile.LastModifiedAt = previousModified;
                throw;
            }
            return (Profile: (UserProfile?)profile, PreviousAvatarPath: previousAvatarPath);
        });
        if (mutation.Profile is null)
            return false;
        var profile = mutation.Profile;
        var previousAvatarPath = mutation.PreviousAvatarPath;
        await TryDeleteOwnedAvatarAsync(profile.Id, previousAvatarPath);
        NotifyProfileChanged(DataChangeType.ProfileUpdated, profile);
        return true;
    }

    public async Task<string?> GetProfileImagePathAsync(int profileId)
    {
        await PrepareOperationAsync();
        var profile = _store.Profiles.FirstOrDefault(item => item.Id == profileId && !item.IsDeleted);
        if (profile is null
            || !IsOwnedAvatarPath(profileId, profile.AvatarPath)
            || !_imageProcessingService.ImageExists(profile.AvatarPath!))
            return null;
        return _imageProcessingService.GetImagePath(profile.AvatarPath!);
    }

    internal static bool IsOwnedAvatarPath(int profileId, string? avatarPath)
    {
        if (string.IsNullOrWhiteSpace(avatarPath)
            || Path.IsPathRooted(avatarPath)
            || !string.Equals(Path.GetFileName(avatarPath), avatarPath, StringComparison.Ordinal))
            return false;

        var legacyName = $"profile_avatar_{profileId}.jpg";
        if (string.Equals(avatarPath, legacyName, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = $"profile_avatar_{profileId}_";
        const string extension = ".jpg";
        if (!avatarPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !avatarPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            return false;

        var token = avatarPath.Substring(prefix.Length, avatarPath.Length - prefix.Length - extension.Length);
        return token.Length == 8 && token.All(Uri.IsHexDigit);
    }

    private async Task TryDeleteOwnedAvatarAsync(int profileId, string? avatarPath)
    {
        if (!IsOwnedAvatarPath(profileId, avatarPath))
            return;
        try
        {
            await _imageProcessingService.DeleteImageAsync(avatarPath!);
            RemovePendingCleanup(profileId, avatarPath!);
        }
        catch (Exception ex)
        {
            QueuePendingCleanup(profileId, avatarPath!, ex);
        }
    }

    private async Task PrepareOperationAsync()
    {
        await _startupCleanup;
        await RetryPendingAvatarCleanupCoreAsync();
    }

    private async Task RetryPendingAvatarCleanupCoreAsync()
    {
        await _cleanupGate.WaitAsync();
        try
        {
            var pending = _store.ExecuteMutation(() => _store.PendingAvatarCleanups
                .Select(item => new PendingAvatarCleanup
                {
                    ProfileId = item.ProfileId,
                    AvatarPath = item.AvatarPath,
                    Attempts = item.Attempts,
                    LastAttemptAt = item.LastAttemptAt,
                    LastError = item.LastError
                })
                .ToList());
            foreach (var item in pending)
            {
                if (!IsOwnedAvatarPath(item.ProfileId, item.AvatarPath))
                {
                    RemovePendingCleanup(item.ProfileId, item.AvatarPath);
                    continue;
                }
                try
                {
                    await _imageProcessingService.DeleteImageAsync(item.AvatarPath);
                    RemovePendingCleanup(item.ProfileId, item.AvatarPath);
                }
                catch (Exception ex)
                {
                    QueuePendingCleanup(item.ProfileId, item.AvatarPath, ex);
                }
            }
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    private void QueuePendingCleanup(int profileId, string avatarPath, Exception exception)
    {
        try
        {
            _store.ExecuteMutation(() =>
            {
                var pending = _store.PendingAvatarCleanups.FirstOrDefault(item =>
                    item.ProfileId == profileId
                    && string.Equals(item.AvatarPath, avatarPath, StringComparison.Ordinal));
                if (pending is null)
                {
                    pending = new PendingAvatarCleanup
                    {
                        ProfileId = profileId,
                        AvatarPath = avatarPath
                    };
                    _store.PendingAvatarCleanups.Add(pending);
                }
                pending.Attempts++;
                pending.LastAttemptAt = DateTime.UtcNow;
                pending.LastError = exception.Message;
                _store.SaveChanges();
            });
        }
        catch (Exception persistenceException)
        {
            Trace.TraceWarning(
                $"Avatar cleanup for '{avatarPath}' failed and its retry record could not be persisted: {persistenceException.Message}");
            return;
        }
        Trace.TraceWarning(
            $"Avatar cleanup for '{avatarPath}' failed and was queued for retry: {exception.Message}");
    }

    private void RemovePendingCleanup(int profileId, string avatarPath)
    {
        try
        {
            _store.ExecuteMutation(() =>
            {
                if (_store.PendingAvatarCleanups.RemoveAll(item =>
                        item.ProfileId == profileId
                        && string.Equals(item.AvatarPath, avatarPath, StringComparison.Ordinal)) == 0)
                    return;
                _store.SaveChanges();
            });
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Avatar cleanup retry record removal failed for '{avatarPath}': {ex.Message}");
        }
    }

    private void NotifyProfileChanged(
        DataChangeType changeType,
        UserProfile profile)
    {
        try
        {
            _notifier.NotifyDataChanged(changeType, profile);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "A committed profile mutation could not publish its data-change notification ({0}).",
                ex.GetType().Name);
        }
    }

    private static UserProfileDto MapToDto(UserProfile p) => new()
    {
        Id = p.Id, Name = p.Name, AvatarPath = p.AvatarPath, Context = p.Context, CreatedAt = p.CreatedAt,
    };
}
