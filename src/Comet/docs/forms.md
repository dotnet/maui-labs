# Form Handling and Validation Guide

Comet provides a set of form controls built on the source generator and
reactive binding system. Each control exposes `PropertySubscription<T>`
properties for two-way data binding and callback extension methods for
responding to value changes.


## Form Controls

### Generated vs. Handwritten

Most form controls are generated from MAUI interfaces via `[CometGenerate]`
attributes. Two controls are handwritten due to their complexity.

| Control | Source | MAUI Interface | Purpose |
|---------|--------|----------------|---------|
| `TextField` | Generated | `IEntry` | Single-line text input |
| `SecureField` | Generated | `IEntry` | Password input (masked) |
| `TextEditor` | Generated | `IEditor` | Multi-line text input |
| `Slider` | Generated | `ISlider` | Numeric range selection |
| `Toggle` | Generated | `ISwitch` | Boolean on/off switch |
| `CheckBox` | Generated | `ICheckBox` | Boolean checkbox |
| `DatePicker` | Generated | `IDatePicker` | Date selection |
| `TimePicker` | Generated | `ITimePicker` | Time selection |
| `SearchBar` | Generated | `ISearchBar` | Search text input |
| `Stepper` | Generated | `IStepper` | Increment/decrement numeric value |
| `ProgressBar` | Generated | `IProgress` | Read-only progress indicator |
| `Picker` | Handwritten | `IPicker` | Drop-down selection list |
| `RadioButton` | Handwritten | `IRadioButton` | Single-choice within a group |


### TextField -- Single-Line Text Input

Constructors:

```csharp
new TextField()
new TextField("initial text")
new TextField("text", "placeholder hint")
new TextField("text", "placeholder", () => OnCompleted())
new TextField(() => signal.Value)
new TextField(() => signal.Value, "placeholder")
```

Key properties:

- `PropertySubscription<string> Text` -- current input value
- `PropertySubscription<string> Placeholder` -- hint text

Extension methods:

```csharp
var field = new TextField("", "Enter name")
	.OnTextChanged(text => Console.WriteLine($"Changed: {text}"))
	.PlaceholderColor(Colors.Gray)
	.Keyboard(Microsoft.Maui.Keyboard.Email)
	.ReturnType(ReturnType.Next)
	.IsPassword(false);
```

Reading and writing the value:

```csharp
string current = field.Text?.CurrentValue;
field.Text?.Set("new value");
```


### SecureField -- Password Input

Same constructor signatures as `TextField`, but `IsPassword` is permanently
`true`. The input is always masked:

```csharp
new SecureField("", "Password")
	.OnTextChanged(text => ValidatePassword(text))
	.PlaceholderColor(Colors.Gray)
```


### TextEditor -- Multi-Line Text Input

```csharp
new TextEditor()
new TextEditor("initial content")
new TextEditor(() => notes.Value)
```

Extension methods:

```csharp
new TextEditor(() => notes.Value)
	.OnTextChanged(text => AutoSave(text))
	.FontSize(16)
	.Color(Colors.Black)
```


### Slider -- Numeric Range

```csharp
new Slider(0.5)                         // value only (0-1 range)
new Slider(50d, 0d, 100d)              // value, min, max
new Slider(() => volume.Value)          // reactive (0-1 range)
new Slider(() => volume.Value, 0d, 100d) // reactive with range
```

Properties: `Value`, `Minimum`, `Maximum` (all `PropertySubscription<double>`).

```csharp
new Slider(50d, 0d, 100d)
	.OnValueChanged(v => Console.WriteLine($"Value: {v}"))
	.MinimumTrackColor(Colors.Green)
	.MaximumTrackColor(Colors.LightGray)
	.ThumbColor(Colors.DarkGreen)
```


### Toggle -- Boolean Switch

```csharp
new Toggle()               // default: off
new Toggle(true)           // initial state
new Toggle(() => flag.Value)  // reactive
```

Property: `Value` (`PropertySubscription<bool>`).

```csharp
new Toggle(() => notifications.Value)
	.OnToggled(isOn => notifications.Value = isOn)
	.OnColor(Colors.Green)
	.ThumbColor(Colors.White)
```


### CheckBox, DatePicker, TimePicker, Stepper, SearchBar

These generated controls follow the same constructor patterns as above:

| Control | Constructors | Key Properties | Callback |
|---------|-------------|----------------|----------|
| `CheckBox` | `()`, `(bool)`, `(Func<bool>)` | `IsChecked` (bool) | `.OnCheckedChanged(Action<bool>)` |
| `DatePicker` | `()`, `(DateTime?)`, `(Func<DateTime?>)` | `Date`, `MinimumDate`, `MaximumDate` (DateTime?) | `.Format(string)`, `.TextColor(Color)` |
| `TimePicker` | `()`, `(TimeSpan?)`, `(Func<TimeSpan?>)` | `Time` (TimeSpan?) | `.TextColor(Color)` |
| `Stepper` | `()`, `(double, double, double, double)` | `Value`, `Minimum`, `Maximum`, `Interval` (double) | `.OnValueChanged(Action<double>)` |
| `SearchBar` | `()`, `(string)`, `(Func<string>)` | `Text` (string) | `.OnTextChanged(Action<string>)` |

```csharp
new CheckBox(() => termsAccepted.Value)
	.OnCheckedChanged(v => termsAccepted.Value = v);

new DatePicker(DateTime.Today).Format("MM/dd/yyyy");

new Stepper(1d, 1d, 10d, 1d)
	.OnValueChanged(v => quantity.Value = (int)v);
```

The `Picker` is handwritten and accepts string items:

```csharp
new Picker(0, "Apple", "Banana", "Cherry")  // selectedIndex, items
new Picker("Apple", "Banana", "Cherry")     // defaults to index 0
```

Properties:

- `PropertySubscription<IList<string>> Items`
- `PropertySubscription<int> SelectedIndex`
- `PropertySubscription<string> Title`

```csharp
new Picker(0, "Small", "Medium", "Large")
	.OnSelectedIndexChanged(index =>
		Console.WriteLine($"Selected: {index}"))
```

Updating items dynamically:

```csharp
picker.Items?.Set(new List<string> { "New A", "New B" });
picker.SelectedIndex?.Set(0);
```


### RadioButton

The `RadioButton` is handwritten with group support:

```csharp
new RadioButton(label: "Option A", selected: true, onClick: () => Choose("A"))
{
	GroupName = "choices",
	Value = "A"
}
```

Reactive variant:

```csharp
new RadioButton(
	label: () => labelSignal.Value,
	selected: () => isSelected.Value,
	onClick: () => HandleSelection())
```

Properties: `Label`, `Selected` (`PropertySubscription` types), `GroupName`
(`string`), `Value` (`object`).

Event: `CheckedChanged` (`EventHandler<CheckedChangedEventArgs>`).


### Stepper and SearchBar

See the table above for constructors and properties.


## Binding Patterns

### Static Values

Pass a value directly. The control displays it but changes require calling
`Set()`:

```csharp
var field = new TextField("Hello");
// Later:
field.Text?.Set("Updated");
```

### Reactive Functions

Pass a lambda that reads signals. The control re-evaluates the lambda when
dependencies change. This creates a one-way binding from signal to control:

```csharp
readonly Signal<string> name = new("Alice");

// Control text updates when name.Value changes
new TextField(() => name.Value, "Name")
```

### Bidirectional Signal Binding

For full two-way binding, pass a `Signal<T>` directly using the factory
methods from `SignalExtensions`. For a comprehensive treatment of binding
patterns, see the [Reactive State Guide](reactive-state-guide.md). User input
writes back to the signal, and signal changes update the control:

```csharp
readonly Signal<string> username = new("");

// Two-way: user edits update username, external changes update field
TextField(username, "Username")
```

```csharp
readonly Signal<double> volume = new(50);

// Two-way: slider drag updates volume, volume changes move slider
Slider(volume, 0d, 100d)
```

```csharp
readonly Signal<bool> darkMode = new(false);

// Two-way: toggle flip updates darkMode, darkMode changes flip toggle
Toggle(darkMode)
```

These factory methods are available via `using static Comet.CometControls;`
and generate `PropertySubscription<T>` instances with `IsBidirectional = true`.


## Building a Form Layout

### Basic Form with VStack

```csharp
using Comet;
using Comet.Styles;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

class RegistrationForm : View
{
	readonly Signal<string> name = new("");
	readonly Signal<string> email = new("");
	readonly Signal<string> password = new("");
	readonly Signal<bool> termsAccepted = new(false);

	[Body]
	View body() => new VStack(spacing: 12)
	{
		new Text("Create Account").FontSize(24),
		TextField(name, "Full Name"),
		TextField(email, "Email")
			.Keyboard(Microsoft.Maui.Keyboard.Email),
		new SecureField(() => password.Value, "Password"),
		new HStack(spacing: 8)
		{
			new CheckBox(() => termsAccepted.Value)
				.OnCheckedChanged(v => termsAccepted.Value = v),
			new Text("I accept the terms")
		},
		new Button("Register", () =>
		{
			// Peek() reads without creating dependencies
			var data = (name.Peek(), email.Peek(), password.Peek());
		})
	}
	.Padding(new Thickness(24));
}
```

Use `Peek()` in the submit handler to read values without creating reactive
dependencies.


### Labeled Field Helper

```csharp
static View LabeledField(string label, View field) => new VStack(spacing: 4)
{
	new Text(label).FontSize(12).Color(Colors.Gray),
	field
};

[Body]
View body() => new VStack(spacing: 16)
{
	LabeledField("Email", TextField(email, "you@example.com")),
	LabeledField("Phone", TextField(phone, "+1 (555) 000-0000")
		.Keyboard(Microsoft.Maui.Keyboard.Telephone))
};
```


## Validation Patterns

### Synchronous Validation

Validate on every text change:

```csharp
readonly Signal<string> email = new("");
readonly Signal<string> emailError = new("");

[Body]
View body() => new VStack(spacing: 8)
{
	TextField(email, "Email")
		.OnTextChanged(text =>
		{
			email.Value = text;
			emailError.Value = IsValidEmail(text)
				? ""
				: "Enter a valid email address";
		}),

	new Text(() => emailError.Value)
		.Color(Colors.Red)
		.FontSize(12)
};

static bool IsValidEmail(string value) =>
	!string.IsNullOrEmpty(value) && value.Contains('@');
```

### Computed Validation

Use `Computed<T>` for derived validation state:

```csharp
readonly Signal<string> password = new("");
readonly Signal<string> confirmPassword = new("");

readonly Computed<bool> passwordsMatch;
readonly Computed<string> passwordError;

public MyForm()
{
	passwordsMatch = new Computed<bool>(
		() => password.Value == confirmPassword.Value);

	passwordError = new Computed<string>(() =>
	{
		if (string.IsNullOrEmpty(password.Value))
			return "Password is required";
		if (password.Value.Length < 8)
			return "Password must be at least 8 characters";
		if (!passwordsMatch.Value)
			return "Passwords do not match";
		return "";
	});
}

[Body]
View body() => new VStack(spacing: 8)
{
	new SecureField(() => password.Value, "Password")
		.OnTextChanged(t => password.Value = t),
	new SecureField(() => confirmPassword.Value, "Confirm Password")
		.OnTextChanged(t => confirmPassword.Value = t),

	new Text(() => passwordError.Value)
		.Color(Colors.Red)
		.FontSize(12)
};
```

The `Computed<string>` re-evaluates only when `password` or
`confirmPassword` change. The error message updates automatically.


### Cross-Field and Asynchronous Validation

For server-side checks, use an `Effect` that re-runs when its dependency
changes. The scheduler coalesces rapid changes:

```csharp
readonly Signal<string> username = new("");
readonly Signal<string> usernameError = new("");

public MyForm()
{
	new Effect(async () =>
	{
		var name = username.Value;
		if (string.IsNullOrEmpty(name)) { usernameError.Value = ""; return; }
		var available = await CheckUsernameAvailability(name);
		usernameError.Value = available ? "" : "Username is taken";
	});
}
```


## Form Submission

### Gathering Values

Use `Peek()` to read all form values without creating dependencies:

```csharp
void OnSubmit()
{
	var first = firstName.Peek();
	var last = lastName.Peek();
	var mail = email.Peek();
	// Validate and submit...
}
```


### Disabling the Submit Button

Derive the enabled state from validation:

```csharp
readonly Computed<bool> isFormValid;

public MyForm()
{
	isFormValid = new Computed<bool>(() =>
		!string.IsNullOrEmpty(firstName.Value) &&
		!string.IsNullOrEmpty(email.Value) &&
		IsValidEmail(email.Value) &&
		termsAccepted.Value);
}

[Body]
View body() => new VStack(spacing: 12)
{
	// ... form fields ...

	new Button("Submit", OnSubmit)
		.Background(() => isFormValid.Value ? Colors.Blue : Colors.Gray)
};
```


## Multi-Step Forms

Use a `Signal<int>` to track the current step. Each step method returns a
`View`. State is preserved across steps because `Signal<T>` instances live
on the view, not inside the step methods:

```csharp
readonly Signal<int> currentStep = new(0);
readonly Signal<string> name = new("");
readonly Signal<string> email = new("");

[Body]
View body()
{
	var step = currentStep.Peek();
	return new VStack(spacing: 16)
	{
		new Text(() => $"Step {currentStep.Value + 1} of 2"),
		step == 0
			? (View)TextField(name, "Full Name")
			: TextField(email, "Email"),
		new HStack(spacing: 12)
		{
			step > 0
				? new Button("Back", () => currentStep.Value--)
				: null,
			step < 1
				? new Button("Next", () => currentStep.Value++)
				: new Button("Finish", () => { /* submit */ })
		}
	};
}
```


## Callback Reference

All form-related callback extension methods:

| Method | Control | Parameter |
|--------|---------|-----------|
| `OnTextChanged(Action<string>)` | TextField, TextEditor, SearchBar | Changed text |
| `OnValueChanged(Action<double>)` | Slider | New slider value |
| `OnValueChanged(Action<double>)` | Stepper | New stepper value |
| `OnToggled(Action<bool>)` | Toggle | New on/off state |
| `OnCheckedChanged(Action<bool>)` | CheckBox | New checked state |
| `OnSelectedIndexChanged(Action<int>)` | Picker | New selected index |

All callback methods return the control instance for fluent chaining.


## Styling Form Controls

Form controls support the same fluent styling as all Comet views. Use
`ControlStyle<T>` for consistent form field styling across the app via
the theme system (see [Styling and Theming](styling.md)).


## See Also

- [Control Catalog](controls.md) -- full API reference for every form control
  including constructors, properties, and handler mappings.
- [Reactive State Guide](reactive-state-guide.md) -- comprehensive guide to
  two-way binding, Signal, Computed, and dependency tracking patterns.
- [Styling and Theming](styling.md) -- design tokens and ControlStyle for
  consistent form field appearance across themes.
- [Accessibility Guide](accessibility.md) -- making form fields accessible with
  semantic labels, help text, and screen reader support.
