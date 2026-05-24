// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Microsoft.Maui.Cli.Commands;

/// <summary>
/// MAUI Go commands — Expo-like getting started experience.
///
/// Primary workflow:
///   1. User creates a .cs file with a Comet View (or runs "maui go create")
///   2. User runs "maui go" from that directory
///   3. Server starts, shows QR code, waits for companion app connection
///   4. User edits .cs file, UI updates live on device
///
/// Subcommands: create, serve, upgrade.
/// Bare "maui go" is the default action — auto-detects .cs files and starts serving.
/// </summary>
public static class GoCommands
{
	public static Command Create()
	{
		var portOption = new Option<int>("--port")
		{
			Description = "Port for the hot reload dev server",
			DefaultValueFactory = _ => 9000
		};

		var noQrOption = new Option<bool>("--no-qr")
		{
			Description = "Suppress QR code display"
		};

		var fileArg = new Argument<string?>("file")
		{
			Description = "Optional .cs file to serve (auto-detected if omitted)",
			Arity = ArgumentArity.ZeroOrOne,
		};

		// TODO: Migrate Go command output to use Program.GetFormatter(parseResult) for --json/--verbose support
		var goCommand = new Command("go", "Comet Go — instant prototyping without IDE setup")
		{
			portOption,
			noQrOption,
			fileArg
		};

		// Bare "maui go" — the primary user experience
		goCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var port = parseResult.GetValue(portOption);
			var noQr = parseResult.GetValue(noQrOption);
			var showQr = !noQr;
			var file = parseResult.GetValue(fileArg);

			return await RunGoAsync(file, port, showQr, ct);
		});

		goCommand.Add(CreateCreateCommand());
		goCommand.Add(CreateServeCommand(portOption, noQrOption));
		goCommand.Add(CreateUpgradeCommand());

		return goCommand;
	}

	/// <summary>
	/// Core logic for starting the Go dev server. Used by both bare "maui go" and "maui go serve".
	///
	/// Detection priority:
	///   1. Explicit file argument (e.g., "maui go MyApp.cs")
	///   2. Single .cs file in current directory → single-file mode
	///   3. Multiple .cs files in current directory → check for .csproj (project mode)
	///   4. No .cs files → error with guidance
	/// </summary>
	static async Task<int> RunGoAsync(string? file, int port, bool showQr, CancellationToken ct)
	{
		var cwd = Directory.GetCurrentDirectory();

		// Explicit file path provided
		if (file is not null)
		{
			var filePath = Path.IsPathRooted(file) ? file : Path.Combine(cwd, file);
			if (!File.Exists(filePath))
			{
				Console.Error.WriteLine($"Error: File not found: {filePath}");
				return 1;
			}
			if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
			{
				Console.Error.WriteLine("Error: File must be a .cs file.");
				return 1;
			}
#if NET10_0_OR_GREATER
			return await Microsoft.Maui.Go.Server.GoDevServer.RunSingleFileAsync(filePath, port, showQr, ct);
#else
			Console.Error.WriteLine("Error: 'maui go' dev server requires the .NET 10 build of the CLI.");
			return 1;
#endif
		}

		// Auto-detect .cs files in current directory
		var csFiles = Directory.GetFiles(cwd, "*.cs")
			.Where(f => !Path.GetFileName(f).StartsWith('.'))
			.ToArray();

		if (csFiles.Length == 0)
		{
			Console.Error.WriteLine("Error: No .cs files found in the current directory.");
			Console.Error.WriteLine();
			Console.Error.WriteLine("  To get started, create a Comet view file:");
			Console.Error.WriteLine("    maui go create MyApp");
			Console.Error.WriteLine();
			Console.Error.WriteLine("  Or specify a file path:");
			Console.Error.WriteLine("    maui go path/to/MyApp.cs");
			return 1;
		}

#if NET10_0_OR_GREATER
		// Single .cs file → single-file mode
		if (csFiles.Length == 1)
		{
			return await Microsoft.Maui.Go.Server.GoDevServer.RunSingleFileAsync(csFiles[0], port, showQr, ct);
		}

		// Multiple .cs files → project mode (look for .csproj)
		var csproj = Directory.GetFiles(cwd, "*.csproj").FirstOrDefault();
		if (csproj is not null)
		{
			return await Microsoft.Maui.Go.Server.GoDevServer.RunAsync(cwd, port, showQr, ct);
		}

		// Multiple .cs files but no .csproj — ask the author to disambiguate.
		Console.Error.WriteLine($"Found {csFiles.Length} .cs files but no .csproj in current directory.");
		Console.Error.WriteLine("Specify which file to serve:");
		foreach (var f in csFiles)
			Console.Error.WriteLine($"    maui go {Path.GetFileName(f)}");
		return 1;
#else
		Console.Error.WriteLine("Error: 'maui go' dev server requires the .NET 10 build of the CLI.");
		return 1;
#endif
	}

	static Command CreateCreateCommand()
	{
		var nameArg = new Argument<string>("name") { Description = "Name for the new Comet Go app" };

		var command = new Command("create", "Create a new Comet Go single-file app")
		{
			nameArg,
		};

		command.SetAction((ParseResult parseResult, CancellationToken _) =>
		{
			var name = parseResult.GetValue(nameArg);

			if (string.IsNullOrWhiteSpace(name))
			{
				Console.Error.WriteLine("Error: App name is required.");
				return Task.FromResult(1);
			}

			// Validate the name as a simple identifier — must not contain path separators
			// or other characters that could escape the current directory.
			if (name!.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
				|| name.Contains("..")
				|| Path.IsPathRooted(name))
			{
				Console.Error.WriteLine("Error: App name must be a simple name (no slashes, drive letters, or '..').");
				return Task.FromResult(1);
			}

			var safeNamespace = name.Replace("-", "_").Replace(" ", "_");
			safeNamespace = new string(safeNamespace.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
			if (safeNamespace.Length == 0 || char.IsDigit(safeNamespace[0]))
				safeNamespace = "_" + safeNamespace;
			var fileName = $"{name}.cs";
			var cwd = Directory.GetCurrentDirectory();
			var filePath = Path.GetFullPath(Path.Combine(cwd, fileName));
			var cwdNormalized = Path.GetFullPath(cwd).TrimEnd(Path.DirectorySeparatorChar);

			// Defensive: confirm the resolved path stays under cwd even though
			// we already rejected obvious traversal sequences above.
			if (!filePath.StartsWith(cwdNormalized + Path.DirectorySeparatorChar, StringComparison.Ordinal)
				&& filePath != cwdNormalized)
			{
				Console.Error.WriteLine("Error: Resolved file path escapes the current directory.");
				return Task.FromResult(1);
			}

			if (File.Exists(filePath))
			{
				Console.Error.WriteLine($"Error: File '{fileName}' already exists.");
				return Task.FromResult(1);
			}

			var source = $$"""
				#:package Comet

				using Comet;
				using Microsoft.Maui;
				using Microsoft.Maui.Graphics;
				using static Comet.CometControls;

				namespace {{safeNamespace}};

				public class MainPage : View
				{
				    readonly Reactive<int> count = new(0);

				    [Body]
				    View body() =>
				        VStack(20,
				            Text("Welcome to {{name}}!")
				                .FontSize(28)
				                .FontWeight(FontWeight.Bold)
				                .Color(Colors.Orange)
				                .HorizontalTextAlignment(TextAlignment.Center),

				            Text(() => $"Count: {count.Value}")
				                .FontSize(22)
				                .HorizontalTextAlignment(TextAlignment.Center),

				            Button("Tap me!", () => count.Value++)
				                .Color(Colors.White)
				                .Background(new SolidPaint(Colors.Orange))
				                .CornerRadius(12)
				                .Frame(height: 50),

				            Text("Edit this file and save -- the UI updates live!")
				                .FontSize(14)
				                .Color(Colors.Gray)
				                .HorizontalTextAlignment(TextAlignment.Center)
				        )
				        .Padding(new Thickness(32))
				        .Alignment(Alignment.Center);
				}
				""";
			File.WriteAllText(filePath, source);

			// Install Comet Go skill for AI coding assistants
			var skillDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				".copilot", "skills", "comet-go");
			var skillPath = Path.Combine(skillDir, "SKILL.md");
			// Skill file is created once. Users can update via `maui ai update` or delete and re-create.
			if (!File.Exists(skillPath))
			{
				Directory.CreateDirectory(skillDir);
				File.WriteAllText(skillPath, GetCometGoSkill());
			}

			Console.WriteLine();
			Console.WriteLine($"  Created {fileName}");
			Console.WriteLine();
			Console.WriteLine("  Next:");
			Console.WriteLine("    maui go");
			Console.WriteLine();
			Console.WriteLine("  Then connect with the Comet Go companion app.");

			return Task.FromResult(0);
		});

		return command;
	}

	/// <summary>
	/// Returns the Comet Go skill content. This is a copy of .github/skills/comet-go/SKILL.md
	/// and should be kept in sync with it.
	/// </summary>
	static string GetCometGoSkill() => """
		---
		name: comet-go
		description: Write and edit Comet Go single-file apps (.cs files using the Comet MVU framework for .NET MAUI). Use when writing, editing, or debugging Comet Go apps, or when the user mentions "maui go", "comet go", or is working with .cs files that import Comet.
		---

		# Comet Go -- Single-File App Development

		Comet Go is a single-file app experience for .NET MAUI using the Comet MVU framework.
		The user writes ONE .cs file, runs `maui go`, and it live-reloads on their device.
		Follow these rules when writing or editing Comet Go .cs files.

		## What is Comet Go?

		Comet Go is a single-file app experience for .NET MAUI using the Comet MVU framework.
		You write ONE .cs file, run `maui go`, and it live-reloads on your phone.

		## Required Imports

		Every Comet Go file MUST start with:

		```csharp
		#:package Comet

		using System;
		using Comet;
		using Microsoft.Maui;
		using Microsoft.Maui.Graphics;
		using static Comet.CometControls;
		```

		`using System;` is required for `Action`, `Func<T>`, `Math`, `Convert`, etc.
		`using static Comet.CometControls;` enables the clean API (no `new` keyword).

		## App Structure

		```csharp
		public class MainPage : View
		{
		    // State: use Reactive<T> for values that update the UI
		    readonly Reactive<int> count = new(0);
		    readonly Reactive<string> name = new("World");

		    // Body method defines the UI -- must have [Body] attribute
		    [Body]
		    View body() =>
		        VStack(16f,
		            Text(() => $"Hello {name.Value}!"),
		            Button("Tap", () => count.Value++)
		        );
		}
		```

		## CRITICAL: VStack/HStack Spacing Parameter

		When passing spacing as a literal number, use `f` suffix to avoid ambiguity:
		- CORRECT: `VStack(16f, ...)`  or `HStack(8f, ...)`
		- WRONG:   `VStack(0, ...)`  -- ambiguous between float? and LayoutAlignment
		- WRONG:   `VStack(16, ...)` -- may be ambiguous

		## Available Controls (via `using static Comet.CometControls`)

		### Layout Containers
		```csharp
		VStack(float? spacing, params View[] children)     // vertical stack
		HStack(float? spacing, params View[] children)     // horizontal stack
		ZStack(params View[] children)                      // overlay stack
		Grid(object[] columns, object[] rows, params View[] children)
		ScrollView(View content)
		ScrollView(Orientation orientation, View content)
		Border(View content)
		Spacer()
		```

		### Controls (source-generated from MAUI interfaces)
		```csharp
		// Text display
		Text("static text")                    // static label
		Text(() => $"dynamic {value}")         // reactive label (auto-updates)
		Text(() => someReactive.Value)          // reactive binding

		// Buttons
		Button("label", () => { /* action */ })
		Button("label", Action handler)

		// Text input
		TextField("initial text", "placeholder")
		TextField(reactiveString, "placeholder")
		SecureField("", "Password")

		// Toggle / Switch
		Toggle(false)
		Toggle(reactiveBool)

		// Slider
		Slider(value: 0.5, minimum: 0, maximum: 1)

		// Progress
		ProgressBar(0.75)

		// Activity indicator
		ActivityIndicator(true)

		// Date/Time
		DatePicker(DateTime.Now)
		TimePicker(TimeSpan.FromHours(12))

		// Stepper
		Stepper(value: 0, minimum: 0, maximum: 100, interval: 1)

		// Image
		Image("https://example.com/image.png")

		// Picker
		Picker(0, "Option A", "Option B", "Option C")
		```

		### Grid Layout
		```csharp
		Grid(
		    columns: new object[] { "*", "*", "*", "*" },  // 4 equal columns
		    rows: new object[] { 70, 70, 70 },              // 3 rows of 70px
		    Button("1", () => {}).Cell(row: 0, column: 0),
		    Button("2", () => {}).Cell(row: 0, column: 1),
		    Button("wide", () => {}).Cell(row: 1, column: 0, colSpan: 2)
		)
		.ColumnSpacing(8f)
		.RowSpacing(8f)
		```

		Column/row definitions: `"*"` (star), `"2*"` (weighted), `"Auto"`, or integer pixels.

		## Fluent Styling API

		Chain these after any control:
		```csharp
		.FontSize(24)
		.FontWeight(FontWeight.Bold)          // Bold, Regular, Light, Medium, Heavy, Thin
		.Color(Colors.White)                   // text/foreground color
		.Background(Colors.Blue)               // solid color background
		.Background(new SolidPaint(Colors.Red)) // paint-based background
		.HorizontalTextAlignment(TextAlignment.Center)  // Start, Center, End
		.Frame(width: 100, height: 50)         // fixed size
		.Frame(height: 50)                     // fixed height, auto width
		.Padding(new Thickness(16))            // uniform padding
		.Padding(new Thickness(16, 8))         // horizontal, vertical
		.Padding(new Thickness(16, 8, 16, 8))  // left, top, right, bottom
		.Margin(new Thickness(8))
		.CornerRadius(12)
		.Alignment(Alignment.Center)
		.ClipShape(new Circle())
		.Shadow(Colors.Black, radius: 4, x: 0, y: 2)
		.Opacity(0.8)
		.IsVisible(true)
		```

		## FontWeight Values (MAUI enum)

		Available values: `Bold`, `Regular`, `Light`, `Medium`, `Heavy`,
		`Thin`, `UltraLight`, `UltraBold`, `Black`

		NOTE: `SemiBold` does NOT exist. Use `Medium` or `Bold` instead.

		## Reactive State

		```csharp
		readonly Reactive<int> count = new(0);         // integer state
		readonly Reactive<string> text = new("hello");  // string state
		readonly Reactive<bool> isOn = new(false);      // boolean state
		readonly Reactive<double> value = new(0.5);     // double state

		// Reading: use .Value in reactive lambdas
		Text(() => $"Count: {count.Value}")

		// Writing: set .Value to trigger UI update
		Button("Add", () => count.Value++)
		```

		## Color Reference

		Use `Colors.*` from `Microsoft.Maui.Graphics`:
		```
		Colors.White, Colors.Black, Colors.Red, Colors.Green, Colors.Blue,
		Colors.Orange, Colors.Yellow, Colors.Purple, Colors.Pink, Colors.Gray,
		Colors.DarkGray, Colors.LightGray, Colors.Transparent,
		Colors.DodgerBlue, Colors.Crimson, Colors.Teal, Colors.Coral,
		Colors.Gold, Colors.Indigo, Colors.Lime, Colors.Navy
		```

		Custom colors: `Color.FromArgb("#FF9F0A")` or `Color.FromRgba(255, 159, 10, 255)`

		## Hot Reload Constraints

		The Go dev server uses Edit-and-Continue (EnC) for hot reload. These edits work:
		- Changing method bodies (text, colors, layout, logic)
		- Changing property values
		- Changing lambda expressions

		These edits require an app restart (server will say "RESTART REQUIRED"):
		- Adding new classes or types
		- Adding new fields to existing classes
		- Changing method signatures
		- Adding new methods
		- Changing base classes or interfaces

		## Common Mistakes to Avoid

		1. Missing `using System;` -- causes `Action`, `Math`, `Func<T>` to not resolve
		2. Missing `using static Comet.CometControls;` -- causes `VStack`, `Text` etc to not resolve
		3. Using `FontWeight.SemiBold` -- does not exist, use `Medium` or `Bold`
		4. Using `VStack(0, ...)` -- ambiguous, use `VStack(0f, ...)` with float suffix
		5. Using `new Text(...)` instead of `Text(...)` -- use the clean static API
		6. Using `new VStack { ... }` collection initializer -- use `VStack(spacing, child1, child2)`
		7. Passing method groups to Button -- use `() => Method()` not `Method`
		8. Using MAUI Controls APIs (Shell, NavigationPage) -- Comet has its own navigation
		9. Forgetting `() =>` wrapper for reactive text -- `Text(() => $"{x.Value}")` not `Text($"{x.Value}")`

		## Example: Calculator App

		```csharp
		#:package Comet

		using System;
		using Comet;
		using Microsoft.Maui;
		using Microsoft.Maui.Graphics;
		using static Comet.CometControls;

		namespace Calculator;

		public class MainPage : View
		{
		    readonly Reactive<string> display = new("0");
		    double accumulator = 0;
		    string pendingOp = "";
		    bool resetOnNext = false;

		    void Append(string digit)
		    {
		        if (resetOnNext) { display.Value = "0"; resetOnNext = false; }
		        display.Value = display.Value == "0" ? digit : display.Value + digit;
		    }

		    void SetOp(string op)
		    {
		        accumulator = double.Parse(display.Value);
		        pendingOp = op;
		        resetOnNext = true;
		    }

		    void Calculate()
		    {
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
		        pendingOp = "";
		        resetOnNext = true;
		    }

		    [Body]
		    View body() =>
		        VStack(8f,
		            Spacer(),
		            Text(() => display.Value)
		                .FontSize(48)
		                .FontWeight(FontWeight.Bold)
		                .HorizontalTextAlignment(TextAlignment.End)
		                .Margin(new Thickness(20, 0)),
		            Grid(
		                columns: new object[] { "*", "*", "*", "*" },
		                rows: new object[] { 60, 60, 60, 60, 60 },
		                Button("AC", () => { display.Value = "0"; accumulator = 0; pendingOp = ""; })
		                    .Cell(row: 0, column: 0),
		                Button("/", () => SetOp("/")).Cell(row: 0, column: 3),
		                Button("7", () => Append("7")).Cell(row: 1, column: 0),
		                Button("8", () => Append("8")).Cell(row: 1, column: 1),
		                Button("9", () => Append("9")).Cell(row: 1, column: 2),
		                Button("x", () => SetOp("x")).Cell(row: 1, column: 3),
		                Button("4", () => Append("4")).Cell(row: 2, column: 0),
		                Button("5", () => Append("5")).Cell(row: 2, column: 1),
		                Button("6", () => Append("6")).Cell(row: 2, column: 2),
		                Button("-", () => SetOp("-")).Cell(row: 2, column: 3),
		                Button("1", () => Append("1")).Cell(row: 3, column: 0),
		                Button("2", () => Append("2")).Cell(row: 3, column: 1),
		                Button("3", () => Append("3")).Cell(row: 3, column: 2),
		                Button("+", () => SetOp("+")).Cell(row: 3, column: 3),
		                Button("0", () => Append("0")).Cell(row: 4, column: 0, colSpan: 2),
		                Button(".", () => Append(".")).Cell(row: 4, column: 2),
		                Button("=", () => Calculate()).Cell(row: 4, column: 3)
		            )
		            .ColumnSpacing(8f)
		            .RowSpacing(8f)
		            .Padding(new Thickness(12))
		        )
		        .Background(Colors.Black);
		}
		```
		""";

	/// <summary>
	/// "maui go serve" — explicit serve command (delegates to the same RunGoAsync logic).
	/// Kept for discoverability and backward compat with existing docs.
	/// </summary>
	static Command CreateServeCommand(Option<int> portOption, Option<bool> noQrOption)
	{
		var fileArg = new Argument<string?>("file")
		{
			Description = "Optional .cs file to serve",
			Arity = ArgumentArity.ZeroOrOne,
		};

		var command = new Command("serve", "Start the Comet Go dev server with hot reload")
		{
			fileArg,
			portOption,
			noQrOption,
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var port = parseResult.GetValue(portOption);
			var noQr = parseResult.GetValue(noQrOption);
			var showQr = !noQr;
			var file = parseResult.GetValue(fileArg);

			return await RunGoAsync(file, port, showQr, ct);
		});

		return command;
	}

	static Command CreateUpgradeCommand()
	{
		var forceOpt = new Option<bool>("--force") { Description = "Overwrite known-generated files if they already exist" };
		var dryRunOpt = new Option<bool>("--dry-run") { Description = "Print planned actions without writing any files" };
		var buildOpt = new Option<bool>("--build") { Description = "Run `dotnet build` after scaffolding to verify the upgrade" };
		var keepBackupOpt = new Option<bool>("--keep-backup") { Description = "Back up the original .cs (and any overwritten files) under .maui-go-backup/<timestamp>/", DefaultValueFactory = _ => true };

		var command = new Command("upgrade", "Graduate a Comet Go file-based program to a full MAUI project")
		{
			forceOpt, dryRunOpt, buildOpt, keepBackupOpt,
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var options = new Go.GoUpgradeOptions(
				Cwd: Directory.GetCurrentDirectory(),
				Force: parseResult.GetValue(forceOpt),
				DryRun: parseResult.GetValue(dryRunOpt),
				Build: parseResult.GetValue(buildOpt),
				KeepBackup: parseResult.GetValue(keepBackupOpt));

			var result = await Go.GoUpgradeRunner.RunAsync(options, line => Console.WriteLine(line), ct);
			return result.ExitCode;
		});

		return command;
	}
}
