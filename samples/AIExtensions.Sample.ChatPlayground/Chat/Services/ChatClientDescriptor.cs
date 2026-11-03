using Microsoft.Maui.Essentials.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Metadata exposed by a configured chat-client slot.</summary>
public sealed record ChatClientDescriptor(
    string Id,
    string DisplayName,
    string Description,
    bool SupportsImageInput = false,
    bool SupportsReasoningSummary = false,
    bool SupportsImageGeneration = false,
    bool IsReplay = false,
    bool SupportsToolCalling = false,
    bool SupportsStructuredOutput = true,
    Func<string?, CancellationToken, Task<ChatModelStatus>>? GetModelStatusAsync = null,
    Func<string?, IProgress<ModelPreparationProgress>, CancellationToken, Task>? PrepareModelAsync = null,
    string PreparationActionLabel = "Prepare",
    IReadOnlyList<ChatModelOption>? Models = null);

public sealed record ChatModelOption(string Id, string DisplayName)
{
    public string AutomationId => $"ChatModel-{Id}";
}

/// <summary>Optional byte counts reported during model preparation.</summary>
public sealed record ModelPreparationProgress(long? BytesDownloaded = null, long? TotalBytes = null);
