# Agentic Chat

## Overview

Demonstrates an AI agent that can modify the app's UI in real time. The agent has a `change_background` tool that changes the page background color, showing how AI tool calls can produce immediate visual side effects.

## Features Demonstrated

- Custom `AgentContext` with inline tool definition via `AIFunctionFactory.Create`
- AI tool execution that directly mutates MAUI UI elements (`PageRoot.BackgroundColor`)
- `Color.TryParse()` accepting both named colors ("LightBlue") and hex values ("#ADD8E6")
- System prompt guiding the agent to be creative with color suggestions
- `MainThread.BeginInvokeOnMainThread` for thread-safe UI updates from tool callbacks

## How to Use

1. Navigate to **Agentic Chat** from the app shell
2. Type a message like "Make the background blue" or "Set it to a warm sunset orange"
3. Observe the page background change immediately after the agent invokes the tool
4. Try vague requests like "something calming" — the agent picks a creative color
5. Ask non-color questions to confirm normal conversational responses still work

## Expected Behavior

- The agent calls `change_background` with a color string whenever the user requests a color change
- The page `Grid` background transitions to the parsed color instantly
- If a color cannot be parsed directly, the code retries with a `#` prefix (e.g., "ADD8E6" → "#ADD8E6")
- The assistant describes what it did after each tool invocation
- Non-color questions receive standard conversational replies without tool calls

## Key Code Patterns

- **Inline tool registration** — the tool lambda is defined directly in the constructor using `AIFunctionFactory.Create` with `[Description]` attributes on parameters (`AgenticChatPage.xaml.cs:13-37`)
- **Thread-safe UI mutation** — `MainThread.BeginInvokeOnMainThread(() => PageRoot.BackgroundColor = parsed)` ensures the color change runs on the UI thread (`AgenticChatPage.xaml.cs:21-31`)
- **Custom AgentContext** — the page creates its own `UIAgent` + `AgentContext` with a dedicated tool list rather than using a shared session (`AgenticChatPage.xaml.cs:39-50`)
