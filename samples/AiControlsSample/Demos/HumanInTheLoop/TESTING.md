# Human in the Loop — Testing Guide

This demo shows multi-step planning with explicit human approval. The agent proposes a plan, waits for user confirmation, and only executes when approved.

## Scenario 1: Create and Confirm a Plan

1. Navigate to **Human in the Loop** from the flyout menu.
2. Type **"Create a plan to organize my desk"** and send.
3. **Expected:** A plan panel slides in from the right showing 3–5 steps, each with a ⬜ checkbox. Two buttons appear below: **✅ Confirm** and **❌ Reject**. The agent says something like "Here's my plan…".
4. Click **✅ Confirm**.
5. **Expected:** The agent begins executing steps one by one. Each step's ⬜ changes to ✅ as `update_plan_step` is called. The agent sends a completion summary when done.

## Scenario 2: Reject a Plan

1. Type **"Plan a weekend trip to the mountains"** and send.
2. **Expected:** A new plan appears in the side panel with fresh steps.
3. Click **❌ Reject**.
4. **Expected:** The agent acknowledges the rejection and asks what you'd like to change. The plan panel remains visible but no steps are executed.
5. Type **"Make it a beach trip instead"** and send.
6. **Expected:** A new plan replaces the old one with beach-related steps.

## Scenario 3: Plan Steps Execute in Order

1. Type **"Plan to make a cup of tea"** and confirm the plan.
2. Watch the steps update.
3. **Expected:** Steps are marked complete sequentially (step 0 first, then 1, then 2, etc.), not all at once. Each step gets a ✅ one at a time.

## Scenario 4: Narrow Window Hides Plan Panel

1. Resize the window to under 700px wide.
2. **Expected:** The plan panel hides. The chat takes the full width.
3. Resize back wider.
4. **Expected:** The plan panel reappears with its current state preserved.
