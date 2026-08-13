using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>A named multimodal attachment ready to be sent as <see cref="DataContent"/>.</summary>
public sealed class ChatAttachment : Microsoft.Maui.Chat.Controls.ChatAttachment
{
    public ChatAttachment(string fileName, DataContent content)
        : base(
            fileName,
            content?.MediaType ?? throw new ArgumentNullException(nameof(content)),
            content.Data)
    {
        Content = content;
        Content.Name ??= fileName;
    }

    public DataContent Content { get; }
}
