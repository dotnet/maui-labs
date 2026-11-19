---
name: maui-accessibility
description: >-
  Improve MAUI accessibility. USE FOR: semantic labels, hints, headings, screen-reader focus/announcements, AutomationProperties, touch targets, decorative content, TalkBack/VoiceOver/Narrator checks. DO NOT USE FOR: general layout, automation-only tests, performance.
---

# MAUI Accessibility

Use this skill to make MAUI UI understandable and operable through assistive
technologies. Accessibility metadata should be added while building UI, not as a
final cosmetic pass.

## Workflow

1. Identify the user task and target controls.
2. Add accessible names with `SemanticProperties.Description`.
3. Add action guidance with `SemanticProperties.Hint` when the control's result
   is not obvious.
4. Add `SemanticProperties.HeadingLevel` for page and section headings.
5. Hide decorative or duplicate content from the accessibility tree.
6. Use `SemanticScreenReader.Announce` for important dynamic changes.
7. Move focus intentionally after navigation or validation when it helps the user.
8. Keep `AutomationId` for testing, but do not treat it as the accessible label.
9. Verify with platform screen readers when possible.

## Common Patterns

### Icon-only button

```xml
<ImageButton
    AutomationId="save-button"
    Source="save.png"
    SemanticProperties.Description="Save"
    SemanticProperties.Hint="Saves the current form" />
```

### Heading

```xml
<Label
    Text="Account settings"
    SemanticProperties.HeadingLevel="Level1"
    Style="{StaticResource TitleStyle}" />
```

### Decorative image

```xml
<Image
    Source="card_background.png"
    AutomationProperties.IsInAccessibleTree="False" />
```

### Dynamic announcement

```csharp
SemanticScreenReader.Announce("Profile saved");
```

## Accessibility Checklist

- Icon-only controls have descriptions and, when useful, hints.
- Page and major section headings have heading levels.
- Decorative images and duplicated labels are hidden from the accessibility tree.
- Dynamic validation, save, and navigation outcomes are announced when important.
- Touch targets are large enough and not crowded.
- Color is not the only way information is conveyed.
- `AutomationId` values exist for test automation but are not used as a substitute
  for accessible names.

## Platform Notes

- Android TalkBack, iOS VoiceOver, macOS VoiceOver, and Windows Narrator differ in
  how aggressively they read hints and grouped content; verify on the target
  platform for critical flows.
- For validation errors, move semantic focus to the summary or first invalid
  field and announce the error.
- For CollectionView rows, ensure the row exposes a meaningful label instead of
  reading every decorative child.

## MAUI-Specific Best Practices

- **Label/Text redundancy**: Do NOT add `SemanticProperties.Description` to a
  `Label` when its `Text` property already serves as the accessible name.
  Only add it when the visible text is absent or insufficient.
- **Announcements**: Call `SemanticScreenReader.Announce` for important outcomes
  (save success, validation summary) but NOT for every state change. Provide
  guidance on avoiding over-announcing by batching related errors.
- **CollectionView rows**: Ensure each row exposes a meaningful composite label
  rather than reading every child view separately. Use `SemanticProperties`
  on the container or a specific child, not all children.
- **Multiple validation errors**: Announce a summary count ("2 errors") after
  the user submits, not a separate announcement per field.

## Anti-Patterns

- Do not rely on placeholder text as the only field label.
- Do not use `AutomationId` as the accessible name.
- Do not hide real interactive content from the accessibility tree to make screen
  reader output shorter.
- Do not announce every small UI update; reserve announcements for meaningful
  state changes.
