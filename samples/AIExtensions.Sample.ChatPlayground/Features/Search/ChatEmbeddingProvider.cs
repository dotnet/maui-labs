using Microsoft.Extensions.AI;

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

/// <summary>Registers an embedding generator without creating it until its search method is selected.</summary>
public sealed class ChatEmbeddingProvider
{
    public ChatEmbeddingProvider(
        ChatSearchDescriptor descriptor,
        Func<IEmbeddingGenerator<string, Embedding<float>>> createGenerator)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(createGenerator);
        if (string.IsNullOrWhiteSpace(descriptor.Id) || descriptor.Id == ChatSearchDescriptor.ContainsId ||
            string.IsNullOrWhiteSpace(descriptor.DisplayName) ||
            string.IsNullOrWhiteSpace(descriptor.Description) ||
            string.IsNullOrWhiteSpace(descriptor.IndexIdentity) ||
            descriptor.IndexIdentity == ChatSearchDescriptor.Contains.IndexIdentity ||
            !Enum.IsDefined(descriptor.DataLocation))
        {
            throw new ArgumentException("An embedding provider needs a unique ID, label, description, data location, and model identity.", nameof(descriptor));
        }

        Descriptor = descriptor;
        CreateGenerator = createGenerator;
    }

    public ChatSearchDescriptor Descriptor { get; }
    public Func<IEmbeddingGenerator<string, Embedding<float>>> CreateGenerator { get; }
}
