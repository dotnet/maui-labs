using Microsoft.Maui.Essentials.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Metadata exposed by a configured chat-client slot. <see cref="SupportsUIActions"/> is false for a client that runs
/// tools itself instead of handing the calls back to the app, so it can't take the chat's UI actions.
/// </summary>
public sealed record ChatClientDescriptor(
    string Id,
    string DisplayName,
    string Description,
    bool SupportsImageInput = false,
    bool SupportsReasoningSummary = false,
    bool SupportsImageGeneration = false,
    bool SupportsToolCalling = false,
    bool SupportsStructuredOutput = true,
    Func<string?, CancellationToken, Task<ChatModelStatus>>? GetModelStatusAsync = null,
    Func<string?, IProgress<ModelPreparationProgress>, CancellationToken, Task>? PrepareModelAsync = null,
    string PreparationActionLabel = "Prepare",
    IReadOnlyList<ChatModelOption>? Models = null,
    bool SupportsRequiredToolMode = true,
    bool SupportsUIActions = true);

public sealed record ChatModelOption(string Id, string DisplayName)
{
    public string AutomationId => $"ChatModel-{Id}";
}

/// <summary>Optional byte counts reported during model preparation.</summary>
public sealed record ModelPreparationProgress(long? BytesDownloaded = null, long? TotalBytes = null);
