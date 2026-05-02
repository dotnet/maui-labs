# Animations and Gestures Guide

Comet provides a property-based animation system that integrates with the
reactive state pipeline, plus a set of gesture recognizers attached via fluent
extension methods. Animations interpolate environment property values over
time, while gestures dispatch user interactions to callbacks.


## Animation Fundamentals

Comet animations work by monitoring property changes. When you call
`view.Animate(action)`, the framework:

1. Starts monitoring property changes on `ContextualObject`.
2. Executes the action (which sets new property values).
3. Captures the old and new values for each changed property.
4. Creates a `ContextualAnimation` for each property that interpolates from
   old to new over the specified duration.

This means any environment property (opacity, scale, rotation, translation,
background color, frame size) can be animated.


## Basic Animations

### The Animate Extension Method

The primary animation API is the `Animate<T>` extension method on `View`:

```csharp
public static T Animate<T>(
	this T view,
	Action<T> action,
	Action completed = null,
	double duration = 0.2,
	double delay = 0,
	bool repeats = false,
	bool autoReverses = false,
	string id = null,
	Lerp lerp = null
) where T : View;
```

With an explicit easing function:

```csharp
public static T Animate<T>(
	this T view,
	Easing easing,
	Action<T> action,
	Action completed = null,
	double duration = 0.2,
	double delay = 0,
	bool repeats = false,
	bool autoReverses = false,
	string id = null,
	Lerp lerp = null
) where T : View;
```

Duration and delay are in **seconds** (not milliseconds).

### Simple Property Animations

```csharp
// Fade a view to 50% opacity over 0.3 seconds
myView.Animate(v => v.Opacity(0.5), duration: 0.3);

// Scale up with easing
myView.Animate(Easing.CubicOut, v => v.Scale(1.5), duration: 0.4);

// Rotate with auto-reverse
myView.Animate(
	v => v.Rotation(180),
	duration: 0.5,
	autoReverses: true
);

// Repeating pulse animation
myView.Animate(
	v => v.Opacity(0.3),
	duration: 0.8,
	repeats: true,
	autoReverses: true
);
```

### Animating Multiple Properties

The action can set multiple properties; each gets its own interpolation:

```csharp
myView.Animate(v =>
{
	v.Scale(1.2);
	v.Opacity(0.8);
	v.Rotation(45);
}, duration: 0.5);
```

### Aborting Animations

Name animations with an `id` to abort them later:

```csharp
myView.Animate(
	v => v.TranslationY(-100),
	duration: 1.0,
	id: "slide-up"
);

// Later, abort the animation
myView.AbortAnimation("slide-up");
```


## Convenience Animation Methods

Comet provides named methods for common animation patterns. All return the
view for fluent chaining. Duration is in seconds, easing defaults to
`Easing.Default`.

| Method | Description |
|--------|-------------|
| `FadeTo(opacity, duration, easing)` | Animate opacity |
| `TranslateTo(x, y, duration, easing)` | Animate position |
| `ScaleTo(scale, duration, easing)` | Animate uniform scale |
| `ScaleTo(scaleX, scaleY, duration, easing)` | Animate non-uniform scale |
| `RotateTo(degrees, duration, easing)` | Animate Z rotation |
| `RotateXTo(degrees, duration, easing)` | Animate X rotation (3D) |
| `RotateYTo(degrees, duration, easing)` | Animate Y rotation (3D) |
| `ColorTo(color, duration, easing)` | Animate background color |

Examples:

```csharp
myView.FadeTo(0.0, duration: 0.3, easing: Easing.CubicIn);

myView.TranslateTo(100, 0, duration: 0.4);

myView.ScaleTo(0.5, 1.5, duration: 0.3);  // scaleX=0.5, scaleY=1.5

myView.RotateTo(360, duration: 1.0, easing: Easing.Linear);

myView.ColorTo(Colors.Red, duration: 0.5);
```


## Animation Sequences

`AnimationSequence<T>` runs animations one after another. Create a sequence
with `BeginAnimationSequence()`, add steps with `Animate()`, and finalize
with `EndAnimationSequence()`:

```csharp
myView
	.BeginAnimationSequence()
	.Animate(v => v.Opacity(0), duration: 0.3)
	.Animate(v => v.TranslationY(-50), duration: 0.2)
	.Animate(v =>
	{
		v.Opacity(1);
		v.TranslationY(0);
	}, duration: 0.3)
	.EndAnimationSequence();
```

The sequence runs each step in order. The total duration is the sum of all
step durations plus delays. Sequences can repeat:

```csharp
myView
	.BeginAnimationSequence(repeats: true)
	.Animate(v => v.Scale(1.1), duration: 0.5)
	.Animate(v => v.Scale(1.0), duration: 0.5)
	.EndAnimationSequence();
```


## AnimationBuilder (Fluent Composition)

`AnimationBuilder<T>` provides a higher-level fluent API for composing complex
animations with named operations, parallel groups, and sequencing. Duration and
delay values in the builder are in **milliseconds** (unlike the base
`Animate` method which uses seconds).

```csharp
myView.Animate(b => b
	.FadeTo(1.0, duration: 300)
	.TranslateTo(0, -20, duration: 300)
	.WithEasing(Easing.CubicOut)
);
```

### Builder Methods

| Method | Description |
|--------|-------------|
| `FadeTo(opacity, duration)` | Add fade step |
| `TranslateTo(x, y, duration)` | Add translation step |
| `ScaleTo(scale, duration)` | Add scale step |
| `RotateTo(rotation, duration)` | Add rotation step |
| `Then(configure)` | Add sequential steps |
| `Parallel(animations...)` | Run animations simultaneously |
| `WithDelay(ms)` | Delay the last added step |
| `WithEasing(easing)` | Set easing for the last step |

### Parallel Animations

```csharp
myView.Animate(b => b
	.Parallel(
		p => p.FadeTo(1.0, duration: 300),
		p => p.ScaleTo(1.0, duration: 400),
		p => p.TranslateTo(0, 0, duration: 350)
	)
);
```

### Sequential with Then

```csharp
myView.Animate(b => b
	.FadeTo(0.5, duration: 200)
	.Then(t => t
		.ScaleTo(1.5, duration: 300)
		.RotateTo(90, duration: 300)
	)
	.FadeTo(1.0, duration: 200)
);
```


## Spring Animations

Spring animations use a mass-spring-damper physics model with RK4 (Runge-Kutta
4th order) integration for natural, physically-driven motion.

### Spring Presets

Four presets cover common animation styles:

| Preset | Mass | Stiffness | Damping | Character |
|--------|------|-----------|---------|-----------|
| `Bouncy` | 1 | 170 | 12 | High energy with visible overshoot |
| `Smooth` | 1 | 100 | 20 | Balanced with minimal overshoot |
| `Stiff` | 1 | 300 | 30 | Fast and responsive, no overshoot |
| `Gentle` | 1 | 80 | 15 | Slow and soft motion |

### Using Spring Presets

```csharp
// Bouncy scale animation
myView.Spring(v => v.Scale(1.2), SpringPreset.Bouncy);

// Smooth translation
myView.Spring(v => v.TranslationY(-100), SpringPreset.Smooth);

// Stiff opacity change
myView.Spring(v => v.Opacity(0.5), SpringPreset.Stiff);
```

### Custom Spring Parameters

```csharp
myView.Spring(
	v => v.Scale(1.5),
	mass: 0.8,
	stiffness: 200,
	damping: 15
);
```

### Spring Physics

The spring simulation models `force = -stiffness * displacement - damping * velocity` and integrates using RK4 for numerical stability. The animation
finishes when both velocity and displacement drop below threshold values
(0.001). Duration is estimated automatically from the spring parameters.


## Keyframe Animations

Keyframe animations define view states at specific progress points (0.0 to
1.0) and interpolate between them.

```csharp
myView.Keyframes(k => k
	.At(0.0, v => { v.Opacity(0); v.Scale(0.5); })
	.At(0.3, v => { v.Opacity(1); v.Scale(1.2); })
	.At(0.6, v => { v.Opacity(1); v.Scale(0.9); })
	.At(1.0, v => { v.Opacity(1); v.Scale(1.0); }),
	duration: 600,
	easing: Easing.CubicOut
);
```

Duration is in **milliseconds**. The builder sorts keyframes by progress and
creates a segment animation between each consecutive pair. The first keyframe
(at progress 0.0) is applied immediately.


## Animation Helpers

The `Comet.Animations.AnimationHelpers` class provides simple fire-and-forget
animation utilities:

```csharp
using Comet.Animations;

// Fade in (sets opacity to 0, then to 1 on next frame)
myView.AnimateFadeIn(onComplete: () =>
{
	Console.WriteLine("Fade in complete");
});

// Fade out
myView.AnimateFadeOut();

// Pulse (repeated fade in/out)
myView.AnimatePulse(count: 3);
```

These helpers use `MainThread.BeginInvokeOnMainThread` to ensure UI updates
happen on the main thread.


## Gesture Recognizers

Comet provides gesture recognizers that attach to any view through fluent
extension methods. All gestures inherit from the `Gesture` base class.

### Gesture Base Classes

```csharp
public class Gesture
{
	public object PlatformGesture { get; set; }
	public virtual void Invoke();
}

public class Gesture<T> : Gesture
{
	public Gesture(Action<T> action);
	public Action<T> Action { get; }
}

public enum GestureStatus
{
	Started,
	Running,
	Completed,
	Canceled
}
```

### Tap Gesture

Detect single or multi-tap interactions:

```csharp
myView.OnTap(v =>
{
	Console.WriteLine("Tapped");
});
```

The `TapGesture` class provides position data and configuration:

| Property | Type | Description |
|----------|------|-------------|
| `X` | `double` | Tap position X relative to the view |
| `Y` | `double` | Tap position Y relative to the view |
| `NumberOfTapsRequired` | `int` | Required tap count (default 1) |
| `NumberOfTouchesRequired` | `int` | Required touch points (default 1) |

For double-tap:

```csharp
myView.AddGesture(new TapGesture(g =>
{
	Console.WriteLine($"Double-tapped at ({g.X}, {g.Y})");
})
{
	NumberOfTapsRequired = 2
});
```

### Long Press Gesture

Detect press-and-hold interactions:

```csharp
myView.OnLongPress(v =>
{
	Console.WriteLine("Long pressed");
});
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MinimumPressDuration` | `double` | 0.5 | Seconds to trigger |

### Pan Gesture

Track continuous drag/pan movements:

```csharp
myView.OnPan(gesture =>
{
	switch (gesture.Status)
	{
		case GestureStatus.Started:
			Console.WriteLine("Pan started");
			break;
		case GestureStatus.Running:
			myView.TranslationX(gesture.TotalX);
			myView.TranslationY(gesture.TotalY);
			break;
		case GestureStatus.Completed:
			Console.WriteLine("Pan ended");
			break;
	}
});
```

| Property | Type | Description |
|----------|------|-------------|
| `TotalX` | `double` | Horizontal displacement from start |
| `TotalY` | `double` | Vertical displacement from start |
| `VelocityX` | `double` | Horizontal velocity (points/sec) |
| `VelocityY` | `double` | Vertical velocity (points/sec) |
| `TouchPoints` | `int` | Number of active touch points |
| `Status` | `GestureStatus` | Current gesture phase |

### Pinch Gesture

Detect pinch-to-zoom interactions:

```csharp
myView.OnPinch(gesture =>
{
	if (gesture.Status == GestureStatus.Running)
	{
		myView.Scale(gesture.Scale);
	}
});
```

| Property | Type | Description |
|----------|------|-------------|
| `Scale` | `double` | Scale factor (1.0 = no scale) |
| `ScaleVelocity` | `double` | Rate of scale change |
| `OriginX` | `double` | Pinch center X relative to view |
| `OriginY` | `double` | Pinch center Y relative to view |
| `Status` | `GestureStatus` | Current gesture phase |

### Swipe Gesture

Detect directional swipe gestures:

```csharp
myView.OnSwipe(gesture =>
{
	Console.WriteLine($"Swiped {gesture.Direction}");
}, SwipeDirection.Right);
```

| Property | Type | Description |
|----------|------|-------------|
| `Direction` | `SwipeDirection` | Left, Right, Up, or Down |
| `Velocity` | `double` | Swipe speed (points/sec) |
| `OffsetX` | `double` | Horizontal offset |
| `OffsetY` | `double` | Vertical offset |
| `Threshold` | `double` | Minimum distance (default 100) |

### Drag and Drop Gestures

Comet provides separate gestures for drag sources and drop targets.

**Drag source:**

```csharp
var dragItem = new DragGesture
{
	DragStarting = (view) =>
	{
		// Return the data object for the drag operation
		return "item-42";
	},
	DropCompleted = (view) =>
	{
		Console.WriteLine("Drop completed");
	}
};
myView.AddGesture(dragItem);
```

**Drop target:**

```csharp
var dropTarget = new DropGesture
{
	DragOver = (view, data) =>
	{
		// Return true to accept, false to reject
		return data is string;
	},
	Drop = (view, data) =>
	{
		Console.WriteLine($"Received: {data}");
	},
	DragLeave = (view) =>
	{
		Console.WriteLine("Drag left the target");
	}
};
myView.AddGesture(dropTarget);
```

Both `DragGesture` and `DropGesture` support MVVM-style `ICommand` properties
as alternatives to the MVU callbacks.

### Pointer Gesture (Desktop)

Track mouse/pointer interactions on desktop platforms:

```csharp
var pointer = new PointerGesture
{
	PointerEntered = (view, point) =>
	{
		view.Background(Colors.LightBlue);
	},
	PointerExited = (view, point) =>
	{
		view.Background(Colors.White);
	},
	PointerMoved = (view, point) =>
	{
		Console.WriteLine($"Pointer at ({point.X}, {point.Y})");
	}
};
myView.AddGesture(pointer);
```

| Property | Type | Description |
|----------|------|-------------|
| `PointerEntered` | `Action<View, Point>` | Pointer enters view |
| `PointerExited` | `Action<View, Point>` | Pointer leaves view |
| `PointerMoved` | `Action<View, Point>` | Pointer moves over view |
| `PointerPressed` | `Action<View, Point>` | Button pressed |
| `PointerReleased` | `Action<View, Point>` | Button released |
| `ButtonsMask` | `int` | Mouse buttons to track (-1 = all) |


## Gesture Extension Method Summary

These extension methods are available on all `View` subclasses:

| Method | Gesture Type |
|--------|-------------|
| `OnTap(Action<T>)` | `TapGesture` |
| `OnLongPress(Action<T>)` | `LongPressGesture` |
| `OnPan(Action<PanGesture>)` | `PanGesture` |
| `OnPinch(Action<PinchGesture>)` | `PinchGesture` |
| `OnSwipe(Action<SwipeGesture>, SwipeDirection)` | `SwipeGesture` |
| `OnTapNavigate(Func<View>)` | `TapGesture` + navigation |
| `AddGesture(Gesture)` | Any gesture type |
| `RemoveGesture(Gesture)` | Remove a specific gesture |


## Combining Animations with State Changes

Animations compose with Comet's reactive state system. For a deep dive on
how state changes trigger view rebuilds, see the
[Reactive State Guide](reactive-state-guide.md). When a state change
triggers a body rebuild, you can animate the transition:

```csharp
public class AnimatedCounter : View
{
	readonly State<int> count = 0;
	readonly State<double> scale = 1.0;

	[Body]
	View body() =>
		VStack(
			Text($"Count: {count.Value}")
				.FontSize(24)
				.Scale(scale.Value),
			Button("Increment", () =>
			{
				count.Value++;

				// Animate the scale as visual feedback
				scale.Value = 1.3;
				this.Animate(v =>
				{
					scale.Value = 1.0;
				}, duration: 0.3, easing: Easing.BounceOut);
			})
		);
}
```


## Performance Considerations

- **Duration:** Keep animations short (0.2-0.5 seconds) for responsive UI.
  Long animations (over 1 second) may feel sluggish.

- **Property count:** Each animated property creates a separate interpolation.
  Animating many properties simultaneously increases per-frame work.

- **Spring animations:** The RK4 integration runs per-tick. Very low damping
  values cause many oscillation cycles before settling. Use `EstimateDuration()`
  to check expected duration before committing to spring parameters.

- **Keyframe animations:** Progress-sorted keyframes are walked linearly. Large
  numbers of keyframes (50+) may impact frame budget.

- **Gesture handlers:** Keep gesture callbacks lightweight. Heavy computation
  in `OnPan` or `OnPinch` callbacks (which fire continuously) can cause frame
  drops. Defer expensive work to `GestureStatus.Completed`.

- **Animation IDs:** Use `AbortAnimation(id)` to cancel running animations
  before starting new ones on the same property. Overlapping animations on the
  same property may produce unexpected interpolation.

For more on optimizing UI updates, see the
[Performance Optimization Guide](performance.md).


## See Also

- [Control Catalog](controls.md) -- every control that can be animated, plus
  the fluent property API used by the animation system.
- [Performance Optimization](performance.md) -- animation performance tips and
  how to minimize per-frame work.
- [Styling and Theming](styling.md) -- animating between theme states and
  building animated theme transitions.
