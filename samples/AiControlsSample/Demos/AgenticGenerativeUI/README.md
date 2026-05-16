# 🚀 Agentic Generative UI

## What This Demo Shows

This demo showcases **automatic plan execution** with real-time progress visualization:

1. **Auto-Planning**: The agent creates a multi-step plan and immediately starts executing
2. **Live Progress**: A progress card shows steps completing in real-time with status indicators
3. **No User Intervention**: Unlike Human-in-the-Loop, this demo executes automatically
4. **Visual Feedback**: Steps transition from pending (⏳) to completed (✅) as the agent works

## How to Interact

Try asking:
- "Build a plan to go to Mars in 5 steps"
- "Build a plan to make pizza from scratch"
- "Build a plan to learn guitar in a month"

Then just watch — the agent executes automatically!

## What You Should See

1. **Send a request** — the agent creates a plan with 3-5 steps
2. A **progress card** appears at the bottom showing all steps
3. Steps start as **pending** (⏳ gray)
4. The agent **executes each step** automatically, calling `complete_step` for each
5. Each step transitions to **completed** (✅ green) in sequence
6. The agent sends a **summary message** when all steps are done
7. **Try another request** — a new plan replaces the old one

## Technical Details

- Two tools: `create_plan` and `complete_step`
- Unlike HITL, there is no `confirm_plan` tool — execution is automatic
- The `PlanCardView` is shown in a footer row below the chat
- `ShowConfirmation="False"` hides the confirm/reject buttons
- The agent's system prompt instructs it to execute immediately without waiting

## Inspired By

[CopilotKit Agentic Generative UI Demo](https://dojo.ag-ui.com/microsoft-agent-framework-dotnet/feature/agentic_generative_ui) — `useCoAgentStateRender` with auto-executing steps.
