# 📋 Human in the Loop

## What This Demo Shows

This demo showcases the **human-in-the-loop** pattern where the AI agent creates a plan and **waits for user approval** before executing:

1. **Plan Generation**: The agent creates a multi-step plan based on your request
2. **User Review**: A plan card appears showing all steps with checkboxes
3. **Confirm/Reject**: You approve or reject the plan before execution begins
4. **Step-by-Step Execution**: After confirmation, the agent executes each step and marks them complete

## How to Interact

Try asking:
- "Create a plan to organize my desk"
- "Create a plan to build a web application with authentication"
- "Create a plan to throw a surprise birthday party"

Then:
1. **Review the plan** that appears in the side panel
2. Click **✅ Confirm** to approve, or **❌ Reject** to decline
3. Watch the agent execute each step, marking them as completed
4. After all steps complete, the agent summarizes what it accomplished

## What You Should See

1. **Send a request** — the agent calls `create_plan` with 3-5 steps
2. A **plan card** appears on the right side showing all steps
3. The agent calls `confirm_plan` and the **Confirm/Reject buttons** appear
4. Click **Confirm** — the agent proceeds to execute each step
5. Steps are **marked completed one by one** with green checkmarks
6. The agent sends a **summary message** after all steps are done
7. **Try rejecting** — the agent acknowledges and asks what to change

## Technical Details

- Three tools: `create_plan`, `confirm_plan`, `update_plan_step`
- `confirm_plan` uses `WaitForResponse("confirm_plan")` to block until the user responds
- `ProvideResponse` sends the user's decision back to the tool
- The `PlanCardView` control displays steps with status indicators
- Plan model uses `ObservableObject` for reactive UI updates

## Inspired By

[CopilotKit Human in the Loop Demo](https://dojo.ag-ui.com/microsoft-agent-framework-dotnet/feature/human_in_the_loop) — `renderAndWaitForResponse` with step checkboxes.
