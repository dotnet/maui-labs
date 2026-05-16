# 🎋 Tool-Based Generative UI (Haiku)

## What This Demo Shows

This demo showcases **tool-based generative UI** where the agent creates visual content displayed in a custom panel:

1. **Haiku Display**: A colored panel on the left shows haiku poems in Japanese and English
2. **AI Poet**: Chat with the assistant on the right to request haiku poems
3. **Visual Theming**: Each haiku gets a unique background color chosen by the AI
4. **Gallery Navigation**: Browse through multiple generated haikus with prev/next buttons

## How to Interact

Try asking:
- "Write a haiku about nature"
- "Create a haiku about the ocean"
- "Generate a haiku about spring"
- "Write me three haikus about different seasons"

## What You Should See

1. Initially, the left panel shows an empty state with "🎋 Ask for a haiku to get started"
2. **Send a haiku request** — the agent calls `create_haiku` with Japanese and English lines
3. The **haiku appears** on the left panel with Japanese text, a divider, and English translation
4. The **background color** changes to the AI's chosen theme color
5. **Request another haiku** — it gets added to the gallery
6. **Navigation arrows** appear when you have multiple haikus (◀ ▶)
7. Browse through your collection with the prev/next buttons
8. Each haiku has a **position indicator** (e.g., "2 / 3")

## Technical Details

- One tool: `create_haiku` with `japanese_lines`, `english_lines`, and `theme_color` parameters
- Lines are passed as JSON arrays of 3 strings (5-7-5 mora pattern for Japanese)
- The haiku panel uses a cycling palette of gradient colors as fallback
- Navigation state tracks the current index in the haiku collection
- The layout is split 50/50 between the haiku display and the chat panel

## Inspired By

[CopilotKit Tool Based Generative UI Demo](https://dojo.ag-ui.com/microsoft-agent-framework-dotnet/feature/tool_based_generative_ui) — custom tool `render` function for haiku cards.
