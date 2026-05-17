using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AiControlsSample;

public partial class PredictiveStatePage : ContentPage
{
    public ChatSession ChatSession { get; }

    private string _currentDocument = string.Empty;
    private string _pendingDocument = string.Empty;

    public PredictiveStatePage(IChatClient chatClient)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(WriteDocument, "write_document",
                "Write or replace the document content. Shows a preview to the user.")
        };

        ChatSession = new ChatSession(tools, chatClient)
        {
            SystemPrompt = """
                You are a document writer. When the user asks you to write or edit:
                1. Call write_document with the full document text.
                2. The user will see the content and can Accept or Reject it.
                3. If accepted, the document is saved. If rejected, you'll be asked to try again.

                Write in markdown format. Be creative and detailed.
                When editing, preserve the overall structure but improve the requested parts.
                """
        };

        InitializeComponent();
    }

    [Description("Write or replace the document content. Shows a preview to the user.")]
    private string WriteDocument(
        [Description("The full document text in markdown")] string content)
    {
        _pendingDocument = content;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DocumentLabel.Text = content;
            WritingIndicator.IsVisible = false;
            ConfirmButtons.IsVisible = true;
        });
        return "Document preview shown to user. Waiting for acceptance.";
    }

    private async void OnAcceptClicked(object? sender, EventArgs e)
    {
        _currentDocument = _pendingDocument;
        ConfirmButtons.IsVisible = false;
        await ChatSession.SendAsync("I accept the changes.");
    }

    private async void OnRejectClicked(object? sender, EventArgs e)
    {
        ConfirmButtons.IsVisible = false;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DocumentLabel.Text = string.IsNullOrEmpty(_currentDocument)
                ? "Ask the AI to write something..."
                : _currentDocument;
        });
        await ChatSession.SendAsync("I reject the changes, please try again.");
    }
}
