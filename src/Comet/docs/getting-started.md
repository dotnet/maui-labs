# Getting Started with Comet

Comet is an MVU (Model-View-Update) framework built on .NET MAUI. You write
declarative, code-only UI in C# -- no XAML. State changes automatically trigger
re-rendering using platform-native controls.

This guide takes you from a clean machine to a running Comet app.


## Prerequisites

You need a development machine and a small set of tools before writing any code.

**Operating system.** macOS for iOS and Mac Catalyst targets. Windows for Android
and Windows targets. macOS can also build for Android.

**IDE (pick one):**

- Visual Studio 2022 (17.12 or later) with the .NET MAUI workload
- Visual Studio Code with the C# Dev Kit extension
- JetBrains Rider (2024.3 or later)

**Install the .NET 10 SDK.** Download it from
[https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0).
After installation, verify from a terminal:

```
dotnet --version
```

The output should start with `10.0`. This project requires SDK 10.0.101 or later.

**Install the MAUI workload:**

```
dotnet workload install maui
```

**For iOS development (macOS only):** install Xcode from the Mac App Store. After
installing, open Xcode once to accept the license and install components, or run:

```
sudo xcodebuild -license accept
```

**For Android development:** the MAUI workload installs the Android SDK
automatically. If you need a standalone install, run:

```
dotnet workload install android
```

Visual Studio 2022 also installs the Android SDK as part of its MAUI workload
setup.


## Create Your First Comet App

Comet ships a `dotnet new` project template that scaffolds a working app with
the correct project structure, NuGet references, and platform configuration.

### Install the template

```
dotnet new install Microsoft.Maui.Comet.Templates.Multiplatform
```

### Create the project

```
dotnet new comet -n MyFirstCometApp
cd MyFirstCometApp
```

This generates a complete Comet project. The key files are described below.

### Replace the main page

The template generates a full-featured counter demo. Replace it with this
minimal version so the code is easy to follow. Open `MainPage.cs` and replace
its entire contents with:

```csharp
using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace MyFirstCometApp;

public class CounterState
{
	public int Count { get; set; }
}

public class MainPage : Component<CounterState>
{
	readonly Reactive<string> message = "Tap the button to start counting.";

	public override View Render()
	{
		return new ScrollView
		{
			new VStack(spacing: 24)
			{
				new Text("Comet Counter")
					.FontSize(32)
					.FontWeight(FontWeight.Bold)
					.HorizontalTextAlignment(TextAlignment.Center),

				new Text($"Count: {State.Count}")
					.FontSize(48)
					.FontWeight(FontWeight.Bold)
					.Color(Colors.Purple)
					.HorizontalTextAlignment(TextAlignment.Center),

				new Text(() => message.Value)
					.FontSize(14)
					.Color(Colors.Grey)
					.HorizontalTextAlignment(TextAlignment.Center),

				new Button("Increment", () =>
				{
					SetState(s => s.Count++);
					message.Value = $"Count updated to {State.Count}.";
				})
				.Background(Colors.Purple)
				.Color(Colors.White)
				.Frame(height: 48)
				.CornerRadius(12),

				new Button("Reset", () =>
				{
					SetState(s => s.Count = 0);
					message.Value = "Counter reset.";
				})
				.Frame(height: 48)
				.CornerRadius(12),
			}
			.Padding(new Thickness(24))
		};
	}
}
```

The remaining generated files do not need changes. For reference, here is what
each one does.

**`App.cs`** -- the application entry point. It extends `CometApp`, sets
`MainPage` as the root view, and configures the MAUI host:

```csharp
using Microsoft.Maui.Hosting;

namespace MyFirstCometApp;

public class App : CometApp
{
	public App()
	{
		Body = () => new MainPage();
	}

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseCometApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.EnableHotReload();
#endif

		return builder.Build();
	}
}
```

`UseCometApp<App>()` registers the Comet handler infrastructure and sets `App`
as the `IApplication` implementation. You do not need to call
`UseCometHandlers()` separately -- `UseCometApp` calls it internally.

**`GlobalUsings.cs`** -- explicit using directives shared across all files.
Comet projects disable implicit usings, so this file imports the namespaces you
need:

```csharp
global using Microsoft.Maui;
global using Microsoft.Maui.Graphics;
global using Comet;
global using System;
global using System.Linq;
global using Microsoft.Maui.Primitives;
```

**`Helpers/Reload.cs`** -- a no-op stub that enables the `EnableHotReload()`
call in `App.cs`. MAUI's built-in hot reload handles everything automatically;
this file exists for backward compatibility.

### Alternative: Starting from a MAUI project

If the template is not available, you can add Comet to a standard MAUI project:

```
dotnet new maui -n MyFirstCometApp
cd MyFirstCometApp
dotnet add package Microsoft.Maui.Comet
```

Then delete the XAML files the MAUI template generates:

```
rm MainPage.xaml MainPage.xaml.cs
rm App.xaml App.xaml.cs
rm AppShell.xaml AppShell.xaml.cs
```

Create `App.cs`, `MainPage.cs`, and `GlobalUsings.cs` with the contents shown
above. In `MauiProgram.cs`, replace the body of `CreateMauiApp()` with:

```csharp
public static MauiApp CreateMauiApp()
{
	var builder = MauiApp.CreateBuilder();
	builder.UseCometApp<App>()
		.ConfigureFonts(fonts =>
		{
			fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
		});

	return builder.Build();
}
```


## Build and Run

From the `MyFirstCometApp` directory, build and launch for your target platform.

**Mac Catalyst (macOS):**

```
dotnet build -f net10.0-maccatalyst -t:Run
```

**iOS Simulator (macOS, requires Xcode):**

```
dotnet build -f net10.0-ios -t:Run
```

**Android Emulator:**

```
dotnet build -f net10.0-android -t:Run
```

Make sure an Android emulator is running or a device is connected. You can
create an emulator through Android Studio or the `avdmanager` command-line tool.

**Windows:**

```
dotnet build -f net10.0-windows10.0.19041.0
```

On Windows, the built executable is in `bin/Debug/net10.0-windows10.0.19041.0/`.
Run it directly or use Visual Studio's debugger.


## What Just Happened?

The counter app demonstrates the core mechanics of Comet.

**`Component<CounterState>`** -- your page extends `Component<TState>`, where
`TState` is a plain C# class holding your structured state. Access it through
the `State` property (e.g., `State.Count`).

**`Reactive<string>`** -- a lightweight reactive signal for individual values.
Declare it as a field, and Comet tracks reads and writes automatically. Implicit
conversion from `string` to `Reactive<string>` means you can initialize it
directly: `readonly Reactive<string> message = "hello";`.

**`SetState(s => s.Count++)`** -- the only way to mutate `Component<TState>`
state. The lambda receives the current state object. After it executes, Comet
calls `Render()` again to rebuild the UI tree with the new values.

**`message.Value = "..."`** -- writing to a `Reactive<T>.Value` triggers any
view that reads it to update. This is a lighter-weight alternative to
`SetState()` for values that live outside the `TState` class.

**`() => message.Value`** -- wrapping a read in a lambda creates a live binding.
Comet evaluates the lambda, records which reactive sources were read, and
re-evaluates it whenever any of those sources change. The `Text` view updates
its display automatically.

**Fluent API** -- methods like `.FontSize(32)`, `.Color(Colors.Purple)`, and
`.Padding(new Thickness(24))` set properties via a fluent builder pattern. These
store values in Comet's environment system and propagate down the view tree. For
the full list of fluent methods, see the [Control Catalog](controls.md).


## Next Steps

- [Control Catalog](controls.md) -- explore every control available in Comet
  with code examples and the fluent API reference.
- [Layout System](layout.md) -- learn how to arrange views with VStack, HStack,
  Grid, and other layout containers.
- [Reactive State Guide](reactive-state-guide.md) -- deep dive on `Reactive<T>`,
  `Signal<T>`, `Component<TState>`, and automatic dependency tracking.
- [Navigation Guide](navigation.md) -- build multi-page apps with stack, tab,
  and Shell navigation.
- [Documentation Index](index.md) -- full list of guides, architecture docs, and
  research.
