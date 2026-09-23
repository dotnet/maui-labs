namespace AIExtensions.Sample.ChatPlayground.Features.Chat;

public enum TranscriptEntryKind
{
    User,
    System,
    Assistant,
    Reasoning,
    Tool,
    Image,
}

/// <summary>A visible transcript change, distinct from an IChatClient protocol message or update.</summary>
public abstract record TranscriptChange
{
    public sealed record Cleared : TranscriptChange;

    /// <summary>Adds a visible entry, not a protocol ChatMessage.</summary>
    public sealed record EntryAdded(
        long EntryId,
        TranscriptEntryKind EntryKind,
        string Label,
        string Text,
        string? Details = null,
        byte[]? ImageBytes = null,
        bool IsStreaming = false) : TranscriptChange;

    public sealed record EntryTextChanged(long EntryId, string Text) : TranscriptChange;
    public sealed record ToolCallResolved(long EntryId, string Label, string Details) : TranscriptChange;
    public sealed record EntryStreamingStopped(long EntryId) : TranscriptChange;
    public sealed record EntryRemoved(long EntryId) : TranscriptChange;
}
