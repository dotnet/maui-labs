# 🔧 Backend Tool Rendering

## What This Demo Shows

This demo showcases **tool call rendering** in the chat interface:

1. **Tool Invocation Display**: See tool calls and their results rendered inline in the chat
2. **Structured Tool Results**: Weather data, calculations, and app info shown with structured formatting
3. **Multi-Tool Conversations**: Ask follow-up questions that trigger different tools

## How to Interact

Try asking:
- "What's the weather like in San Francisco?"
- "Calculate (100 * 3.14) / 2"
- "Tell me a random fun fact"
- "What's the weather in Tokyo and then calculate 42 * 7?"

## What You Should See

1. **Send a weather query** — the assistant calls `get_weather` and shows the tool call in the chat
2. The **tool result** (weather JSON) appears inline, followed by the assistant's interpretation
3. **Send a calculation** — the `calculate` tool is invoked and the result is shown
4. **Multi-turn**: ask follow-up questions — the conversation history is preserved correctly
5. Tool messages show the function name, arguments, and result with expand/collapse

## Technical Details

- Tools are registered from `SampleTools` class: `get_weather`, `calculate`, `get_random_fact`, `get_app_info`
- The `ShowToolMessages="True"` property makes tool calls visible in the chat
- Tool results are displayed via the `ToolMessageTemplate` in the control's theme
- `FunctionCallContent` and `FunctionResultContent` are preserved in chat history

## Inspired By

[CopilotKit Backend Tool Rendering Demo](https://dojo.ag-ui.com/microsoft-agent-framework-dotnet/feature/backend_tool_rendering) — rendering agent tool calls with state.
