# Predictive State Updates — Testing Guide

This demo shows an accept/reject workflow: the AI generates content, you preview it, then decide whether to save or revert.

## Scenario 1: Generate and Accept a Document

1. Navigate to **Predictive State** from the flyout menu.
2. You should see an empty document panel on the left and a chat panel on the right.
3. Type **"Write a short poem about the ocean"** and send.
4. **Expected:** The document panel shows the generated poem text. **✅ Accept** and **❌ Reject** buttons appear below the document header. A "✍️ Writing..." indicator may briefly flash.
5. Click **✅ Accept**.
6. **Expected:** The poem is saved. The Accept/Reject buttons disappear. The document content remains visible.

## Scenario 2: Generate and Reject, Then Retry

1. Type **"Write a haiku about rain"** and send.
2. **Expected:** A haiku appears in the document panel, replacing the previous poem. Accept/Reject buttons are visible.
3. Click **❌ Reject**.
4. **Expected:** The document reverts to the previously accepted poem (from Scenario 1). The agent acknowledges the rejection and asks you to try again.
5. Type **"Try a different haiku about rain"** and send.
6. **Expected:** A new haiku appears with Accept/Reject buttons again.

## Scenario 3: Edit then Replace

1. Accept a document.
2. Type **"Now make it longer with two more stanzas"** and send.
3. **Expected:** The agent generates an expanded version. The document panel shows the new longer content with Accept/Reject buttons. The old accepted version is preserved if you reject.

## Scenario 4: Multiple Accept/Reject Cycles

1. Accept a document, then request a modification, then reject it.
2. **Expected:** You see the previously accepted content again, not an empty panel.
3. Request another modification and accept it.
4. **Expected:** The new content replaces the previous accepted version.
