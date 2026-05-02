# Accessibility Guide

This guide covers how to make Comet applications accessible to users who rely on
screen readers, keyboard navigation, and other assistive technologies. Comet
bridges to MAUI's accessibility infrastructure through its environment system,
providing fluent extension methods that map to platform-native accessibility
APIs on iOS, Android, macOS, and Windows.


## Semantic Properties

Comet provides semantic property extensions in `Helpers/ViewExtensions.cs` that
map directly to MAUI's `SemanticProperties`. These are the primary way to
communicate meaning to assistive technologies.

### SemanticDescription

Sets the accessible description that screen readers announce when the element
receives focus. This is the equivalent of `contentDescription` on Android and
`accessibilityLabel` on iOS.

```csharp
new Text("Submit")
	.SemanticDescription("Submit the registration form")
```

### SemanticHint

Provides additional context about what will happen when the user interacts with
the element. On iOS, VoiceOver reads this after the label. On Android, TalkBack
uses it as supplementary text.

```csharp
new Button("Delete")
	.SemanticDescription("Delete item")
	.SemanticHint("Double tap to permanently remove this item")
```

### SemanticHeadingLevel

Marks an element as a heading for document structure navigation. Screen reader
users can jump between headings to navigate content quickly.

Available levels are defined by `SemanticHeadingLevel`:

- `SemanticHeadingLevel.None` -- not a heading (default)
- `SemanticHeadingLevel.Level1` through `SemanticHeadingLevel.Level9`

```csharp
new VStack
{
	new Text("Account Settings")
		.SemanticHeadingLevel(SemanticHeadingLevel.Level1),

	new Text("Profile")
		.SemanticHeadingLevel(SemanticHeadingLevel.Level2),

	new Text("Your display name and photo")
		.SemanticDescription("Profile section description"),

	new Text("Security")
		.SemanticHeadingLevel(SemanticHeadingLevel.Level2),
}
```

### Chaining

All semantic extensions return the view instance for fluent chaining:

```csharp
new Button("Save")
	.SemanticDescription("Save changes")
	.SemanticHint("Double tap to save your changes")
	.SemanticHeadingLevel(SemanticHeadingLevel.None)
```


## Automation Properties

Comet provides a second layer of accessibility extensions in
`Controls/SemanticExtensions.cs` through the `AutomationExtensions` class.
These map to MAUI's `AutomationProperties` and are used by both assistive
technologies and UI testing frameworks.

### AutomationName

Sets the accessible name announced by screen readers. This takes priority over
the control's visible text content.

```csharp
new Image("profile.png")
	.AutomationName("User profile photo")
```

### AutomationHelpText

Provides help text for the element, used by assistive technologies to give
additional guidance.

```csharp
new TextField("")
	.AutomationName("Email address")
	.AutomationHelpText("Enter the email address associated with your account")
```

### IsInAccessibleTree

Controls whether the element is visible to assistive technologies. Set to
`false` to hide decorative elements that add no informational value.

```csharp
// Decorative divider -- hide from screen readers
new View()
	.Frame(height: 1)
	.Background(Colors.LightGray)
	.IsInAccessibleTree(false)

// Informational icon -- include in accessibility tree
new Image("warning.png")
	.AutomationName("Warning")
	.IsInAccessibleTree(true)
```

### Reading Automation Properties

You can read back the values set on a view for testing or inspection:

```csharp
var button = new Button("OK").AutomationName("Confirm");
string name = button.GetAutomationName();   // "Confirm"
string help = button.GetAutomationHelpText(); // null
bool? inTree = button.GetIsInAccessibleTree(); // null (not explicitly set)
```


## AutomationId

The `AutomationId` property serves dual purposes: it provides a stable
identifier for UI test automation frameworks (Appium, XCUITest) and is also
used as the platform accessibility identifier.

```csharp
new Button("Login")
	.AutomationId("login-button")
```

On iOS and macOS, this sets `UIView.AccessibilityIdentifier`. On Android, it
sets the content description fallback. Every Comet view also receives an
auto-generated stable `View.Id` that is used as a fallback when no explicit
`AutomationId` is set, ensuring all views are discoverable by automation tools.

### Reading and Writing AutomationId

```csharp
var view = new Text("Hello");
view.SetAutomationId("greeting-label");
string id = view.GetAutomationId(); // "greeting-label"
```

Setting `AutomationId` also updates the internal `AccessibilityId` property,
keeping both values synchronized.


## IsReadOnly

Marks a view as read-only content, which assistive technologies can use to
communicate that the element displays information but does not accept input.

```csharp
new Text(()=> $"Total: {total.Value}")
	.IsReadOnly(true)
```


## Platform Bridging

When a Comet view connects to its handler, the framework automatically bridges
accessibility properties to the native platform layer through
`ApplyAutomationProperties()` and handler mapper callbacks.

### iOS and macOS (UIKit / AppKit)

- `AutomationId` maps to `AccessibilityIdentifier`
- Views with an `AutomationId` or gesture recognizers get
  `IsAccessibilityElement = true`
- `IsVisible` maps to `Hidden`
- `IsEnabled` maps to `UserInteractionEnabled`

### Android

- `AutomationId` maps to `ContentDescription`
- `IsEnabled` maps to `Enabled`
- `IsVisible` maps to `ViewStates.Visible` / `ViewStates.Gone`
- Input transparency maps to `Clickable`

### Windows (WinUI)

- `AutomationId` maps to `AutomationProperties.AutomationId`
- Standard MAUI accessibility property mapping applies


## View State and Accessibility

Several view properties affect how assistive technologies interact with
elements. These are set through Comet's fluent API and automatically bridged
to the platform.

### IsEnabled

Disabled views are announced differently by screen readers and typically cannot
receive focus.

```csharp
var isFormValid = new Signal<bool>(false);

new Button("Submit")
	.IsEnabled(() => isFormValid.Value)
	.SemanticDescription("Submit form")
	.SemanticHint("Button is disabled until all fields are filled")
```

### IsVisible

Hidden views are removed from the accessibility tree entirely.

```csharp
var showError = new Signal<bool>(false);

new Text("Invalid email address")
	.Color(Colors.Red)
	.IsVisible(() => showError.Value)
	.SemanticDescription("Validation error message")
```

### View State Properties

The `View` class exposes several properties relevant to accessibility:

| Property | Type | Description |
|----------|------|-------------|
| `AutomationId` | `string` | Stable identifier for automation and accessibility |
| `AccessibilityId` | `string` | Internal accessibility identifier |
| `IsEnabled` | `bool` | Whether the view accepts interaction |
| `IsVisible` | `bool` | Whether the view is rendered |
| `Hidden` | `bool` | Inverse of `IsVisible` |
| `Disabled` | `bool` | Inverse of `IsEnabled` |
| `Semantics` | `Semantics` | MAUI Semantics object for platform bridging |


## Color Contrast

While Comet does not include a built-in WCAG contrast checker, you can use the
design token system to establish accessible color pairings. Define token values
that meet minimum contrast ratios (4.5:1 for normal text, 3:1 for large text
per WCAG 2.1 AA).

```csharp
// Define accessible color tokens
public static class AccessibleColors
{
	public static readonly Color TextOnLight = Color.FromArgb("#1A1A1A");
	public static readonly Color TextOnDark = Color.FromArgb("#F5F5F5");
	public static readonly Color ErrorRed = Color.FromArgb("#D32F2F");
}
```

When building themes, verify contrast ratios between your foreground and
background token pairs using external tools such as the WebAIM Contrast Checker
or the Accessibility Insights toolkit.


## Font Scaling and Dynamic Type

MAUI supports system font scaling on all platforms. When users increase the
system font size (Dynamic Type on iOS, font scale on Android, text scaling on
Windows), text controls automatically respect these settings.

In Comet, avoid setting fixed font sizes in device-independent units where
possible. Instead, use relative sizing or let the platform defaults apply:

```csharp
// Preferred: use typography tokens that scale with system settings
new Text("Body text")
	.FontSize(16) // Base size, scaled by platform

// For headings, use semantic heading levels alongside size
new Text("Section Title")
	.FontSize(24)
	.SemanticHeadingLevel(SemanticHeadingLevel.Level2)
```

Platform font scaling works automatically. Comet does not override or interfere
with the system scaling factor.


## Keyboard Navigation

On Windows and macOS, keyboard navigation is critical for accessibility. MAUI
provides built-in tab order and focus management that Comet views inherit
through the handler layer.

### Tab Order

Views participate in the platform tab order by default. Users can press Tab to
move between interactive controls (buttons, text fields, toggles, sliders).

### Focus Management

Interactive controls receive focus through platform-native mechanisms. The
`IsEnabled` property controls whether a view can receive focus -- disabled views
are skipped in the tab order.

```csharp
new VStack
{
	new TextField("")
		.AutomationName("First name"),
	new TextField("")
		.AutomationName("Last name"),
	new Button("Submit")
		.SemanticDescription("Submit the form"),
}
```

In this layout, Tab moves focus through the text fields and button in order.


## Testing Accessibility

### Unit Tests

The test suite in `tests/Comet.Tests/AccessibilityTests.cs` provides 13 tests
covering semantic properties, automation IDs, heading levels, view state
synchronization, and handler bridging. Use these as reference patterns. For
the full testing guide, see [Testing Guide](testing.md).

```csharp
[Fact]
public void SemanticDescriptionSetsDescription()
{
	var text = new Text("Hello").SemanticDescription("Greeting label");
	var semantics = text.GetEnvironment<Semantics>(
		EnvironmentKeys.View.Semantics
	);
	Assert.Equal("Greeting label", semantics?.Description);
}

[Fact]
public void AutomationIdViaIView()
{
	var view = new Text("Test");
	view.SetAutomationId("test-label");
	var iview = (IView)view;
	Assert.Equal("test-label", iview.AutomationId);
}

[Fact]
public void HeadingLevels()
{
	var h1 = new Text("Title")
		.SemanticHeadingLevel(SemanticHeadingLevel.Level1);
	var h2 = new Text("Subtitle")
		.SemanticHeadingLevel(SemanticHeadingLevel.Level2);
	var plain = new Text("Body")
		.SemanticHeadingLevel(SemanticHeadingLevel.None);
	// Verify via environment lookup
}
```

### Platform Testing

For end-to-end accessibility validation:

- **iOS**: Use the Accessibility Inspector in Xcode to verify VoiceOver labels,
  traits, and hierarchy.
- **Android**: Enable TalkBack and navigate the app. Use the Accessibility
  Scanner for automated checks.
- **Windows**: Use Narrator and the Accessibility Insights for Windows tool.
- **macOS**: Enable VoiceOver (Cmd+F5) and verify navigation and announcements.

### Appium Testing

Set `AutomationId` on key controls to make them discoverable by Appium:

```csharp
new Button("Login")
	.AutomationId("login-button")
	.SemanticDescription("Log in to your account")
```

Then locate the element in Appium by its automation ID:

```python
login_button = driver.find_element(AppiumBy.ACCESSIBILITY_ID, "login-button")
```


## Accessibility Checklist

When building a Comet view, verify the following:

1. Every interactive control has a `SemanticDescription` or `AutomationName`.
2. Decorative images and layout spacers use `.IsInAccessibleTree(false)`.
3. Page sections use `SemanticHeadingLevel` for structural navigation.
4. Dynamic content updates are reflected in accessible labels (use lambda
   bindings: `() => $"Count: {count.Value}"`).
5. Color is not the only means of conveying information. Pair color indicators
   with text labels or icons.
6. Text meets WCAG 2.1 AA contrast ratios against its background.
7. All form fields have accessible names that describe their purpose.
8. Error messages are programmatically associated with their fields.
9. The tab order follows a logical reading sequence on Windows and macOS.
10. The app is usable with system font scaling set to the maximum level.


## See Also

- [Control Catalog](controls.md) -- semantic property extensions available on
  every control, including SemanticDescription and AutomationName.
- [Styling and Theming](styling.md) -- color contrast considerations when
  defining theme tokens for accessible color pairings.
- [Form Handling](forms.md) -- making form fields accessible with labels, help
  text, and error message associations.
- [Platform-Specific Guides](platform-guides.md) -- platform screen reader
  details for VoiceOver, TalkBack, and Narrator.
