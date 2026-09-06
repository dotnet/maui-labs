# Tool Rendering — Testing Guide

This demo shows custom inline rendering for specific tool results. Weather queries render as styled cards; other tools use the generic text template.

## Scenario 1: Weather Card Rendering

1. Navigate to **Tool Rendering** from the flyout menu.
2. Type **"What's the weather in Paris?"** and send.
3. **Expected:** A user bubble appears, then a "⚙️ Calling GetCurrentWeather…" badge, then a **styled weather card** (not plain text) showing: city name, temperature in °C, conditions, humidity %, wind speed km/h, and feels-like temperature. The assistant then describes the weather in natural language.

## Scenario 2: Generic Tool Result (Math)

1. Type **"Calculate 256 / 8"** and send.
2. **Expected:** The tool result appears as **plain text** in a generic result bubble (not a weather card). This confirms the template priority system works — `WeatherResultTemplate` only matches `GetCurrentWeather`.

## Scenario 3: Compare Custom vs Generic

1. Ask for weather in two cities back-to-back: **"Weather in Tokyo"** then **"Weather in London"**.
2. **Expected:** Both render as weather cards with different data. The cards are visually identical in structure but show different temperatures, conditions, etc.
3. Then ask **"Tell me a random fact"**.
4. **Expected:** The fact result uses the generic function result template, visually distinct from the weather cards.

## Scenario 4: Multiple Tool Calls in One Conversation

1. Send: **"What's the weather in Sydney and also calculate 99 * 42"**.
2. **Expected:** The agent may call both tools. The weather result should render as a card, and the math result as plain text, both in the same conversation thread.
