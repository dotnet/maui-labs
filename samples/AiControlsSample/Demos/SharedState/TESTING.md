# Shared State — Testing Guide

This demo shows bidirectional state synchronization between a recipe form and an AI agent. The agent can read and update the form fields.

## Scenario 1: Improve Recipe with AI

1. Navigate to **Shared State** from the flyout menu.
2. You should see a recipe form on the left (Pasta Primavera, Beginner, 30 min, 5 ingredients) and a chat panel on the right.
3. Click the **✨ Improve with AI** button.
4. **Expected:** A user message appears containing the current recipe as JSON. The agent calls `update_recipe` and the form fields update — the title may change, the skill level may advance, cooking time may adjust, and ingredients may be added or modified. The agent explains its changes.

## Scenario 2: Manual Edit then AI Improve

1. Change the **Title** to "Spicy Thai Noodles".
2. Change the **Skill Level** to "Advanced".
3. Click **✨ Improve with AI**.
4. **Expected:** The message sent includes your manual changes. The agent's improvements build on "Spicy Thai Noodles" with "Advanced" skill level, not the original Pasta Primavera.

## Scenario 3: Chat-Based Recipe Modifications

1. Type **"Can you add some protein to this recipe?"** in the chat and send.
2. **Expected:** The agent calls `update_recipe` with the protein additions. The ingredients list updates on the left to include items like chicken, tofu, or shrimp.

## Scenario 4: Verify Ingredient List Updates

1. After an AI improvement, count the ingredients shown in the form.
2. **Expected:** Each ingredient has an emoji icon, and the list matches what the agent described in its response. New ingredients appear at the bottom of the list.
