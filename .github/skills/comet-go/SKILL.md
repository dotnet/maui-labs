---
name: comet-go
description: Write and edit Comet Go single-file apps (.cs files using the Comet MVU framework for .NET MAUI). Use when writing, editing, or debugging Comet Go apps, or when the user mentions "maui go", "comet go", or is working with .cs files that import Comet.
---

# Comet Go — Single-File App Development

Comet Go is a single-file app experience for .NET MAUI using the Comet MVU framework.
The user writes ONE `.cs` file, runs `maui go`, and it live-reloads on their device.

## How It Works

1. User writes a single `.cs` file with a `MainPage : View` class
2. `maui go` starts GoDevServer → Roslyn compiles to DLL (OutputKind.DynamicallyLinkedLibrary)
3. DLL is sent over WebSocket to the companion app
4. Companion app loads via `Assembly.Load` → `MetadataUpdater.ApplyUpdate` for hot reload
5. Scaffold new apps with: `maui go create <AppName>`

## Required Imports

Every Comet Go file MUST start with:

```csharp
#:package Comet

using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;
```

- `using static Comet.CometControls;` enables `VStack(...)`, `Text(...)`, `Button(...)` etc. as top-level functions
- Add `using System;` if you need `Math`, `Convert`, `DateTime`, or `TimeSpan` directly
- `Action` and `Func<T>` resolve without `using System;` via implicit usings

## App Structure

```csharp
namespace MyApp;

public class MainPage : View
{
    readonly Reactive<int> count = new(0);

    [Body]
    View body() =>
        VStack(spacing: 16f,
            Text(() => $"Count: {count.Value}"),
            Button("Tap", () => count.Value++)
        );
}
```

## CRITICAL: VStack/HStack Spacing Parameter

`VStack(0, ...)` is **ambiguous** between `VStack(float?, params View[])` and `VStack(LayoutAlignment, params View[])`.

- ✅ `VStack(spacing: 0f, ...)` — named parameter (clearest)
- ✅ `VStack(16f, ...)` — float literal with `f` suffix
- ❌ `VStack(0, ...)` — CS0121 ambiguous call
- ❌ `VStack(16, ...)` — may be ambiguous

Always use `f` suffix or `spacing:` named parameter.

## Available Controls (via `using static Comet.CometControls`)

### Layout
```csharp
VStack(float? spacing, params View[] children)     // vertical stack
HStack(float? spacing, params View[] children)     // horizontal stack
ZStack(params View[] children)                      // overlay stack
Grid(object[] columns, object[] rows, params View[] children)
ScrollView(View content)
Border(View content)
Spacer()                                            // flexible space
```

### Display & Input
```csharp
// Text display
Text("static text")
Text(() => $"dynamic {reactive.Value}")   // auto-updates when reactive changes
Text(() => reactive.Value)                // simple reactive binding

// Buttons — MUST use lambda, not method group
Button("label", () => DoSomething())      // ✅ lambda wrapper
Button("label", DoSomething)              // ❌ CS1503 — method groups don't work

// Text input — requires Signal<string>, NOT Reactive<string>
TextField(signal, "placeholder")          // two-way binding with Signal<string>
SecureField(signal, "Password")

// Toggle / Switch
Toggle(reactiveBool)

// Slider
Slider(value: 0.5, minimum: 0, maximum: 1)

// Image
Image("https://example.com/image.png")

// Picker
Picker(0, "Option A", "Option B", "Option C")

// Others: ProgressBar, ActivityIndicator, DatePicker, TimePicker, Stepper
```

### Grid Layout
```csharp
Grid(
    columns: new object[] { "*", "*", "*", "*" },  // 4 equal columns
    rows: new object[] { 70, 70, 70 },              // 3 rows @ 70px
    Button("1", () => {}).Cell(row: 0, column: 0),
    Button("2", () => {}).Cell(row: 0, column: 1),
    Button("wide", () => {}).Cell(row: 1, column: 0, colSpan: 2)
)
.ColumnSpacing(10).RowSpacing(10)
```

Column/row definitions: `"*"` (star), `"2*"` (weighted), `"Auto"`, or integer pixels.

## Fluent Styling API

Chain these after any control:
```csharp
// Typography
.FontSize(24)
.FontWeight(FontWeight.Bold)              // Bold, Medium, Semibold, Regular, Light, Heavy, Thin
.Color(Colors.White)                       // text/foreground color
.HorizontalTextAlignment(TextAlignment.End)

// Background & shape
.Background(new SolidPaint(myColor))       // use SolidPaint for Color values
.Background(Colors.Blue)                   // convenience overload
.CornerRadius(12)                          // ⚠️ BUTTON ONLY — does NOT work on VStack/HStack/etc
.RoundedBorder(radius: 12, color: Colors.Gray, width: 1)  // rounded corners on ANY view

// Layout & spacing
.Frame(width: 100, height: 50)             // fixed size
.Padding(new Thickness(16))
.Margin(new Thickness(8))
.FillHorizontal()                          // expand to fill horizontal space
.FillVertical()                            // expand to fill vertical space
.IgnoreSafeArea()                          // extend past safe area (root layout)

// Other
.Alignment(Alignment.Center)
.Opacity(0.8)
.IsVisible(true)
.AutomationId("my-button")                 // accessibility / test identifier
```

> ⚠️ **`.CornerRadius()` works on Button and Border but NOT on layout views (VStack, HStack, TextField, etc.).** Using it on unsupported views is silently ignored or crashes. Use `.RoundedBorder()` instead for rounded corners on containers and other views.

## Reactive State: Reactive<T> vs Signal<T>

### Reactive<T> — Display binding (read in UI, write in code)
```csharp
readonly Reactive<int> count = new(0);
readonly Reactive<string> display = new("0");

// Read in reactive lambda — UI auto-updates when .Value changes
Text(() => $"Count: {count.Value}")

// Write to trigger UI update
Button("Add", () => count.Value++)
```

### Signal<T> — Two-way binding (for text input)
```csharp
using Comet.Reactive;  // needed for Signal<T>

readonly Signal<string> username = new Signal<string>("");

// TextField REQUIRES Signal<string>, not Reactive<string>
TextField(username, "Enter name")

// Read signal value reactively
Text(() => $"Hello, {username.Value}!")
```

> 🔑 Use `Reactive<T>` for display-only state. Use `Signal<T>` (from `Comet.Reactive`) for `TextField` two-way binding.

## FontWeight Values

Available: `Bold`, `Semibold`, `Medium`, `Regular`, `Light`, `Heavy`, `Thin`, `UltraLight`, `UltraBold`, `Black`

> ❌ `FontWeight.SemiBold` (capital B) does NOT exist — use `FontWeight.Semibold` (lowercase b).

## Color Reference

`Colors.*` from `Microsoft.Maui.Graphics`:
```
Colors.White, Colors.Black, Colors.Red, Colors.Green, Colors.Blue,
Colors.Orange, Colors.Yellow, Colors.Purple, Colors.Pink,
Colors.Grey, Colors.Gray, Colors.DarkGray, Colors.LightGray,
Colors.Transparent, Colors.DodgerBlue, Colors.Crimson, Colors.Teal,
Colors.Coral, Colors.Gold, Colors.Indigo, Colors.Lime, Colors.Navy
```

Custom hex colors: `Color.FromArgb("#FF9F0A")`

## Hot Reload Constraints

The Go dev server uses Edit-and-Continue (EnC). Know what works and what doesn't:

### ✅ Works — delta produced, UI updates
- Method body changes (text, colors, layout, logic)
- Field initializer value changes
- New methods in existing types
- Lambda expression changes

### ⚠️ Unreliable — sometimes works, sometimes crashes
- Adding new fields or properties to existing classes

### ❌ Restart required (`Ctrl+C` and re-run `maui go`)
- Adding new classes or types
- Changing constructor signatures
- Changing method signatures (parameters, return type)
- Changing base classes or interfaces

### Design Principle

Design your initial file with all methods you'll need. During hot reload, modify method *bodies* freely. If you need structural changes (new types, new fields), restart.

```csharp
// ✅ SAFE: Multiple methods defined at initial compile. Bodies can change freely.
void Append(string digit) { /* body can change via hot reload */ }
void SetOp(string op) { /* body can change via hot reload */ }
void Calculate() { /* body can change via hot reload */ }
```

## Anti-Patterns That Cause Crashes

1. **Unicode operators in switch expressions** — `"÷"`, `"×"`, `"−"` cause encoding issues in the hot reload pipeline. Use ASCII: `"/"`, `"x"`, `"-"`.

2. **`.CornerRadius()` on non-Button views** — silently ignored or crashes. Use `.RoundedBorder()` for containers.

3. **`Button("Go", MyMethod)`** — CS1503 error. Method groups don't work. Must be `Button("Go", () => MyMethod())`.

4. **`Reactive<string>` for TextField** — won't bind two-way. TextField requires `Signal<string>`.

5. **`FontWeight.SemiBold`** (capital B) — doesn't exist. Use `FontWeight.Semibold`.

6. **String interpolation lambdas to string params** — `SomeHelper(() => $"val {x}")` fails if the method takes `string`. Inline the interpolation in `body()` since the whole body is reactive.

7. **`using new VStack { ... }`** collection initializer syntax — use `VStack(spacing: 8f, child1, child2)`.

## Common Mistakes to Avoid

1. Missing `using static Comet.CometControls;` — `VStack`, `Text`, `Button` won't resolve
2. Using `VStack(0, ...)` without `f` suffix — ambiguous overload
3. Using `new Text(...)` instead of `Text(...)` — use the clean static API
4. Forgetting `() =>` wrapper for reactive text — `Text(() => $"{x.Value}")` not `Text($"{x.Value}")`
5. Using MAUI Controls APIs (Shell, NavigationPage) — Comet has its own navigation
6. Using `Reactive<string>` for TextField — use `Signal<string>` from `Comet.Reactive`

## Example: Calculator App (E2E Verified)

Built from template, hot-reloaded, and interactively tested with button taps and arithmetic verification.

```csharp
#:package Comet

using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Calculator;

public class MainPage : View
{
    readonly Reactive<string> display = new("0");
    readonly Reactive<string> subDisplay = new("");

    double accumulator = 0;
    string pendingOp = "";
    bool resetOnNext = false;

    void Append(string digit)
    {
        if (resetOnNext) { display.Value = "0"; resetOnNext = false; }
        if (display.Value == "0" && digit != ".") display.Value = digit;
        else if (digit == "." && display.Value.Contains(".")) return;
        else display.Value += digit;
    }

    void SetOp(string op)
    {
        if (pendingOp != "") DoCalculate();
        accumulator = double.Parse(display.Value);
        pendingOp = op;
        subDisplay.Value = $"{accumulator} {op}";
        resetOnNext = true;
    }

    void DoCalculate()
    {
        if (pendingOp == "") return;
        double current = double.Parse(display.Value);
        double result = pendingOp switch
        {
            "+" => accumulator + current,
            "-" => accumulator - current,
            "x" => accumulator * current,
            "/" => current != 0 ? accumulator / current : 0,
            _ => current
        };
        display.Value = result.ToString();
        subDisplay.Value = "";
        accumulator = result;
        pendingOp = "";
        resetOnNext = true;
    }

    [Body]
    View body()
    {
        var darkBg = Color.FromArgb("#1C1C1E");
        var numBg = Color.FromArgb("#3A3A3C");
        var opBg = Color.FromArgb("#FF9F0A");
        var fnBg = Color.FromArgb("#636366");
        var eqBg = Color.FromArgb("#30D158");

        return VStack(spacing: 0f,
            new Spacer(),
            Text(() => subDisplay.Value)
                .FontSize(18).Color(Colors.Grey)
                .HorizontalTextAlignment(TextAlignment.End)
                .Margin(new Thickness(20, 0)),
            Text(() => display.Value)
                .FontSize(56).FontWeight(FontWeight.Bold)
                .Color(Colors.White)
                .HorizontalTextAlignment(TextAlignment.End)
                .Margin(new Thickness(20, 0, 20, 16)),
            Grid(
                columns: new object[] { "*", "*", "*", "*" },
                rows: new object[] { 70, 70, 70, 70, 70 },
                Button("AC", () => { display.Value = "0"; subDisplay.Value = ""; accumulator = 0; pendingOp = ""; resetOnNext = false; })
                    .Color(Colors.White).Background(new SolidPaint(fnBg)).CornerRadius(36).Cell(row: 0, column: 0),
                Button("+/-", () => { if (display.Value != "0") display.Value = display.Value.StartsWith("-") ? display.Value[1..] : "-" + display.Value; })
                    .Color(Colors.White).Background(new SolidPaint(fnBg)).CornerRadius(36).Cell(row: 0, column: 1),
                Button("%", () => { display.Value = (double.Parse(display.Value) / 100).ToString(); })
                    .Color(Colors.White).Background(new SolidPaint(fnBg)).CornerRadius(36).Cell(row: 0, column: 2),
                Button("/", () => SetOp("/")).Color(Colors.White).Background(new SolidPaint(opBg)).CornerRadius(36).Cell(row: 0, column: 3),
                Button("7", () => Append("7")).Color(Colors.White).Background(new SolidPaint(numBg)).CornerRadius(36).Cell(row: 1, column: 0),
                Button("8", () => Append("8")).Color(Colors.White).Background(new SolidPaint(numBg)).CornerRadius(36).Cell(row: 1, column: 1),
                Button("9", () => Append("9")).Color(Colors.White).Background(new SolidPaint(numBg)).CornerRadius(36).Cell(row: 1, column: 2),
                Button("x", () => SetOp("x")).Color(Colors.White).Background(new SolidPaint(opBg)).CornerRadius(36).Cell(row: 1, column: 3),
                Button("4", () => Append("4")).Color(Colors.White).Background(new SolidPaint(numBg)).CornerRadius(36).Cell(row: 2, column: 0),
                Button("5", () => Append("5")).Color(Colors.White).Background(new SolidPaint(numBg)).CornerRadius(36).Cell(row: 2, column: 1),
                Button("6", () => Append("6")).Color(Colors.White).Background(new SolidPaint(numBg)).CornerRadius(36).Cell(row: 2, column: 2),
                Button("-", () => SetOp("-")).Color(Colors.White).Background(new SolidPaint(opBg)).CornerRadius(36).Cell(row: 2, column: 3),
                Button("1", () => Append("1")).Color(Colors.White).Background(new SolidPaint(numBg)).CornerRadius(36).Cell(row: 3, column: 0),
                Button("2", () => Append("2")).Color(Colors.White).Background(new SolidPaint(numBg)).CornerRadius(36).Cell(row: 3, column: 1),
                Button("3", () => Append("3")).Color(Colors.White).Background(new SolidPaint(numBg)).CornerRadius(36).Cell(row: 3, column: 2),
                Button("+", () => SetOp("+")).Color(Colors.White).Background(new SolidPaint(opBg)).CornerRadius(36).Cell(row: 3, column: 3),
                Button("0", () => Append("0")).Color(Colors.White).Background(new SolidPaint(numBg)).CornerRadius(36).Cell(row: 4, column: 0, colSpan: 2),
                Button(".", () => Append(".")).Color(Colors.White).Background(new SolidPaint(numBg)).CornerRadius(36).Cell(row: 4, column: 2),
                Button("=", () => DoCalculate()).Color(Colors.White).Background(new SolidPaint(eqBg)).CornerRadius(36).Cell(row: 4, column: 3)
            ).ColumnSpacing(10).RowSpacing(10).Padding(new Thickness(14, 0, 14, 14))
        ).Background(new SolidPaint(darkBg)).IgnoreSafeArea();
    }
}
```