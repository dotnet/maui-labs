# Agentic Chat — Testing Guide

This demo shows real-time UI mutation: the AI agent changes the page background color through a tool call.

## Scenario 1: Change Background by Name

1. Navigate to **Agentic Chat** from the flyout menu.
2. Type **"Make the background blue"** and send.
3. **Expected:** The agent calls `change_background`, the entire page background turns blue, and the assistant confirms: "I changed the background to blue" (or similar).

## Scenario 2: Change Background by Hex Code

1. Type **"Set the background to #FF6B6B"** and send.
2. **Expected:** The background changes to a coral/salmon color. The tool call badge shows the hex value passed.

## Scenario 3: Vague Color Request

1. Type **"Make it feel like a sunset"** and send.
2. **Expected:** The agent picks a creative color (orange, pink, gold, etc.) and applies it. The assistant describes which color it chose and why.

## Scenario 4: Non-Color Conversation

1. Type **"What's your favorite programming language?"** and send.
2. **Expected:** The agent responds conversationally WITHOUT calling the `change_background` tool. The background stays unchanged from the last color.

## Scenario 5: Multiple Color Changes

1. Send three color change requests in sequence: "Red", "Green", "Purple".
2. **Expected:** Each time the background updates immediately. The conversation shows three user messages, three tool calls, and three assistant responses. The final background is purple.
