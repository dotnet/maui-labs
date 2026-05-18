using System.ComponentModel;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AiControlsSample;

public partial class PredictiveStatePage : ContentPage
{
    public AgentContext Session { get; }

    public PredictiveStatePage(IChatClient chatClient)
    {
        // write_document is a UIAction — the conversation pauses at AwaitingInput
        // showing a UIActionBlock inline. The UI auto-invokes it, which updates
        // the document editor and resumes the conversation.
        var writeDocument = AIFunctionFactory.Create(
            [Description("Write or replace the document content. The content appears in the document editor.")]
            ([Description("The full document text in markdown")] string content) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DocumentLabel.Text = content;
                    WritingIndicator.IsVisible = false;
                });
                return "Document updated successfully.";
            },
            "write_document",
            "Write or replace the document content. Shows it in the document editor.");

        var chatOptions = new ChatOptions
        {
            Instructions = """
                You are a document writer. When the user asks you to write or edit:
                1. Call write_document with the full document text.
                2. The content will appear in the document editor on the left.
                3. After writing, briefly describe what you wrote.

                Write in markdown format. Be creative and detailed.
                When editing, preserve the overall structure but improve the requested parts.
                """,
        };

        var agent = new UIAgent(chatClient, options =>
        {
            options.ChatOptions = chatOptions;
            options.RegisterUIAction(writeDocument);
        });
        Session = new AgentContext(agent);

        InitializeComponent();

        // Show writing indicator when streaming starts
        Session.RegisterOnStatusChanged(status =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                WritingIndicator.IsVisible = status == ConversationStatus.Streaming;
            });
        });
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Session.Clear();
        DocumentLabel.Text = "Ask the AI to write something...";
        WritingIndicator.IsVisible = false;
    }
}
