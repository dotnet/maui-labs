# Playground — Testing Guide

The Playground combines a full-featured chat panel with a settings sidebar. It exercises all four shared tools (weather, math, facts, app info) and lets you tweak display properties live.

## Scenario 1: Suggestion Chip → Tool Call → Response

1. Open the app — the Playground page loads first.
2. You should see a welcome message ("Hello! I'm your AI assistant…") and four suggestion chips.
3. Tap **"What's the weather in Tokyo?"**.
4. **Expected:** The welcome panel and suggestion chips disappear. A user bubble appears, followed by a "⚙️ Calling GetCurrentWeather…" badge, a tool result, and then an assistant response describing Tokyo's weather.

## Scenario 2: Multi-Turn Conversation

1. After Scenario 1, type **"Now calculate 42 * 3 + 7"** in the input and press Send.
2. **Expected:** A new user bubble, a "⚙️ Calling calculate…" badge, the result ("133"), and an assistant explanation.
3. Type **"Tell me a random fact"** and send.
4. **Expected:** The fact tool is called and a fun fact is shown. The conversation now has three exchanges visible.

## Scenario 3: Settings Sidebar — Toggle Display Options

1. In the sidebar under **DISPLAY**, toggle **Show Timestamps** on.
2. **Expected:** Timestamp labels (e.g., "9:23 PM") appear under each message bubble.
3. Toggle **Show Tool Calls** off.
4. **Expected:** The "⚙️ Calling…" badges disappear from the conversation.
5. Toggle **Show Tool Results** off.
6. **Expected:** The tool result bubbles also disappear. Only user and assistant text remain.

## Scenario 4: Settings Sidebar — Adjust Styling

1. Drag the **Bubble Corner Radius** slider to 0.
2. **Expected:** Message bubbles become sharp rectangles. The slider label updates to "0".
3. Drag it to 24.
4. **Expected:** Bubbles become very rounded.
5. Drag the **Max Bubble Width** slider to 200.
6. **Expected:** Longer messages wrap to more lines since the bubble width is constrained.

## Scenario 5: Clear Chat

1. Tap the **🗑️ Clear** toolbar button.
2. **Expected:** All messages disappear. The welcome panel and suggestion chips reappear.

## Scenario 6: Narrow Window — Sidebar Auto-Hide

1. Resize the window to be narrow (< 700px wide).
2. **Expected:** The settings sidebar hides automatically, giving full width to the chat.
3. Resize wider again.
4. **Expected:** The sidebar reappears.
