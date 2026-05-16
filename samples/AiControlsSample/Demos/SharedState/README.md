# 🍳 Shared State

## What This Demo Shows

This demo showcases **bidirectional shared state** between the AI agent and the UI:

1. **Recipe Editor**: A form on the left with title, skill level, cooking time, and ingredients
2. **AI Chat**: A chat panel on the right that can read and modify the recipe
3. **Bidirectional Updates**: Edit the form manually OR ask the AI to improve it — both directions work
4. **"Improve with AI" Button**: Sends the current recipe state to the agent for enhancement

## How to Interact

Try:
- Click **"✨ Improve with AI"** to send the current recipe to the agent
- Ask: "Make this recipe healthier with more vegetables"
- Ask: "Add more protein to this recipe"
- Ask: "Make this recipe spicier with chili and paprika"
- Manually edit the recipe title, skill level, or ingredients, then ask the AI to improve it

## What You Should See

1. The **recipe form** starts with "Pasta Primavera" and default ingredients
2. **Click "Improve with AI"** — the agent receives the recipe JSON and responds
3. The agent calls `update_recipe` with improved values
4. **Form fields update automatically** — title, skill level, cooking time, and ingredients change
5. **Edit manually** — change the title or add an ingredient
6. **Ask the AI again** — it sees the updated recipe and builds on your changes
7. Multiple rounds of editing show the state flowing both directions

## Technical Details

- The agent has an `update_recipe` tool that receives the full recipe as JSON
- Recipe state is built from the form fields and sent as a `ChatMessage` with `DataContent`
- The `StateSnapshotReceived` event on `IAgentSession` handles incoming state updates
- `ApplyRecipe` parses the JSON and updates all form fields on the main thread
- Ingredients are dynamically rendered as a list of rows

## Inspired By

[CopilotKit Shared State Demo](https://dojo.ag-ui.com/microsoft-agent-framework-dotnet/feature/shared_state) — bidirectional recipe form with `useCoAgentStateRender`.
