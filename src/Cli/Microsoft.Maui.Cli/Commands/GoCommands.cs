// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Microsoft.Maui.Cli.Commands;

/// <summary>
/// MAUI Go commands — Expo-like getting started experience.
/// Subcommands: create, serve, upgrade.
/// </summary>
public static class GoCommands
{
	public static Command Create()
	{
		var goCommand = new Command("go", "MAUI Go — instant prototyping without IDE setup");

		goCommand.Add(CreateCreateCommand());
		goCommand.Add(CreateServeCommand());
		goCommand.Add(CreateUpgradeCommand());

		return goCommand;
	}

	static Command CreateCreateCommand()
	{
		var nameArg = new Argument<string>("name") { Description = "Project name for the new MAUI Go app" };

		var templateOption = new Option<string>("--template")
		{
			Description = "Template to use (default, counter, notes, todo)",
			DefaultValueFactory = _ => "default"
		};

		var command = new Command("create", "Create a new MAUI Go project")
		{
			nameArg,
			templateOption
		};

		command.SetAction((ParseResult parseResult, CancellationToken _) =>
		{
			var name = parseResult.GetValue(nameArg);
			var template = parseResult.GetValue(templateOption);

			if (string.IsNullOrWhiteSpace(name))
			{
				Console.Error.WriteLine("Error: Project name is required.");
				return Task.FromResult(1);
			}

			var targetDir = Path.Combine(Directory.GetCurrentDirectory(), name);

			if (Directory.Exists(targetDir))
			{
				Console.Error.WriteLine($"Error: Directory '{name}' already exists.");
				return Task.FromResult(1);
			}

			Console.WriteLine($"  Creating MAUI Go project: {name}");

			// Scaffold the project
			Directory.CreateDirectory(targetDir);

			// Compute relative path to Comet project from the new project dir
			var repoRoot = FindRepoRoot(targetDir) ?? FindRepoRoot(AppContext.BaseDirectory);
			var cometRelativePath = repoRoot is not null
				? Path.GetRelativePath(targetDir, Path.Combine(repoRoot, "src", "Comet", "src", "Comet", "Comet.csproj"))
				: @"..\..\src\Comet\src\Comet\Comet.csproj";

			// Write .csproj with ProjectReference to local Comet
			var csproj = $"""
				<Project Sdk="Microsoft.NET.Sdk">
				  <PropertyGroup>
				    <TargetFramework>net11.0</TargetFramework>
				    <RootNamespace>{name.Replace("-", "_").Replace(" ", "_")}</RootNamespace>
				    <ImplicitUsings>enable</ImplicitUsings>
				    <Nullable>enable</Nullable>
				  </PropertyGroup>
				  <ItemGroup>
				    <ProjectReference Include="{cometRelativePath}" />
				  </ItemGroup>
				</Project>
				""";
			File.WriteAllText(Path.Combine(targetDir, $"{name}.csproj"), csproj);

			// Write MainPage.cs
			var mainPage = $$"""
				using Comet;
				using Microsoft.Maui;
				using Microsoft.Maui.Graphics;

				namespace {{name.Replace("-", "_").Replace(" ", "_")}};

				public class MainPage : View
				{
				    readonly Reactive<int> count = new(0);

				    [Body]
				    View body() => new VStack(spacing: 20)
				    {
				        new Text("Welcome to {{name}}! 🚀")
				            .FontSize(28)
				            .FontWeight(FontWeight.Bold)
				            .Color(Colors.Purple)
				            .HorizontalTextAlignment(TextAlignment.Center),

				        new Text(() => $"Count: {count.Value}")
				            .FontSize(22)
				            .HorizontalTextAlignment(TextAlignment.Center),

				        new Button("Tap me!", () => count.Value++)
				            .Color(Colors.White)
				            .Background(new SolidPaint(Colors.Purple))
				            .CornerRadius(12)
				            .Frame(height: 50),

				        new Text("Edit MainPage.cs and save — the UI updates live!")
				            .FontSize(14)
				            .Color(Colors.Gray)
				            .HorizontalTextAlignment(TextAlignment.Center),
				    }
				    .Padding(new Thickness(32))
				    .Alignment(Alignment.Center);
				}
				""";
			File.WriteAllText(Path.Combine(targetDir, "MainPage.cs"), mainPage);

			Console.WriteLine();
			Console.WriteLine($"  ✅ Created '{name}' in ./{name}/");
			Console.WriteLine();
			Console.WriteLine("  Next steps:");
			Console.WriteLine($"    cd {name}");
			Console.WriteLine($"    maui go serve --qr");
			Console.WriteLine();
			Console.WriteLine("  Then scan the QR code with the MAUI Go companion app.");

			return Task.FromResult(0);
		});

		return command;
	}

	/// <summary>
	/// Walk up directory tree to find the repo root (contains MauiLabs.slnx or .git).
	/// </summary>
	static string? FindRepoRoot(string? startDir)
	{
		if (startDir is null) return null;

		var dir = new DirectoryInfo(startDir);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "MauiLabs.slnx")) ||
				Directory.Exists(Path.Combine(dir.FullName, ".git")))
				return dir.FullName;
			dir = dir.Parent;
		}
		return null;
	}

	static Command CreateServeCommand()
	{
		var portOption = new Option<int>("--port")
		{
			Description = "Port for the hot reload dev server",
			DefaultValueFactory = _ => 9000
		};

		var qrOption = new Option<bool>("--qr")
		{
			Description = "Display QR code for companion app connection"
		};

		var command = new Command("serve", "Start the MAUI Go dev server with hot reload")
		{
			portOption,
			qrOption
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var port = parseResult.GetValue(portOption);
			var showQr = parseResult.GetValue(qrOption);
			var projectDir = Directory.GetCurrentDirectory();

			// Verify we're in a Go project
			var csproj = Directory.GetFiles(projectDir, "*.csproj").FirstOrDefault();
			if (csproj is null)
			{
				Console.Error.WriteLine("Error: No .csproj found in current directory.");
				Console.Error.WriteLine("Run 'maui go create <name>' first, then cd into the project.");
				return 1;
			}

#if NET10_0_OR_GREATER
			return await Go.Server.GoDevServer.RunAsync(projectDir, port, showQr, ct);
#else
			Console.Error.WriteLine("Error: 'maui go dev' requires .NET 10 or later.");
			return 1;
#endif
		});

		return command;
	}

	static Command CreateUpgradeCommand()
	{
		var command = new Command("upgrade", "Graduate a MAUI Go project to a full MAUI project");

		command.SetAction((ParseResult _, CancellationToken __) =>
		{
			Console.WriteLine($"[maui go upgrade] Not implemented yet.");
			Console.WriteLine();
			Console.WriteLine("  When implemented, this will:");
			Console.WriteLine("  1. Keep all Comet views (they work in full MAUI too)");
			Console.WriteLine("  2. Add platform-specific project structure (Platforms/)");
			Console.WriteLine("  3. Update .csproj with full MAUI workload references");
			Console.WriteLine("  4. Generate App.cs with CometApp subclass");

			return Task.FromResult(0);
		});

		return command;
	}
}
