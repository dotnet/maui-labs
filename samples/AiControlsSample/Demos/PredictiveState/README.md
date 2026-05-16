# ✍️ Predictive State Updates

## What This Demo Shows

This demo showcases **document editing** with AI-assisted writing and **accept/reject** workflow:

1. **Document Editor**: A document panel on the left shows the current text
2. **AI Writer**: Ask the assistant to write or edit content via chat on the right
3. **Preview Changes**: The document updates with a preview and a "✍️ Writing..." indicator
4. **Accept/Reject**: Buttons appear to confirm or revert the changes

## How to Interact

Try asking:
- "Write a short story about a pirate named Candy Beard"
- "Write a brief article about the future of AI"
- "Write a poem about the ocean at sunset"

Then:
1. Watch the **document panel update** with the new content
2. Click **✅ Accept** to keep the changes, or **❌ Reject** to revert
3. If rejected, ask the agent to try again with different instructions
4. **Edit in sequence** — ask for modifications to the accepted document

## What You Should See

1. **Send a writing request** — the agent calls `write_document` with the content
2. The **document panel** shows the text with a "✍️ Writing..." indicator
3. **Accept/Reject buttons** appear in the document header
4. Click **Accept** — the content is saved and the indicator disappears
5. Click **Reject** — the document reverts to the previous version
6. **Ask for edits** — "Make it funnier" or "Add more detail" to modify the saved document
7. The agent sees the current document state and builds on it

## Technical Details

- Two tools: `write_document` and `confirm_changes`
- `write_document` sets the document text and shows a preview
- `confirm_changes` uses `WaitForResponse("confirm_document")` to block until the user responds
- Accepted changes become the new baseline; rejected changes revert to the previous document
- The document state is preserved across multiple editing rounds

## Inspired By

[CopilotKit Predictive State Updates Demo](https://dojo.ag-ui.com/microsoft-agent-framework-dotnet/feature/predictive_state_updates) — document editor with streaming diffs and confirm/reject.
