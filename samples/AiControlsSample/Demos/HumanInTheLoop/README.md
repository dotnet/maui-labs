# Human in the Loop

## Overview

Demonstrates an AI agent that creates multi-step plans requiring explicit human confirmation before execution. A side panel displays the plan steps, and the user must click Confirm or Reject before the agent proceeds.

## Features Demonstrated

- `create_plan` tool that deserializes a JSON array of step descriptions and displays them in a side panel
- `update_plan_step` tool that marks individual steps as completed by zero-based index
- Confirm/Reject buttons that programmatically send messages back to the `ChatSession`
- `ToolApprovalTemplate` in the content templates for tool approval UI
- Responsive layout — plan panel hides when window width < 700px

## How to Use

1. Navigate to **Human in the Loop** from the app shell
2. Ask something like "Create a plan to organize my desk" or "Plan how to build a web app"
3. Review the plan that appears in the right-side panel (3-5 steps with ⬜ checkboxes)
4. Click **✅ Confirm** to approve the plan, or **❌ Reject** to decline
5. If confirmed, watch each step get marked ✅ as the agent executes them sequentially
6. If rejected, the agent acknowledges and asks what to change

## Expected Behavior

- The agent calls `create_plan` with a JSON array of step descriptions → the plan panel becomes visible
- Confirm/Reject buttons appear below the step list
- Clicking **Confirm** sends "I confirm the plan. Please proceed with execution." to the chat session
- The agent then calls `update_plan_step` for each step index (0, 1, 2...) marking them complete
- Clicking **Reject** sends "I reject this plan. Please suggest changes." — the agent responds conversationally
- Steps display ⬜ (pending) or ✅ (completed) with reduced opacity for completed items

## Key Code Patterns

- **Plan state management** — `_currentSteps` list of `PlanStep` objects tracks completion state; `RefreshStepsUI()` rebuilds the visual layout each time (`HumanInTheLoopPage.xaml.cs:70-91`)
- **Programmatic chat messages** — `ChatSession.SendAsync("I confirm the plan...")` sends user messages on button click without manual typing (`HumanInTheLoopPage.xaml.cs:96, 102`)
- **Responsive panel visibility** — `OnSizeAllocated` hides the plan panel on narrow windows (`HumanInTheLoopPage.xaml.cs:105-109`)
