using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;
using IOPath = System.IO.Path;

namespace Comet.Tests
{
	public class Phase9SampleDocumentationValidationTests
	{
		const string CounterSampleProjectEnv = "COMET_PHASE9_COUNTER_SAMPLE_PROJECT";
		const string CoffeeSampleProjectEnv = "COMET_PHASE9_COFFEE_SAMPLE_PROJECT";
		const string MigrationGuideEnv = "COMET_PHASE9_MIGRATION_GUIDE";

		static readonly string[] DeprecatedPatterns =
		{
			@"\bState<[^>]+>",
			@"\[Body\]",
			@"\bListView\b",
			@"\bTableView\b",
			@"\bnew\s+(?:[\w\.]+\.)?Frame\b|:\s*(?:[\w\.]+\.)?Frame\b",
			@"\bMessagingCenter\b",
			@"\bDevice\.",
			@"\bDisplayAlert\s*\(",
			@"\bDisplayActionSheet\s*\(",
			@"\bXamarin\.",
			@"\bCompatibility\.",
		};

		[Fact]
		public void CounterSamplePassesPhase9GateWhenConfigured()
		{
			if (!TryGetEnvironmentPath(CounterSampleProjectEnv, out var projectPath))
				return;

			Phase9ArtifactValidator.ValidateCounterSample(projectPath);
		}

		[Fact]
		public void CoffeeAppPassesPhase9GateWhenConfigured()
		{
			if (!TryGetEnvironmentPath(CoffeeSampleProjectEnv, out var projectPath))
				return;

			Phase9ArtifactValidator.ValidateCoffeeApp(projectPath);
		}

		[Fact]
		public void MigrationGuidePassesPhase9GateWhenConfigured()
		{
			if (!TryGetEnvironmentPath(MigrationGuideEnv, out var guidePath))
				return;

			Phase9ArtifactValidator.ValidateMigrationGuide(guidePath);
		}

		[Fact]
		public void SyntheticCounterFixturePassesCurrentSurfaceGate()
		{
			using var fixture = SyntheticFixture.CreateCounterFixture();
			Phase9ArtifactValidator.ValidateCounterSample(fixture.ProjectPath);
		}

		[Fact]
		public void LegacyCounterFixtureIsRejected()
		{
			using var fixture = SyntheticFixture.CreateLegacyCounterFixture();
			var exception = Record.Exception(() => Phase9ArtifactValidator.ValidateCounterSample(fixture.ProjectPath));
			Assert.NotNull(exception);
			Assert.IsAssignableFrom<XunitException>(exception);
			Assert.Contains("Counter sample", exception.Message, StringComparison.OrdinalIgnoreCase);
		}

		[Fact]
		public void SyntheticCoffeeFixturePassesCurrentSurfaceGate()
		{
			using var fixture = SyntheticFixture.CreateCoffeeFixture();
			Phase9ArtifactValidator.ValidateCoffeeApp(fixture.ProjectPath);
		}

		[Fact]
		public void SyntheticMigrationGuidePassesGate()
		{
			using var fixture = SyntheticFixture.CreateMigrationGuideFixture();
			Phase9ArtifactValidator.ValidateMigrationGuide(fixture.GuidePath);
		}

		static bool TryGetEnvironmentPath(string variableName, out string path)
		{
			path = Environment.GetEnvironmentVariable(variableName);
			return !string.IsNullOrWhiteSpace(path);
		}

		static class Phase9ArtifactValidator
		{
			static readonly RegexOptions RegexOptions = RegexOptions.IgnoreCase | RegexOptions.Multiline;
			static readonly Regex ComponentPattern = new(@":\s*Component(?:\s*<|\b)", RegexOptions);
			static readonly Regex RenderPattern = new(@"override\s+(?:Comet\.)?View\s+Render\s*\(", RegexOptions);
			static readonly Regex ReactiveOrSetStatePattern = new(@"\bReactive<|\bSetState\s*\(", RegexOptions);
			static readonly Regex CurrentAppSurfacePattern = new(@"\bUseCometApp\s*<", RegexOptions);
			static readonly Regex TypedNavigationPattern = new(@"\bNavigation\s*\??\.\s*Navigate\s*<", RegexOptions);
			static readonly Regex RichCoffeeSurfacePattern = new(@"\bCollectionView\b|\bNavigationView\b|\bTabView\b|\bCometShell\b|\bGoToAsync\s*<|\bRegisterRoute\s*<|\bNavigation\s*\??\.\s*Navigate\s*<", RegexOptions);

			public static void ValidateCounterSample(string projectPath)
			{
				var project = LoadProject(projectPath);
				AssertCurrentMauiProject(project);
				AssertContains(project.AllSources, CurrentAppSurfacePattern, "Counter sample must boot through builder.UseCometApp<TApp>().");
				AssertContains(project.ProductSources, ComponentPattern, "Counter sample must include at least one Component-based screen.");
				AssertContains(project.ProductSources, RenderPattern, "Counter sample must render through Render() rather than [Body].");
				AssertContains(project.ProductSources, ReactiveOrSetStatePattern, "Counter sample must demonstrate Reactive<T> or SetState(...).");
				AssertNoDeprecatedPatterns(project.ProductSources, "Counter sample");
			}

			public static void ValidateCoffeeApp(string projectPath)
			{
				var project = LoadProject(projectPath);
				var currentSurfaceSources = GetCurrentSurfaceSources(project);
				AssertCurrentMauiProject(project);
				AssertContains(project.AllSources, CurrentAppSurfacePattern, "Coffee app must boot through builder.UseCometApp<TApp>().");
				AssertMinimumMatches(project.ProductSources, ComponentPattern, 2, "Coffee app must include multiple Component-based screens or composables.");
				AssertContains(project.ProductSources, RenderPattern, "Coffee app must render through Render() rather than [Body].");
				AssertContains(project.ProductSources, ReactiveOrSetStatePattern, "Coffee app must demonstrate Reactive<T> or SetState(...).");
				AssertContains(currentSurfaceSources, RichCoffeeSurfacePattern, "Coffee app must exercise a richer current surface such as CollectionView, TabView, NavigationView, CometShell, or typed navigation.");
				AssertNoDeprecatedPatterns(currentSurfaceSources, "Coffee app current-surface references");
			}

			public static void ValidateMigrationGuide(string guidePath)
			{
				var normalizedGuidePath = IOPath.GetFullPath(guidePath);
				Assert.True(File.Exists(normalizedGuidePath), $"Migration guide not found: {normalizedGuidePath}");

				var markdown = File.ReadAllText(normalizedGuidePath);
				AssertContains(markdown, @"\bcounter\b", "Migration guide must call out the counter sample.");
				AssertContains(markdown, @"\bcoffee\b|\bbarista\b", "Migration guide must call out the coffee app.");
				AssertContains(markdown, @"\bComponent\b", "Migration guide must explain the Component surface.");
				AssertContains(markdown, @"Render\(\)", "Migration guide must explain Render() as the replacement for [Body].");
				AssertContains(markdown, @"Reactive<|Reactive<T>", "Migration guide must explain Reactive<T>.");
				AssertContains(markdown, @"State<|State<T>", "Migration guide must map legacy State<T> usage.");
				AssertContains(markdown, @"\[Body\]", "Migration guide must call out the legacy [Body] pattern.");
				AssertContains(markdown, @"SetState", "Migration guide must explain SetState(...) for Component state updates.");
				AssertContains(markdown, @"GoToAsync<|RegisterRoute<", "Migration guide must mention the typed navigation surface.");
				AssertContains(markdown, @"Comet\.SourceGenerator", "Migration guide must include the documented build order starting with Comet.SourceGenerator.");
				AssertContains(markdown, @"src/Comet|Comet\.csproj", "Migration guide must include the Comet build step.");
				AssertContains(markdown, @"tests/Comet\.Tests|Comet\.Tests", "Migration guide must include the test build/test step.");
				AssertContains(markdown, @"CollectionView", "Migration guide must steer samples toward CollectionView for MAUI 10.");
				AssertContains(markdown, @"Border", "Migration guide must steer samples toward Border for MAUI 10.");
				AssertContains(markdown, @"DisplayAlertAsync", "Migration guide must steer samples toward DisplayAlertAsync for MAUI 10.");
				AssertContains(markdown, @"MainThread", "Migration guide must steer samples toward MainThread APIs for MAUI 10.");
				AssertContains(markdown, @"```", "Migration guide must include runnable code blocks.");
			}

			static Phase9Project LoadProject(string projectPath)
			{
				var normalizedProjectPath = IOPath.GetFullPath(projectPath);
				Assert.True(File.Exists(normalizedProjectPath), $"Sample project not found: {normalizedProjectPath}");

				var projectDirectory = IOPath.GetDirectoryName(normalizedProjectPath);
				Assert.False(string.IsNullOrWhiteSpace(projectDirectory), $"Unable to determine project directory for {normalizedProjectPath}");

				var projectText = File.ReadAllText(normalizedProjectPath);
				var sourceFiles = LoadSourceFiles(projectDirectory);
				Assert.NotEmpty(sourceFiles);

				return new Phase9Project(normalizedProjectPath, projectDirectory, projectText, sourceFiles);
			}

			static IReadOnlyList<Phase9SourceFile> LoadSourceFiles(string projectDirectory)
			{
				var sourceFiles = Directory
					.EnumerateFiles(projectDirectory, "*.*", SearchOption.AllDirectories)
					.Where(path => IsSourceFile(path))
					.Where(path => !IsGeneratedPath(path))
					.Select(path => new Phase9SourceFile(path, File.ReadAllText(path)))
					.ToList();

				return sourceFiles;
			}

			static bool IsSourceFile(string path)
			{
				var extension = IOPath.GetExtension(path);
				return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
					|| extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase)
					|| extension.Equals(".razor", StringComparison.OrdinalIgnoreCase);
			}

			static bool IsGeneratedPath(string path)
			{
				var segments = path.Split(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);
				return segments.Any(segment =>
					segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
					|| segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
			}

			static void AssertCurrentMauiProject(Phase9Project project)
			{
				AssertContains(project.ProjectFileText, @"<UseMaui>\s*true\s*</UseMaui>", $"{project.ProjectPath} must opt into .NET MAUI.");
				AssertContains(project.ProjectFileText, @"net10\.0-maccatalyst", $"{project.ProjectPath} must target net10.0-maccatalyst so the Phase 9 gate can run on macOS.");
				AssertContains(project.ProjectFileText, @"<SingleProject>\s*true\s*</SingleProject>", $"{project.ProjectPath} must stay a MAUI single-project app.");
				AssertContains(project.ProjectFileText, @"ProjectReference\s+Include=.*Comet\.csproj", $"{project.ProjectPath} must reference src/Comet/Comet.csproj.");
			}

			static void AssertNoDeprecatedPatterns(IEnumerable<Phase9SourceFile> sourceFiles, string artifactName)
			{
				var hits = new List<string>();
				foreach (var pattern in DeprecatedPatterns)
				{
					var regex = new Regex(pattern, RegexOptions);
					foreach (var sourceFile in sourceFiles)
					{
						if (regex.IsMatch(sourceFile.Text))
							hits.Add($"{IOPath.GetFileName(sourceFile.Path)} matches `{pattern}`");
					}
				}

				Assert.True(hits.Count == 0, $"{artifactName} must not use legacy patterns: {string.Join("; ", hits)}");
			}

			static void AssertMinimumMatches(IEnumerable<Phase9SourceFile> sourceFiles, Regex pattern, int minimumCount, string message)
			{
				var matchCount = sourceFiles.Sum(sourceFile => pattern.Matches(sourceFile.Text).Count);
				Assert.True(matchCount >= minimumCount, $"{message} Found {matchCount}, expected at least {minimumCount}.");
			}

			static void AssertContains(IEnumerable<Phase9SourceFile> sourceFiles, Regex pattern, string message)
			{
				Assert.True(sourceFiles.Any(sourceFile => pattern.IsMatch(sourceFile.Text)), message);
			}

			static void AssertContains(string text, string pattern, string message)
			{
				Assert.True(Regex.IsMatch(text, pattern, RegexOptions), message);
			}

			static IReadOnlyList<Phase9SourceFile> GetCurrentSurfaceSources(Phase9Project project)
			{
				var currentSurfaceSources = project.ProductSources
					.Where(sourceFile => ComponentPattern.IsMatch(sourceFile.Text)
						|| RenderPattern.IsMatch(sourceFile.Text)
						|| ReactiveOrSetStatePattern.IsMatch(sourceFile.Text)
						|| TypedNavigationPattern.IsMatch(sourceFile.Text))
					.ToList();

				Assert.NotEmpty(currentSurfaceSources);
				return currentSurfaceSources;
			}
		}

		sealed class Phase9Project
		{
			public Phase9Project(string projectPath, string projectDirectory, string projectFileText, IReadOnlyList<Phase9SourceFile> allSources)
			{
				ProjectPath = projectPath;
				ProjectDirectory = projectDirectory;
				ProjectFileText = projectFileText;
				AllSources = allSources;
				ProductSources = allSources.Where(sourceFile => !IsBootstrapFile(sourceFile.Path)).ToList();
			}

			public string ProjectPath { get; }
			public string ProjectDirectory { get; }
			public string ProjectFileText { get; }
			public IReadOnlyList<Phase9SourceFile> AllSources { get; }
			public IReadOnlyList<Phase9SourceFile> ProductSources { get; }

			static bool IsBootstrapFile(string path)
			{
				var fileName = IOPath.GetFileName(path);
				return fileName.Equals("MauiProgram.cs", StringComparison.OrdinalIgnoreCase)
					|| fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)
					|| fileName.Equals("AppShell.cs", StringComparison.OrdinalIgnoreCase)
					|| fileName.EndsWith("App.cs", StringComparison.OrdinalIgnoreCase);
			}
		}

		readonly record struct Phase9SourceFile(string Path, string Text);

		sealed class SyntheticFixture : IDisposable
		{
			SyntheticFixture(string rootDirectory, string projectPath, string guidePath = null)
			{
				RootDirectory = rootDirectory;
				ProjectPath = projectPath;
				GuidePath = guidePath;
			}

			public string RootDirectory { get; }
			public string ProjectPath { get; }
			public string GuidePath { get; }

			public static SyntheticFixture CreateCounterFixture()
			{
				return CreateProjectFixture(
					"counter-current",
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFrameworks>net10.0-android;net10.0-ios;net10.0-maccatalyst</TargetFrameworks>
							<UseMaui>true</UseMaui>
							<SingleProject>true</SingleProject>
						</PropertyGroup>
						<ItemGroup>
							<ProjectReference Include="..\..\src\Comet\Comet.csproj" />
						</ItemGroup>
					</Project>
					""",
					("MauiProgram.cs",
					"""
					using Comet;
					using Microsoft.Maui.Hosting;

					namespace CounterCurrent;

					public static class MauiProgram
					{
						public static MauiApp CreateMauiApp()
						{
							var builder = MauiApp.CreateBuilder();
							builder.UseCometApp<CounterApp>();
							return builder.Build();
						}
					}
					"""),
					("CounterApp.cs",
					"""
					using Comet;

					namespace CounterCurrent;

					public sealed class CounterApp : CometApp
					{
						[Body]
						View body() => new CounterPage();
					}
					"""),
					("CounterPage.cs",
					"""
					using Comet;

					namespace CounterCurrent;

					public sealed class CounterPage : Component<CounterState>
					{
						public override View Render() => new VStack
						{
							new Button("Increment", () => SetState(state => state.Count++)),
							new Text(() => $"Count: {State.Count}"),
						};
					}

					public sealed class CounterState
					{
						public int Count { get; set; }
					}
					"""));
			}

			public static SyntheticFixture CreateLegacyCounterFixture()
			{
				return CreateProjectFixture(
					"counter-legacy",
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFrameworks>net10.0-android;net10.0-ios;net10.0-maccatalyst</TargetFrameworks>
							<UseMaui>true</UseMaui>
							<SingleProject>true</SingleProject>
						</PropertyGroup>
						<ItemGroup>
							<ProjectReference Include="..\..\src\Comet\Comet.csproj" />
						</ItemGroup>
					</Project>
					""",
					("MauiProgram.cs",
					"""
					using Comet;
					using Microsoft.Maui.Hosting;

					namespace CounterLegacy;

					public static class MauiProgram
					{
						public static MauiApp CreateMauiApp()
						{
							var builder = MauiApp.CreateBuilder();
							builder.UseCometApp<LegacyApp>();
							return builder.Build();
						}
					}
					"""),
					("LegacyCounterPage.cs",
					"""
					using Comet;

					namespace CounterLegacy;

					public sealed class LegacyCounterPage : Component
					{
						readonly State<int> count = 0;

						public override View Render() => new VStack
						{
							new Button("Increment", () => count.Value++),
							new Text(() => $"Count: {count.Value}"),
						};
					}
					"""));
			}

			public static SyntheticFixture CreateCoffeeFixture()
			{
				return CreateProjectFixture(
					"coffee-current",
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFrameworks>net10.0-android;net10.0-ios;net10.0-maccatalyst</TargetFrameworks>
							<UseMaui>true</UseMaui>
							<SingleProject>true</SingleProject>
						</PropertyGroup>
						<ItemGroup>
							<ProjectReference Include="..\..\src\Comet\Comet.csproj" />
						</ItemGroup>
					</Project>
					""",
					("MauiProgram.cs",
					"""
					using Comet;
					using Microsoft.Maui.Hosting;

					namespace CoffeeCurrent;

					public static class MauiProgram
					{
						public static MauiApp CreateMauiApp()
						{
							var builder = MauiApp.CreateBuilder();
							builder.UseCometApp<CoffeeApp>();
							return builder.Build();
						}
					}
					"""),
					("CoffeeApp.cs",
					"""
					using Comet;

					namespace CoffeeCurrent;

					public sealed class CoffeeApp : CometApp
					{
						[Body]
						View body() => new DashboardPage();
					}
					"""),
					("DashboardPage.cs",
					"""
					using Comet;

					namespace CoffeeCurrent;

					public sealed class DashboardPage : Component<DashboardState>
					{
						public override View Render() => new TabView
						{
							new NavigationView { new ShotListPage() }.TabText("Shots"),
							new NavigationView { new ProfilePage() }.TabText("Profile"),
						};
					}

					public sealed class DashboardState
					{
						public string SelectedTab { get; set; } = "Shots";
					}
					"""),
					("ShotListPage.cs",
					"""
					using Comet;

					namespace CoffeeCurrent;

					public sealed class ShotListPage : Component
					{
						readonly Reactive<int> shotCount = 3;

						public override View Render() => new VStack
						{
							new Text(() => $"Shots logged: {shotCount.Value}"),
							new CollectionView<int>(new[] { 1, 2, 3 }, value => new Text(() => $"Shot {value}")),
						};
					}
					"""),
					("ProfilePage.cs",
					"""
					using Comet;

					namespace CoffeeCurrent;

					public sealed class ProfilePage : Component<ProfileState>
					{
						public override View Render() => new VStack
						{
							new Text(() => $"Preferred beans: {State.BeanCount}"),
							new Button("Add", () => SetState(state => state.BeanCount++)),
						};
					}

					public sealed class ProfileState
					{
						public int BeanCount { get; set; }
					}
					"""));
			}

			public static SyntheticFixture CreateMigrationGuideFixture()
			{
				var rootDirectory = CreateRoot("migration-guide");
				var guidePath = IOPath.Combine(rootDirectory, "MIGRATION_GUIDE.md");
				File.WriteAllText(guidePath,
					"""
					# Counter + Coffee migration guide

					This guide migrates the counter sample and the coffee app from `View` + `[Body]` + `State<T>` to `Component`, `Render()`, `Reactive<T>`, and `SetState`.

					## Build order

					```bash
					dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
					dotnet build src/Comet/Comet.csproj -c Release
					dotnet build tests/Comet.Tests/Comet.Tests.csproj -c Release
					dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release
					```

					## Navigation

					Use `CometShell.RegisterRoute<TView>()` and `GoToAsync<TView>()` for the coffee flow.

					## MAUI 10 notes

					Prefer `CollectionView` over `ListView`, `Border` over `Frame`, `DisplayAlertAsync` over `DisplayAlert`, and `MainThread` over `Device.BeginInvokeOnMainThread`.
					""");

				return new SyntheticFixture(rootDirectory, projectPath: null, guidePath: guidePath);
			}

			static SyntheticFixture CreateProjectFixture(string name, string projectText, params (string relativePath, string content)[] files)
			{
				var rootDirectory = CreateRoot(name);
				var projectPath = IOPath.Combine(rootDirectory, $"{name}.csproj");
				File.WriteAllText(projectPath, projectText);
				foreach (var (relativePath, content) in files)
				{
					var fullPath = IOPath.Combine(rootDirectory, relativePath);
					var directory = IOPath.GetDirectoryName(fullPath);
					if (!string.IsNullOrWhiteSpace(directory))
						Directory.CreateDirectory(directory);
					File.WriteAllText(fullPath, content);
				}

				return new SyntheticFixture(rootDirectory, projectPath);
			}

			static string CreateRoot(string name)
			{
				var rootDirectory = IOPath.Combine(IOPath.GetTempPath(), $"comet-phase9-{name}-{Guid.NewGuid():N}");
				Directory.CreateDirectory(rootDirectory);
				return rootDirectory;
			}

			public void Dispose()
			{
				if (Directory.Exists(RootDirectory))
					Directory.Delete(RootDirectory, recursive: true);
			}
		}
	}
}
