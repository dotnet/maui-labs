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
    bool SupportsFullReasoning = false)
{
    public bool SupportsReasoning => SupportsReasoningSummary || SupportsFullReasoning;
    public string ReasoningLabel => SupportsFullReasoning ? "Full reasoning" : "Reasoning summary";
    public string ReasoningDescription => SupportsFullReasoning
        ? "Return the local model's actual full reasoning, not a summary. Unchecking hides the trace without disabling computation. Guided JSON may bypass reasoning."
        : "Request a visible summary; private reasoning is never displayed.";
}
