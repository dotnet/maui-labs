# AI Controls Sample — Testing Guide

## Prerequisites

### Azure OpenAI Configuration

The sample requires Azure OpenAI credentials configured via user secrets. Set them up once (shared across all AI samples in this repo):

```bash
dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<your-azure-openai-endpoint>"
dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<your-api-key>"
dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<your-deployment-name>"
```

The deployment should support function calling (GPT-4o or equivalent recommended).

### Build and Run

```bash
# From the repo root
dotnet build samples/AiControlsSample/AiControlsSample.csproj

# Or run directly (requires a target platform, e.g. maccatalyst)
dotnet build samples/AiControlsSample/AiControlsSample.csproj -f net10.0-maccatalyst -t:Run
```

Alternatively, open `MauiLabs.slnx` in Visual Studio and set `AiControlsSample` as the startup project.

### MAUI Workload

Ensure the MAUI workload is installed:

```bash
dotnet workload install maui
```

---

## Demo Walkthroughs

### Playground

**Location:** `PlaygroundPage.xaml` (root of the sample project)

**Steps:**
1. Launch the app — Playground is the default page
2. Observe the settings sidebar (visible on wide windows ≥700px) with system prompt editor, tools list, and quick prompts
3. Click a quick prompt button (e.g., "🌤 What's the weather in Tokyo?")
4. Verify the agent calls `get_current_weather` and responds with weather information
5. Click "🔢 Calculate (42 * 3) + 7" — verify the agent calls `calculate` and returns 133
6. Click "💡 Tell me a random fact" — verify a fun fact is returned
7. Click "📱 What app am I running?" — verify app name, version, and platform are shown
8. Edit the system prompt in the sidebar and click "Apply System Prompt" — verify subsequent responses reflect the new prompt
9. Click "🗑️ Clear" in the toolbar — verify the chat history is cleared
10. Resize the window below 700px — verify the settings sidebar hides

**Expected Results:**
- All four tools (weather, calculate, random fact, app info) work correctly
- Tool calls and results render inline in the conversation
- System prompt changes take effect on the next message
- Collapsible sections (▼/▶) toggle visibility when tapped

---

### Agentic Chat

**Location:** `Demos/AgenticChat/`

**Steps:**
1. Navigate to **Agentic Chat** via the app shell
2. Type: "Make the background light blue"
3. Verify the page background changes to light blue
4. Type: "Change it to salmon"
5. Verify the background changes to salmon
6. Type: "Use hex color #2D1B69"
7. Verify the background changes to a dark purple
8. Type: "What is 2+2?" (a non-color question)
9. Verify a normal text response without a background change

**Expected Results:**
- Background color updates immediately after each tool call
- Both color names and hex values are accepted
- Non-color questions get conversational responses (no tool invocation)
- The agent describes the color change in its response text

---

### Tool Rendering

**Location:** `Demos/ToolRendering/`

**Steps:**
1. Navigate to **Tool Rendering**
2. Type: "What's the weather in San Francisco?"
3. Verify a rich weather card appears with: city name, temperature (°C), conditions, humidity (%), wind speed (km/h), feels-like temperature
4. Verify the card has a weather icon emoji and styled border
5. Type: "Calculate (100 * 3.14) / 2"
6. Verify the calculation result appears in the generic `FunctionResultTemplate` format (not a custom card)
7. Type: "Tell me a random fact"
8. Verify the fact appears in the generic result template
9. Compare the weather card styling vs. the generic tool result styling

**Expected Results:**
- Weather results render as a custom `WeatherResultView` card with structured layout
- Non-weather tool results use the default `FunctionResultTemplate` (plain text rendering)
- `FunctionCallTemplate` shows the tool name and arguments inline
- The agent provides natural language interpretation after each tool result

---

### Human in the Loop

**Location:** `Demos/HumanInTheLoop/`

**Steps:**
1. Navigate to **Human in the Loop**
2. Type: "Create a plan to organize a team hackathon"
3. Verify a plan panel appears on the right with 3-5 steps (⬜ checkboxes)
4. Verify Confirm (✅) and Reject (❌) buttons appear below the steps
5. Click **✅ Confirm**
6. Verify steps are marked complete (✅) one by one as the agent executes
7. Verify the agent sends a summary after all steps complete
8. Start a new request: "Plan how to learn a new language"
9. This time, click **❌ Reject**
10. Verify the agent acknowledges the rejection and asks what to change
11. Resize the window below 700px — verify the plan panel hides

**Expected Results:**
- Plan panel shows all steps with pending (⬜) status initially
- After confirmation, steps transition to completed (✅) sequentially
- Rejection triggers a conversational response asking for modifications
- The plan panel is responsive and hides on narrow viewports
- `ToolApprovalTemplate` renders tool approval UI in the chat

---

### Shared State

**Location:** `Demos/SharedState/`

**Steps:**
1. Navigate to **Shared State**
2. Verify the recipe form shows: "Pasta Primavera", Beginner, 30 min, 5 ingredients
3. Click **"✨ Improve with AI"**
4. Verify the agent receives the recipe JSON and responds with improvement suggestions
5. Verify the form fields update (title may change, ingredients may be added/modified)
6. Manually change the title to "Spicy Noodles"
7. Click **"+ Add Ingredient"** — verify a "New Ingredient" row appears
8. Click **"✨ Improve with AI"** again
9. Verify the agent sees "Spicy Noodles" and the new ingredient in its response
10. Verify the form updates with the agent's new suggestions

**Expected Results:**
- The "Improve with AI" button serializes current form state to JSON and sends it as a user message
- The agent calls `update_recipe` with improved values — form fields update automatically
- Skill level and cooking time pickers update to matching items
- Ingredients list rebuilds dynamically with emoji icons
- Manual edits are preserved in subsequent AI interactions (bidirectional sync)

---

### Predictive State Updates

**Location:** `Demos/PredictiveState/`

**Steps:**
1. Navigate to **Predictive State Updates**
2. Verify the left panel shows: "Ask the AI to write something..."
3. Type: "Write a haiku about programming"
4. Verify the document panel updates with the generated text
5. Verify Accept (✅) and Reject (❌) buttons appear in the document header
6. Click **✅ Accept**
7. Verify buttons hide and the content is saved
8. Type: "Now make it about coffee instead"
9. Verify new content appears with Accept/Reject buttons again
10. Click **❌ Reject**
11. Verify the document reverts to the previously accepted haiku (about programming)

**Expected Results:**
- The "✍️ Writing..." indicator appears briefly while the tool executes
- Accept saves the pending content as the current document baseline
- Reject restores the previous accepted content (or the placeholder if nothing was accepted)
- The agent's follow-up responses reference the current document state
- Multiple accept/reject cycles maintain correct state

---

### Agentic Generative UI

**Location:** `Demos/AgenticGenerativeUI/`

**Steps:**
1. Navigate to **Agentic Generative UI**
2. Type: "Build a plan to learn guitar in 5 steps"
3. Verify a progress footer appears at the bottom with all steps as ⏳ pending
4. Verify steps automatically transition to ✅ completed without any user interaction
5. Verify the agent sends a summary message after completing all steps
6. Type another request: "Plan a weekend camping trip"
7. Verify a new set of steps appears (previous plan is replaced)

**Expected Results:**
- No Confirm/Reject buttons appear — execution is fully automatic
- Steps transition from ⏳ to ✅ in sequence (with brief delays between tool calls)
- The footer is only visible after the first `create_plan` call
- Completed steps have reduced opacity (0.6)
- The agent does not pause between step completions

---

### Haiku Generator

**Location:** `Demos/HaikuGenerator/`

**Steps:**
1. Navigate to **Haiku Generator**
2. Verify the left panel shows the empty state: "🎋 Ask for a haiku to get started"
3. Type: "Write a haiku about cherry blossoms"
4. Verify a haiku appears with Japanese text (large, white), a divider, and English translation
5. Verify the panel background color changes to a theme color chosen by the AI
6. Type: "Write another haiku about the moon"
7. Verify the new haiku is displayed and navigation arrows (◀ ▶) appear
8. Verify the position label shows "2 / 2"
9. Click **◀** (prev) — verify you return to the cherry blossom haiku (position "1 / 2")
10. Click **▶** (next) — verify you return to the moon haiku
11. Verify the background color changes per haiku as you navigate

**Expected Results:**
- Japanese lines render at large font size (28pt), English lines below at smaller size (16pt)
- Each haiku has a unique background color (from the AI's `theme_color` parameter)
- Navigation is bounded: prev is disabled at index 0, next is disabled at the last index
- The empty state disappears after the first haiku is created
- Multiple haikus accumulate in the gallery without replacing each other

---

## Troubleshooting

### "AI services are not configured" exception on startup

User secrets are missing or incomplete. Run the three `dotnet user-secrets` commands from the Prerequisites section. The secrets ID is `ai-attributes-secrets`.

### Tool calls fail or agent responds "I don't have access to tools"

- Verify the Azure OpenAI deployment supports function calling (GPT-4o, GPT-4-turbo, or similar)
- Check that the `FunctionInvocation` middleware is in the pipeline (`UseFunctionInvocation()` in `MauiProgram.cs`)

### Weather card shows "--" for all fields

The `get_current_weather` tool in `SampleTools.cs` uses deterministic random based on city name hash — it always returns data. If you see "--", the JSON parsing in `WeatherResultView.RefreshFromContentContext()` may be failing. Check that the `FunctionResultContent.Result` is a non-null string.

### Plan panel not visible (Human in the Loop)

The plan panel auto-hides when window width < 700px. Resize the window wider, or check on a tablet/desktop-sized display.

### Background color doesn't change (Agentic Chat)

- Verify `Color.TryParse()` supports the color format the agent used. Named colors (e.g., "LightBlue") and hex (e.g., "#ADD8E6") are supported.
- Check the debug output for the tool invocation — the agent may be using an unsupported format.

### Chat appears empty / no response

- Network connectivity — Azure OpenAI endpoint must be reachable
- Check debug console for HTTP errors or timeout exceptions
- The `IChatClient` pipeline includes logging (`UseLogging(lf)`) — check `ILogger` output for details

### Settings sidebar not visible (Playground)

The sidebar hides when window width < 700px. Widen the window to see it appear.
