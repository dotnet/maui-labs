#nullable enable

namespace CometBaristaNotes.Services;

public static class ProfileFormValidation
{
    public const int MaximumNameLength = 50;
    public const int MaximumContextLength = 2000;

    public static string? Validate(string? name, string? context)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
            return "Profile name is required";
        if (trimmedName.Length > MaximumNameLength)
            return "Profile name must be 50 characters or less";
        if ((context?.Length ?? 0) > MaximumContextLength)
            return "About this person must be 2000 characters or less";
        return null;
    }
}
