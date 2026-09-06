# Shared State

## Overview

A split-pane recipe editor and chat panel demonstrating bidirectional state synchronization between a UI form and an AI agent. The AI can read the current recipe state and modify it via the `update_recipe` tool.

## Features Demonstrated

- Split-pane layout with a form editor (left) and `CopilotChatView` (right)
- `update_recipe` tool that accepts title, skill level, cooking time, and ingredients JSON
- "Improve with AI" button that serializes the current form state and sends it to the chat
- Dynamic ingredient list rendered programmatically from a `List<(string Icon, string Name, string Amount)>`
- JSON serialization/deserialization for bidirectional state transfer

## How to Use

1. Navigate to **Shared State** from the app shell
2. Review the default recipe: "Pasta Primavera" with 5 ingredients
3. Click **"✨ Improve with AI"** — the current recipe is sent as JSON to the agent
4. The agent responds with suggestions and calls `update_recipe` to modify the form
5. Observe the form fields (title, skill picker, time picker, ingredients) update automatically
6. Manually edit the form (change title, add ingredients with "+ Add Ingredient")
7. Click "Improve with AI" again — the agent sees your manual changes and builds on them

## Expected Behavior

- The form starts with "Pasta Primavera", Beginner skill, 30 min cooking time, and 5 ingredients (🍝🫑🧅🧄🫒)
- "Improve with AI" serializes `{ title, skill_level, cooking_time, ingredients }` as indented JSON
- The agent calls `update_recipe(title, skill_level, cooking_time, ingredients_json)` with improved values
- All form fields update on the UI thread: `RecipeTitleEntry.Text`, `SkillPicker.SelectedIndex`, `TimePicker.SelectedIndex`, and the ingredients list
- Adding ingredients manually via the "+ Add Ingredient" button appends a placeholder row
- Multiple improvement rounds show cumulative changes

## Key Code Patterns

- **State serialization** — `BuildRecipeJson()` creates a JSON snapshot from form controls for sending to the agent (`SharedStatePage.xaml.cs:118-129`)
- **Tool-driven form updates** — `UpdateRecipe()` parses skill level/time by matching picker items, and deserializes ingredients from a JSON array of `{icon, name, amount}` objects (`SharedStatePage.xaml.cs:42-91`)
- **Dynamic UI rendering** — `RefreshIngredientsUI()` clears and rebuilds `IngredientsLayout.Children` from the in-memory list (`SharedStatePage.xaml.cs:93-104`)
