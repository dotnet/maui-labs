# Predictive State Updates

## Overview

A document writer with an accept/reject workflow. The AI writes content via the `write_document` tool, the user previews the proposed text, and then decides whether to accept or reject it before it becomes the saved document.

## Features Demonstrated

- Split-pane layout with document preview (left) and chat panel (right)
- `write_document` tool that sets pending content and shows Accept/Reject buttons
- Two-tier state: `_currentDocument` (accepted baseline) and `_pendingDocument` (proposed changes)
- "✍️ Writing..." indicator during document generation
- Reject reverts to previous content; accept promotes pending to current

## How to Use

1. Navigate to **Predictive State Updates** from the app shell
2. The left panel shows placeholder text: "Ask the AI to write something..."
3. Ask the agent to write something, e.g., "Write a short story about a robot chef"
4. Observe the document panel update with the generated content
5. Click **✅ Accept** to save the document, or **❌ Reject** to revert
6. If accepted, ask for modifications: "Make it funnier" or "Add a twist ending"
7. If rejected, the document reverts to the prior accepted version (or placeholder)

## Expected Behavior

- The agent calls `write_document(content)` → the document label updates, the writing indicator hides, and Accept/Reject buttons appear
- **Accept**: `_currentDocument = _pendingDocument`, buttons hide, "I accept the changes." sent to agent
- **Reject**: document label reverts to `_currentDocument` (or placeholder if empty), "I reject the changes, please try again." sent to agent
- The agent can reference accepted content in follow-up responses for iterative editing
- Multiple accept/reject cycles maintain a single "current" baseline document

## Key Code Patterns

- **Pending vs. current state** — `_pendingDocument` holds the proposed text; `_currentDocument` holds the last accepted version. Accept promotes, reject discards (`PredictiveStatePage.xaml.cs:11-12, 53-54, 61-64`)
- **Tool returns "waiting" status** — `WriteDocument` returns "Document preview shown to user. Waiting for acceptance." so the agent knows to pause (`PredictiveStatePage.xaml.cs:49`)
- **Programmatic user messages** — Accept/Reject buttons call `ChatSession.SendAsync()` to inform the agent of the user's decision (`PredictiveStatePage.xaml.cs:56, 68`)
