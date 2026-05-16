using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI;

namespace AiControlsSample;

public partial class PredictiveStatePage : ContentPage
{
    private readonly IAgentSession _session;
    private string _currentDocument = string.Empty;
    private string _pendingDocument = string.Empty;

    public PredictiveStatePage(IAgentSessionFactory sessionFactory, IChatClient chatClient)
    {
        InitializeComponent();

        _session = sessionFactory.Create(chatClient);
        _session.SystemInstructions = """
            You are a document writer. When the user asks you to write or edit:
            1. Call write_document with the full document text.
            2. Then call confirm_changes to wait for user approval.
            3. If confirmed, the document is saved. If rejected, revert.

            Write in markdown format. Be creative and detailed.
            When editing, preserve the overall structure but improve the requested parts.
            """;

        RegisterTools();

        ChatView.Session = _session;
        ChatView.SuggestionPrompts =
        [
            new Suggestion("Pirate story", "Write a short story about a pirate named Candy Beard"),
            new Suggestion("Tech article", "Write a brief article about the future of AI"),
            new Suggestion("Poem", "Write a poem about the ocean at sunset"),
        ];
    }

    private void RegisterTools()
    {
        [Description("Write or replace the document content. Shows a preview to the user.")]
        string write_document(
            [Description("The full document text in markdown")] string content)
        {
            _pendingDocument = content;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DocumentLabel.Text = content;
                WritingIndicator.IsVisible = true;
            });
            return "Document preview shown to user.";
        }

        [Description("Wait for the user to accept or reject the document changes.")]
        async Task<string> confirm_changes()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                WritingIndicator.IsVisible = false;
                ConfirmButtons.IsVisible = true;
            });

            var response = await _session.WaitForResponse("confirm_document");

            MainThread.BeginInvokeOnMainThread(() => ConfirmButtons.IsVisible = false);

            if (response is PlanConfirmationResult result && result.Confirmed)
            {
                _currentDocument = _pendingDocument;
                return "User accepted the changes.";
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DocumentLabel.Text = string.IsNullOrEmpty(_currentDocument)
                        ? "Ask the AI to write something..."
                        : _currentDocument;
                });
                return "User rejected the changes. Ask what they'd like changed.";
            }
        }

        _session.RegisterTools(
            AIFunctionFactory.Create(write_document),
            AIFunctionFactory.Create(confirm_changes));
    }

    private void OnAcceptClicked(object? sender, EventArgs e)
    {
        _session.ProvideResponse("confirm_document", new PlanConfirmationResult { Confirmed = true });
    }

    private void OnRejectClicked(object? sender, EventArgs e)
    {
        _session.ProvideResponse("confirm_document", new PlanConfirmationResult { Confirmed = false });
    }
}
