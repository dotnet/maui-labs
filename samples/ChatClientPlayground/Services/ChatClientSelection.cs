using ChatClientPlayground.Models;
using Microsoft.Extensions.AI;

namespace ChatClientPlayground.Services;

/// <summary>Combines a resolved chat client with the metadata exposed by its wrapper.</summary>
public sealed record ChatClientSelection(ChatClientKind Kind, IChatClient? Client, ChatClientDescriptor Descriptor)
{
    /// <summary>Gets whether a real client was resolved for this slot.</summary>
    public bool IsAvailable => Client is not null;
}
