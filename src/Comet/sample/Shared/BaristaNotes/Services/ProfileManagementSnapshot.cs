#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public sealed record ProfileManagementSnapshot(
    IReadOnlyList<UserProfileDto> Profiles,
    IReadOnlyDictionary<int, string?> AvatarPaths,
    bool IsLoading,
    string? ErrorMessage)
{
    public static ProfileManagementSnapshot Empty { get; } = new(
        Array.Empty<UserProfileDto>(),
        new Dictionary<int, string?>(),
        IsLoading: true,
        ErrorMessage: null);
}

public sealed class ProfileManagementSnapshotLoader
{
    readonly IUserProfileService _service;

    public ProfileManagementSnapshotLoader(IUserProfileService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public async Task<ProfileManagementSnapshot> LoadAsync()
    {
        var profiles = await _service.GetAllProfilesAsync();
        var resolvedAvatars = await Task.WhenAll(profiles.Select(async profile =>
            new KeyValuePair<int, string?>(
                profile.Id,
                await _service.GetProfileImagePathAsync(profile.Id))));

        return new ProfileManagementSnapshot(
            profiles.ToArray(),
            resolvedAvatars.ToDictionary(item => item.Key, item => item.Value),
            IsLoading: false,
            ErrorMessage: null);
    }
}
