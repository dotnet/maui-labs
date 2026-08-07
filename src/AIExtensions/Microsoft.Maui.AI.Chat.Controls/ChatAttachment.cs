using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>A named multimodal attachment ready to be sent as <see cref="DataContent"/>.</summary>
public sealed class ChatAttachment
{
    public ChatAttachment(string fileName, DataContent content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        FileName = fileName;
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Content.Name ??= fileName;
    }

    public string FileName { get; }

    public DataContent Content { get; }
}
