# Haiku Generator

## Overview

A creative haiku generator with a carousel display panel. The AI creates haiku poems with both Japanese and English text, plus a custom theme color for each, demonstrating tool-based generative UI that builds a browseable gallery.

## Features Demonstrated

- `create_haiku` tool with `japanese_lines`, `english_lines`, and `theme_color` parameters
- Gallery/carousel navigation (◀ ▶) with position indicator for multiple haikus
- Dynamic background color per haiku using `Color.FromArgb()` with fallback to a predefined palette
- Split-pane layout: haiku display (left) with empty state, chat panel (right)
- JSON array deserialization for the 3-line haiku structure

## How to Use

1. Navigate to **Haiku Generator** from the app shell
2. The left panel shows an empty state: "🎋 Ask for a haiku to get started"
3. Ask the agent to write a haiku, e.g., "Write a haiku about the ocean"
4. Observe the haiku appear on the left with Japanese text, a white divider, and English translation
5. The panel background changes to the AI's chosen theme color
6. Request more haikus — navigation arrows (◀ ▶) appear when you have 2+ haikus
7. Browse your haiku collection with the prev/next buttons; note the "N / M" position label

## Expected Behavior

- The agent calls `create_haiku(japanese_lines, english_lines, theme_color)` where lines are JSON arrays of 3 strings
- The haiku is added to `_haikus` list and `_currentIndex` points to the newest
- Japanese lines render at 28pt white, English lines at 16pt white with 0.9 opacity, separated by a thin white divider
- The `HaikuPanel.BackgroundColor` changes to the theme color (or a fallback from `GradientColors` if parsing fails)
- Navigation is disabled at the boundaries (PrevButton disabled at index 0, NextButton disabled at last index)
- Each new haiku request appends to the collection without removing previous ones

## Key Code Patterns

- **Gallery state** — `List<HaikuData> _haikus` stores all generated haikus with `_currentIndex` tracking the visible one; navigation buttons adjust the index and call `RefreshHaikuUI()` (`HaikuPage.xaml.cs:12-13, 137-153`)
- **Color fallback** — `Color.FromArgb(theme_color)` is wrapped in try/catch; on failure, the code falls back to `GradientColors[_haikus.Count % GradientColors.Length]` (`HaikuPage.xaml.cs:63-69`)
- **Dynamic label generation** — `RefreshHaikuUI()` clears and rebuilds `JapaneseLines` and `EnglishLines` `VerticalStackLayout` children from the haiku data (`HaikuPage.xaml.cs:88-135`)
