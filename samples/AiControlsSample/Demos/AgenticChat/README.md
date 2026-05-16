# 🎨 Agentic Chat with Frontend Tools

## What This Demo Shows

This demo showcases the AI chat control's **agentic chat** capabilities with **frontend tool integration**:

1. **Natural Conversation**: Chat with an AI assistant in a familiar chat interface
2. **Frontend Tool Execution**: The assistant can directly interact with the UI by calling frontend-registered tools
3. **Seamless Integration**: Tools defined in C# are automatically discovered and made available to the agent

## How to Interact

Try asking the assistant to:
- "Change the background to light blue"
- "Set the background to a warm sunset orange"
- "Change it to a dark slate color"
- "Make the background salmon"

You can also chat about other topics — the assistant will respond conversationally while having the ability to use your UI tools when appropriate.

## What You Should See

1. **Send a message** requesting a background color change
2. The assistant calls the `change_background` tool
3. **The page background changes immediately** to the requested color
4. The assistant provides a conversational response about the change
5. **Send another message** with a different color — the background updates again
6. You can also ask non-tool questions and get normal chat responses

## Technical Details

- A `change_background` tool is registered via `AIFunctionFactory.Create`
- The tool uses `Color.TryParse` to set the page's `BackgroundColor`
- The chat uses streaming responses from Azure OpenAI
- Multiple color changes in sequence demonstrate stateful tool calling

## Inspired By

[CopilotKit Agentic Chat Demo](https://dojo.ag-ui.com/microsoft-agent-framework-dotnet/feature/agentic_chat) — frontend function exposure via `useCopilotAction`.
