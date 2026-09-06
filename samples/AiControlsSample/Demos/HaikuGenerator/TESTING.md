# Haiku Generator — Testing Guide

This demo shows a creative gallery UI: the AI generates haikus with Japanese/English text and a theme color, building a browseable collection.

## Scenario 1: Generate First Haiku

1. Navigate to **Haiku Generator** from the flyout menu.
2. You should see "🎋 Ask for a haiku to get started" on the left and a chat panel on the right.
3. Type **"Write a haiku about cherry blossoms"** and send.
4. **Expected:** The empty state disappears. A haiku appears on the left with Japanese text (large, centered), a divider line, and the English translation below. The panel background changes to a theme color chosen by the agent. The agent comments on the haiku's meaning in the chat.

## Scenario 2: Gallery Navigation

1. Type **"Write a haiku about the moon"** and send.
2. **Expected:** The haiku panel updates to show the new moon haiku. A navigation bar appears at the bottom with **◀** and **▶** buttons and a "2 / 2" position label.
3. Click **◀** (previous).
4. **Expected:** The cherry blossom haiku reappears, and the background color changes to its original theme color. Position shows "1 / 2".
5. Click **▶** (next).
6. **Expected:** Returns to the moon haiku with its theme color. Position shows "2 / 2".

## Scenario 3: Build a Collection

1. Generate three more haikus on different topics (e.g., "ocean", "winter", "coffee").
2. **Expected:** After each generation, the new haiku is shown and the position counter updates (e.g., "5 / 5"). You can navigate through all five haikus with ◀ and ▶. Each haiku retains its unique theme color.

## Scenario 4: Boundary Navigation

1. Navigate to the first haiku (position 1).
2. Click **◀** again.
3. **Expected:** Nothing happens — you stay at position 1. The button may be disabled or simply not navigate past the boundary.
4. Navigate to the last haiku and click **▶**.
5. **Expected:** Same behavior — you stay at the last position.
