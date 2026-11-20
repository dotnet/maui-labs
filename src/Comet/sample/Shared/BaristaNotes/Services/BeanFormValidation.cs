#nullable enable

namespace CometBaristaNotes.Services;

public static class BeanFormValidation
{
    public const string NameRequired = "Bean name is required";

    public static string? Validate(
        string name,
        string roaster,
        string origin,
        string notes,
        string roasterUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
            return NameRequired;
        if (name.Trim().Length > 100)
            return "Bean name must be 100 characters or less";
        if (roaster.Trim().Length > 100)
            return "Roaster must be 100 characters or less";
        if (origin.Trim().Length > 100)
            return "Origin must be 100 characters or less";
        if (notes.Trim().Length > 500)
            return "Notes must be 500 characters or less";
        if (roasterUrl.Trim().Length > 500)
            return "Roaster URL must be 500 characters or less";
        return null;
    }
}
