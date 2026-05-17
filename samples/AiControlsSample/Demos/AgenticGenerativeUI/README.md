# Agentic Generative UI

## Overview

Demonstrates auto-executing plan generation with real-time progress tracking. Unlike the Human-in-the-Loop demo, this agent executes its plan immediately without waiting for user confirmation.

## Features Demonstrated

- `create_plan` tool that deserializes step descriptions and shows a progress footer
- `complete_step` tool that marks steps as completed by zero-based index
- Automatic execution — the system prompt instructs the agent to proceed without waiting
- Footer-based progress UI with ⏳ (pending) and ✅ (completed) status indicators
- No confirm/reject buttons — fully autonomous agent behavior

## How to Use

1. Navigate to **Agentic Generative UI** from the app shell
2. Ask the agent to do something multi-step, e.g., "Build a plan to make pizza from scratch"
3. Watch the footer appear showing all plan steps as ⏳ pending
4. Observe each step transition to ✅ completed as the agent executes automatically
5. Read the agent's summary message after all steps are done
6. Try another request — a new plan replaces the previous one

## Expected Behavior

- The agent calls `create_plan` with a JSON array of 3-5 step descriptions → the plan footer becomes visible
- Without pausing, the agent immediately calls `complete_step(0)`, `complete_step(1)`, etc., in sequence
- Each step transitions from ⏳ to ✅ with reduced opacity for completed items
- After all steps complete, the agent sends a conversational summary of what was accomplished
- A new request creates a fresh plan, replacing the previous steps in the footer

## Key Code Patterns

- **No confirmation gate** — compared to HumanInTheLoop, this demo has no confirm/reject mechanism; the system prompt says "Do NOT wait for user confirmation — just execute" (`AgenticGenerativeUIPage.xaml.cs:24-32`)
- **Footer layout** — the plan progress is displayed in a `Border` at `Grid.Row="1"` below the chat rather than a side panel (`AgenticGenerativeUIPage.xaml:25-35`)
- **Shared step rendering** — `RefreshStepsUI()` pattern is identical to HumanInTheLoop but uses ⏳/✅ instead of ⬜/✅ (`AgenticGenerativeUIPage.xaml.cs:69-90`)
