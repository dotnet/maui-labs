// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using Microsoft.Maui.Cli.Commands.Go;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class GoUpgradeTests : IDisposable
{
	readonly string _tempRoot;
	readonly string _fakeRepoRoot;
	readonly string _fakeCometCsproj;
	readonly string _projectDir;

	public GoUpgradeTests()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), $"maui-go-upgrade-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempRoot);

		// Fake repo root: create .git/ marker + the Comet csproj path + minimal Resources tree.
		_fakeRepoRoot = Path.Combine(_tempRoot, "fake-repo");
		Directory.CreateDirectory(Path.Combine(_fakeRepoRoot, ".git"));

		var cometDir = Path.Combine(_fakeRepoRoot, "src", "Comet", "src", "Comet");
		Directory.CreateDirectory(cometDir);
		_fakeCometCsproj = Path.Combine(cometDir, "Comet.csproj");
		File.WriteAllText(_fakeCometCsproj, "<Project />");

		var resources = Path.Combine(_fakeRepoRoot, "src", "Comet", "sample", "Comet.Sample", "Resources");
		Directory.CreateDirectory(Path.Combine(resources, "AppIcon"));
		Directory.CreateDirectory(Path.Combine(resources, "Splash"));
		File.WriteAllText(Path.Combine(resources, "AppIcon", "appicon.svg"), "<svg/>");
		File.WriteAllText(Path.Combine(resources, "AppIcon", "appiconfg.svg"), "<svg/>");
		File.WriteAllText(Path.Combine(resources, "Splash", "splash.svg"), "<svg/>");

		// Project lives inside the fake repo so FindRepoRoot() succeeds.
		_projectDir = Path.Combine(_fakeRepoRoot, "samples", "MyGoApp");
		Directory.CreateDirectory(_projectDir);
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
	}

	const string DefaultGoSource = """
		#:package Comet

		using Comet;
		using Microsoft.Maui;
		using Microsoft.Maui.Graphics;
		using static Comet.CometControls;

		namespace MyGoApp;

		public class MainPage : View
		{
		    [Body]
		    View body() => Text("Hi");
		}
		""";

	void WriteUserFile(string contents, string fileName = "MyGoApp.cs")
	{
		File.WriteAllText(Path.Combine(_projectDir, fileName), contents);
	}

	GoUpgradeOptions Options(bool dryRun = false, bool force = false, bool build = false, bool keepBackup = true) =>
		new(_projectDir, force, dryRun, build, keepBackup);

	[Fact]
	public async Task DefaultCreateOutput_UpgradeWritesAllFilesAndStripsDirectives()
	{
		WriteUserFile(DefaultGoSource);

		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);

		Assert.Equal(0, result.ExitCode);
		Assert.NotNull(result.Detection);
		Assert.Equal("MainPage", result.Detection!.RootViewClassName);
		Assert.Equal("MyGoApp", result.Detection.Namespace);
		Assert.Equal("MyGoAppCometApp", result.Detection.AppClassName);

		// Generated files exist.
		Assert.True(File.Exists(Path.Combine(_projectDir, "MyGoApp.csproj")));
		Assert.True(File.Exists(Path.Combine(_projectDir, "MauiProgram.cs")));
		Assert.True(File.Exists(Path.Combine(_projectDir, "Platforms", "iOS", "Program.cs")));
		Assert.True(File.Exists(Path.Combine(_projectDir, "Platforms", "iOS", "AppDelegate.cs")));
		Assert.True(File.Exists(Path.Combine(_projectDir, "Platforms", "iOS", "Info.plist")));
		Assert.True(File.Exists(Path.Combine(_projectDir, "Platforms", "MacCatalyst", "AppDelegate.cs")));
		Assert.True(File.Exists(Path.Combine(_projectDir, "Platforms", "Android", "MainActivity.cs")));
		Assert.True(File.Exists(Path.Combine(_projectDir, "Platforms", "Android", "MainApplication.cs")));
		Assert.True(File.Exists(Path.Combine(_projectDir, "Platforms", "Android", "AndroidManifest.xml")));

		// Resources copied.
		Assert.True(File.Exists(Path.Combine(_projectDir, "Resources", "AppIcon", "appicon.svg")));
		Assert.True(File.Exists(Path.Combine(_projectDir, "Resources", "Splash", "splash.svg")));

		// Directives stripped from user file.
		var stripped = File.ReadAllText(Path.Combine(_projectDir, "MyGoApp.cs"));
		Assert.DoesNotContain("#:package", stripped);
		Assert.Contains("namespace MyGoApp;", stripped);
		Assert.Contains("public class MainPage : View", stripped);

		// MauiProgram references root view.
		var mp = File.ReadAllText(Path.Combine(_projectDir, "MauiProgram.cs"));
		Assert.Contains("UseCometApp<MyGoAppCometApp>", mp);
		Assert.Contains("new MainPage()", mp);

		// Backup directory exists with original .cs.
		Assert.NotNull(result.BackupDir);
		var backupCs = Directory.EnumerateFiles(result.BackupDir!, "MyGoApp.cs", SearchOption.AllDirectories).Single();
		Assert.Contains("#:package Comet", File.ReadAllText(backupCs));
	}

	[Fact]
	public async Task RenamedNamespace_PreservedVerbatim()
	{
		WriteUserFile("""
			#:package Comet
			using Comet;

			namespace Acme.Pretty.Stuff;

			public class MainPage : View { [Body] View body() => Text("x"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);
		Assert.Equal(0, result.ExitCode);
		Assert.Equal("Acme.Pretty.Stuff", result.Detection!.Namespace);
		var csproj = File.ReadAllText(Path.Combine(_projectDir, "MyGoApp.csproj"));
		Assert.Contains("<RootNamespace>Acme.Pretty.Stuff</RootNamespace>", csproj);
		var mp = File.ReadAllText(Path.Combine(_projectDir, "MauiProgram.cs"));
		Assert.Contains("namespace Acme.Pretty.Stuff;", mp);
	}

	[Fact]
	public async Task RenamedRootView_PickedUpAutomatically()
	{
		WriteUserFile("""
			#:package Comet
			using Comet;
			namespace MyGoApp;
			public class HomeScreen : View { [Body] View body() => Text("x"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);
		Assert.Equal(0, result.ExitCode);
		Assert.Equal("HomeScreen", result.Detection!.RootViewClassName);
		var mp = File.ReadAllText(Path.Combine(_projectDir, "MauiProgram.cs"));
		Assert.Contains("new HomeScreen()", mp);
	}

	[Fact]
	public async Task MultipleViewSubclasses_RefusesWithHint()
	{
		WriteUserFile("""
			#:package Comet
			using Comet;
			namespace MyGoApp;
			public class HomeScreen : View { [Body] View body() => Text("a"); }
			public class SettingsScreen : View { [Body] View body() => Text("b"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);
		Assert.NotEqual(0, result.ExitCode);
		Assert.Contains(result.Messages, m => m.Contains("HomeScreen") && m.Contains("SettingsScreen"));
	}

	[Fact]
	public async Task ExistingMauiProgram_RefusesWithoutForce()
	{
		WriteUserFile(DefaultGoSource);
		File.WriteAllText(Path.Combine(_projectDir, "MauiProgram.cs"), "// preexisting");

		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);
		Assert.NotEqual(0, result.ExitCode);
		Assert.Contains(result.Messages, m => m.Contains("MauiProgram.cs already exists"));
	}

	[Fact]
	public async Task DryRun_WritesNothing()
	{
		WriteUserFile(DefaultGoSource);
		var beforeFiles = Directory.EnumerateFiles(_projectDir, "*", SearchOption.AllDirectories).OrderBy(s => s).ToList();

		var result = await GoUpgradeRunner.RunAsync(Options(dryRun: true), null, CancellationToken.None);

		Assert.Equal(0, result.ExitCode);
		var afterFiles = Directory.EnumerateFiles(_projectDir, "*", SearchOption.AllDirectories).OrderBy(s => s).ToList();
		Assert.Equal(beforeFiles, afterFiles);

		// Original file still has its directive.
		Assert.Contains("#:package Comet", File.ReadAllText(Path.Combine(_projectDir, "MyGoApp.cs")));
	}

	[Fact]
	public async Task AppClassCollision_RefusesEvenWithForce()
	{
		WriteUserFile("""
			#:package Comet
			using Comet;
			namespace MyGoApp;
			public class MauiProgram { }
			public class MainPage : View { [Body] View body() => Text("x"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(force: true), null, CancellationToken.None);
		Assert.NotEqual(0, result.ExitCode);
		Assert.Contains(result.Messages, m => m.Contains("collide"));
	}

	[Fact]
	public async Task PackageDirective_WithVersion_RendersPackageReference()
	{
		WriteUserFile("""
			#:package Comet
			#:package Acme.Lib@1.2.3
			using Comet;
			namespace MyGoApp;
			public class MainPage : View { [Body] View body() => Text("x"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);
		Assert.Equal(0, result.ExitCode);
		var csproj = File.ReadAllText(Path.Combine(_projectDir, "MyGoApp.csproj"));
		Assert.Contains("<PackageReference Include=\"Acme.Lib\" Version=\"1.2.3\" />", csproj);
	}

	[Fact]
	public async Task PropertyDirective_RendersInPropertyGroup()
	{
		WriteUserFile("""
			#:package Comet
			#:property LangVersion=preview
			using Comet;
			namespace MyGoApp;
			public class MainPage : View { [Body] View body() => Text("x"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);
		Assert.Equal(0, result.ExitCode);
		var csproj = File.ReadAllText(Path.Combine(_projectDir, "MyGoApp.csproj"));
		Assert.Contains("<LangVersion>preview</LangVersion>", csproj);
	}

	[Fact]
	public async Task NoCsFile_Refuses()
	{
		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);
		Assert.NotEqual(0, result.ExitCode);
		Assert.Contains(result.Messages, m => m.Contains("No .cs file"));
	}

	[Fact]
	public async Task MultipleCsFiles_Refuses()
	{
		WriteUserFile(DefaultGoSource, "MyGoApp.cs");
		WriteUserFile(DefaultGoSource, "Helper.cs");

		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);
		Assert.NotEqual(0, result.ExitCode);
		Assert.Contains(result.Messages, m => m.Contains("v1 upgrade supports a single-file"));
	}

	[Fact]
	public async Task NoCometDirective_Refuses()
	{
		WriteUserFile("""
			using Microsoft.Maui;
			namespace MyGoApp;
			public class MainPage : View { }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);
		Assert.NotEqual(0, result.ExitCode);
		Assert.Contains(result.Messages, m => m.Contains("does not declare `#:package Comet`"));
	}

	[Fact]
	public async Task Build_InvokesProcessRunnerWithMacCatalystOnMacOs()
	{
		// Only meaningful when host is macOS — skip otherwise.
		if (!OperatingSystem.IsMacOS()) return;

		WriteUserFile(DefaultGoSource);
		var fake = new FakeRunner(exitCode: 0);

		var opts = new GoUpgradeOptions(_projectDir, false, false, true, true, fake);
		var result = await GoUpgradeRunner.RunAsync(opts, null, CancellationToken.None);

		Assert.Equal(0, result.ExitCode);
		Assert.True(result.BuildAttempted);
		Assert.Equal(0, result.BuildExitCode);
		Assert.Equal("net11.0-maccatalyst", fake.LastTfm);
	}

	[Fact]
	public async Task Build_PropagatesNonZeroExitCode()
	{
		if (!OperatingSystem.IsMacOS()) return;

		WriteUserFile(DefaultGoSource);
		var fake = new FakeRunner(exitCode: 7, tail: new[] { "fail line 1", "fail line 2" });

		var opts = new GoUpgradeOptions(_projectDir, false, false, true, true, fake);
		var result = await GoUpgradeRunner.RunAsync(opts, null, CancellationToken.None);

		Assert.Equal(7, result.ExitCode);
		Assert.True(result.BuildAttempted);
		Assert.Equal(7, result.BuildExitCode);
		Assert.Contains(result.Messages, m => m.Contains("build verification failed"));
	}

	sealed class FakeRunner : IGoUpgradeProcessRunner
	{
		readonly int _exit;
		readonly IReadOnlyList<string> _tail;
		public string? LastTfm { get; private set; }

		public FakeRunner(int exitCode, IReadOnlyList<string>? tail = null)
		{
			_exit = exitCode;
			_tail = tail ?? Array.Empty<string>();
		}

		public Task<(int ExitCode, IReadOnlyList<string> OutputTail)> RunDotnetBuildAsync(string workingDir, string targetFramework, Action<string>? lineSink, CancellationToken ct)
		{
			LastTfm = targetFramework;
			return Task.FromResult((_exit, _tail));
		}
	}

	[Fact]
	public async Task RecordCollision_Detected()
	{
		WriteUserFile("""
			#:package Comet
			using Comet;
			namespace MyGoApp;
			public record MauiProgram(string Name);
			public class MainPage : View { [Body] View body() => Text("x"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(force: true), null, CancellationToken.None);
		Assert.NotEqual(0, result.ExitCode);
		Assert.Contains(result.Messages, m => m.Contains("collide"));
	}

	[Fact]
	public async Task StructCollision_Detected()
	{
		WriteUserFile("""
			#:package Comet
			using Comet;
			namespace MyGoApp;
			public struct Program { }
			public class MainPage : View { [Body] View body() => Text("x"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(force: true), null, CancellationToken.None);
		Assert.NotEqual(0, result.ExitCode);
		Assert.Contains(result.Messages, m => m.Contains("collide"));
	}

	[Fact]
	public async Task InterfaceCollision_Detected()
	{
		WriteUserFile("""
			#:package Comet
			using Comet;
			namespace MyGoApp;
			public interface MauiProgram { }
			public class MainPage : View { [Body] View body() => Text("x"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(force: true), null, CancellationToken.None);
		Assert.NotEqual(0, result.ExitCode);
		Assert.Contains(result.Messages, m => m.Contains("collide"));
	}

	[Fact]
	public async Task RecordClassCollision_CometAppSuffix_Detected()
	{
		WriteUserFile("""
			#:package Comet
			using Comet;
			namespace MyGoApp;
			public record class MyGoAppCometApp(string X);
			public class MainPage : View { [Body] View body() => Text("x"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(force: true), null, CancellationToken.None);
		Assert.NotEqual(0, result.ExitCode);
		Assert.Contains(result.Messages, m => m.Contains("collide"));
	}

	[Fact]
	public async Task NonCollidingRecord_PassesSuccessfully()
	{
		WriteUserFile("""
			#:package Comet
			using Comet;
			namespace MyGoApp;
			public record ViewModel(string Name);
			public class MainPage : View { [Body] View body() => Text("x"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);
		Assert.Equal(0, result.ExitCode);
	}

	[Fact]
	public async Task CometPackageCaseInsensitive_AcceptsLowercase()
	{
		WriteUserFile("""
			#:package comet
			using Comet;
			namespace MyGoApp;
			public class MainPage : View { [Body] View body() => Text("x"); }
			""");

		var result = await GoUpgradeRunner.RunAsync(Options(), null, CancellationToken.None);
		Assert.Equal(0, result.ExitCode);
		Assert.Equal("MainPage", result.Detection!.RootViewClassName);
	}

	[Fact]
	public void ToApplicationId_StripsNonAsciiCharacters()
	{
		var id = GoFileBasedDetector.ToApplicationId("MiAño");
		Assert.Equal("com.cometgo.miao", id);
		Assert.DoesNotMatch("[^a-z0-9.]", id);
	}

	[Fact]
	public void ToApplicationId_AllNonAscii_FallsBackToHash()
	{
		var id = GoFileBasedDetector.ToApplicationId("日本語");
		Assert.StartsWith("com.cometgo.app", id);
		Assert.Matches("^com\\.cometgo\\.app[0-9a-f]{8}$", id);
	}

	[Fact]
	public void ParseDirectives_RejectsPropertyNameWithXmlSpecialChars()
	{
		var src = "#:property Foo>Bar=value\n\nclass MainPage : View { }\n";
		var (directives, _) = GoFileBasedDetector.ParseDirectives(src);
		Assert.Single(directives);
		Assert.IsType<GoMalformedDirective>(directives[0]);
	}

	[Fact]
	public void ParseDirectives_RejectsPackageNameWithAngleBracket()
	{
		var src = "#:package Evil<Inject\n\nclass MainPage : View { }\n";
		var (directives, _) = GoFileBasedDetector.ParseDirectives(src);
		Assert.Single(directives);
		Assert.IsType<GoMalformedDirective>(directives[0]);
	}

	[Fact]
	public void ParseDirectives_RejectsSdkValueWithQuote()
	{
		var src = "#:sdk Bad\"Sdk\n\nclass MainPage : View { }\n";
		var (directives, _) = GoFileBasedDetector.ParseDirectives(src);
		Assert.Single(directives);
		Assert.IsType<GoMalformedDirective>(directives[0]);
	}

	[Fact]
	public void ParseDirectives_AcceptsValidPropertyAndPackage()
	{
		var src = "#:property LangVersion=latest\n#:package Foo.Bar@1.2.3-preview\n\nclass MainPage : View { }\n";
		var (directives, _) = GoFileBasedDetector.ParseDirectives(src);
		Assert.Equal(2, directives.Count);
		Assert.IsType<GoPropertyDirective>(directives[0]);
		Assert.IsType<GoPackageDirective>(directives[1]);
	}
}
