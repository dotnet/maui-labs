namespace AIExtensions.Sample.ChatPlayground.Features.Search;

public enum ChatSearchDataLocation
{
    OnDevice,
    Remote,
}

/// <summary>Identifies one search method and the exact vector space used for its index.</summary>
public sealed record ChatSearchDescriptor(
    string Id, string DisplayName, string Description, string IndexIdentity,
    ChatSearchDataLocation DataLocation)
{
    public const string ContainsId = "contains";

    public static ChatSearchDescriptor Contains { get; } = new(
        ContainsId, "Contains", "Match words in chat titles and messages on this device.",
        "text-v1", ChatSearchDataLocation.OnDevice);
}
