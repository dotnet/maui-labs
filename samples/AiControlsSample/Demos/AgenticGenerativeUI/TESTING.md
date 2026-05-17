# Agentic Generative UI — Testing Guide

This demo shows fully autonomous plan execution: the agent creates a plan and immediately executes all steps without waiting for user approval.

## Scenario 1: Auto-Executing Plan

1. Navigate to **Agentic Gen UI** from the flyout menu.
2. Type **"Create a plan to learn photography"** and send.
3. **Expected:** A footer panel appears at the bottom showing "🚀 Plan Progress" with 3–5 steps. The agent immediately starts executing — each step transitions from ⏳ to ✅ without any user interaction. The agent sends a summary when done.

## Scenario 2: Watch Step Progression

1. Type **"Plan to bake a cake from scratch"** and send.
2. Watch the footer progress.
3. **Expected:** Steps complete one at a time (not all at once). You can see the ⏳ → ✅ transition happen for each step in sequence as the agent processes.

## Scenario 3: New Plan Replaces Old

1. After the first plan completes, type **"Now plan a camping trip"** and send.
2. **Expected:** The old plan steps in the footer are replaced with new camping-related steps. The new steps start executing immediately.

## Scenario 4: Compare with Human in the Loop

1. Note: Unlike the HITL demo, there are **no Confirm/Reject buttons**. The agent just goes.
2. **Expected:** This is intentional — the Agentic Generative UI demo demonstrates fully autonomous execution versus the supervised approach in Human in the Loop.
