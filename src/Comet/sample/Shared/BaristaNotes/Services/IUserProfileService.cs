using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public interface IUserProfileService
{
    Task<List<UserProfileDto>> GetAllProfilesAsync();
    Task<UserProfileDto?> GetProfileByIdAsync(int id);
    Task<UserProfileDto> CreateProfileAsync(CreateUserProfileDto dto);
    Task<UserProfileDto> UpdateProfileAsync(int id, UpdateUserProfileDto dto);
    Task DeleteProfileAsync(int id);
    Task<ProfileImageUpdateResult> UpdateProfileImageAsync(int profileId, Stream imageStream);
    Task<bool> RemoveProfileImageAsync(int profileId);
    Task<string?> GetProfileImagePathAsync(int profileId);
}
