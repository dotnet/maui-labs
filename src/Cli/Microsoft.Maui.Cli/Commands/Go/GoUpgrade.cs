// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.Cli.Commands.Go;

/// <summary>
/// Options for <see cref="GoUpgradeRunner.RunAsync"/>.
/// </summary>
internal sealed record GoUpgradeOptions(
	string Cwd,
	bool Force,
	bool DryRun,
	bool Build,
	bool KeepBackup,
	IGoUpgradeProcessRunner? ProcessRunner = null,
	Func<DateTimeOffset>? Clock = null);

/// <summary>
/// Surface for shelling out the optional `dotnet build`. Injectable so tests
/// can run upgrade end-to-end without invoking dotnet.
/// </summary>
internal interface IGoUpgradeProcessRunner
{
	Task<(int ExitCode, IReadOnlyList<string> OutputTail)> RunDotnetBuildAsync(
		string workingDir,
		string targetFramework,
		Action<string>? lineSink,
		CancellationToken ct);
}

internal sealed class DefaultGoUpgradeProcessRunner : IGoUpgradeProcessRunner
{
	public async Task<(int ExitCode, IReadOnlyList<string> OutputTail)> RunDotnetBuildAsync(
		string workingDir,
		string targetFramework,
		Action<string>? lineSink,
		CancellationToken ct)
	{
		const int TailSize = 30;
		var tail = new Queue<string>(TailSize);
		var tailLock = new object();

		var psi = new ProcessStartInfo
		{
			FileName = "dotnet",
			WorkingDirectory = workingDir,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		psi.ArgumentList.Add("build");
		psi.ArgumentList.Add("-f");
		psi.ArgumentList.Add(targetFramework);
		psi.ArgumentList.Add("-nologo");
		psi.ArgumentList.Add("-v:minimal");

		using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

		void OnLine(string? line)
		{
			if (line is null) return;
			lineSink?.Invoke(line);
			lock (tailLock)
			{
				if (tail.Count >= TailSize) tail.Dequeue();
				tail.Enqueue(line);
			}
		}

		proc.OutputDataReceived += (_, e) => OnLine(e.Data);
		proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
		proc.Start();
		proc.BeginOutputReadLine();
		proc.BeginErrorReadLine();

		try
		{
			await proc.WaitForExitAsync(ct).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			try { proc.Kill(entireProcessTree: true); } catch { }
			throw;
		}
		string[] result;
		lock (tailLock)
		{
			result = tail.ToArray();
		}
		return (proc.ExitCode, result);
	}
}

/// <summary>
/// Outcome of <see cref="GoUpgradeRunner.RunAsync"/>.
/// </summary>
internal sealed record GoUpgradeResult(
	int ExitCode,
	GoUpgradeDetection? Detection,
	IReadOnlyList<string> Messages,
	string? BackupDir,
	bool BuildAttempted,
	int? BuildExitCode);

/// <summary>
/// Result of inspecting the cwd for a Comet Go file-based program.
/// </summary>
internal sealed record GoUpgradeDetection(
	string UserFilePath,
	string OriginalSource,
	string SourceWithoutDirectives,
	string Namespace,
	string RootViewClassName,
	string DirName,
	string SafeName,
	string AppClassName,
	string ApplicationId,
	IReadOnlyList<GoDirective> Directives,
	string CometProjectRelativePath,
	string RepoRoot);

internal abstract record GoDirective
{
	public required string OriginalLine { get; init; }
}

internal sealed record GoPackageDirective(string Name, string? Version) : GoDirective;

internal sealed record GoCometPackageDirective(string? Version) : GoDirective;

internal sealed record GoPropertyDirective(string Name, string Value) : GoDirective;

internal sealed record GoSdkDirective(string Sdk) : GoDirective;

internal sealed record GoMalformedDirective(string Reason) : GoDirective;

internal static class GoUpgradeRunner
{
	public static async Task<GoUpgradeResult> RunAsync(GoUpgradeOptions options, Action<string>? log, CancellationToken ct)
	{
		var messages = new List<string>();
		void Log(string m) { messages.Add(m); log?.Invoke(m); }

		// 1. Detect.
		var (detection, errors) = GoFileBasedDetector.Detect(options.Cwd, options.Force);
		if (errors.Count > 0)
		{
			foreach (var e in errors) Log(e);
			return new GoUpgradeResult(1, null, messages, null, false, null);
		}

		Debug.Assert(detection is not null);
		Log($"Detected Comet Go file: {Path.GetFileName(detection!.UserFilePath)}");
		Log($"  Namespace:    {detection.Namespace}");
		Log($"  Root view:    {detection.RootViewClassName}");
		Log($"  App class:    {detection.AppClassName}");
		Log($"  App id:       {detection.ApplicationId}");

		// 2. Plan files.
		var plan = GoUpgradeScaffolder.BuildPlan(detection);

		// 3. Verify resource sources exist before any write (pre-flight).
		var resources = GoUpgradeScaffolder.ResourceSources(detection.RepoRoot);
		var missing = resources.Where(r => !File.Exists(r.Source)).ToList();
		if (missing.Count > 0)
		{
			Log("Missing required resource files in repo (cannot upgrade):");
			foreach (var m in missing) Log($"  {m.Source}");
			return new GoUpgradeResult(1, detection, messages, null, false, null);
		}

		// 4. Pre-flight collision checks (existing files we'd write under non-force).
		if (!options.Force)
		{
			var conflicts = plan
				.Where(f => File.Exists(Path.Combine(options.Cwd, f.RelativePath)))
				.Select(f => f.RelativePath)
				.ToList();
			conflicts.AddRange(resources
				.Where(r => File.Exists(Path.Combine(options.Cwd, r.RelativePath)))
				.Select(r => r.RelativePath));
			if (conflicts.Count > 0)
			{
				Log("Refusing to overwrite existing files (use --force to overwrite known-generated files):");
				foreach (var c in conflicts) Log($"  {c}");
				return new GoUpgradeResult(1, detection, messages, null, false, null);
			}
		}

		// 5. Dry-run preview.
		if (options.DryRun)
		{
			Log("");
			Log("[dry-run] Would write:");
			foreach (var f in plan) Log($"  {f.RelativePath}");
			foreach (var r in resources) Log($"  {r.RelativePath}");
			Log($"[dry-run] Would strip {detection.OriginalSource.Length - detection.SourceWithoutDirectives.Length} bytes of #: directives from {Path.GetFileName(detection.UserFilePath)}");
			if (options.KeepBackup) Log("[dry-run] Would back up the original .cs to .maui-go-backup/<timestamp>/");
			return new GoUpgradeResult(0, detection, messages, null, false, null);
		}

		// 6. Backup.
		string? backupDir = null;
		if (options.KeepBackup)
		{
			var clock = options.Clock ?? (() => DateTimeOffset.UtcNow);
			var ts = clock().ToString("yyyy-MM-ddTHH-mm-ss", CultureInfo.InvariantCulture);
			backupDir = Path.Combine(options.Cwd, ".maui-go-backup", ts);
			Directory.CreateDirectory(backupDir);
			File.Copy(detection.UserFilePath, Path.Combine(backupDir, Path.GetFileName(detection.UserFilePath)));
			// Also back up any pre-existing generated files we're about to overwrite (under --force).
			foreach (var f in plan)
			{
				var dest = Path.Combine(options.Cwd, f.RelativePath);
				if (File.Exists(dest))
				{
					var rel = f.RelativePath.Replace('/', Path.DirectorySeparatorChar);
					var backupTarget = Path.Combine(backupDir, rel);
					Directory.CreateDirectory(Path.GetDirectoryName(backupTarget)!);
					File.Copy(dest, backupTarget, overwrite: true);
				}
			}
			foreach (var r in resources)
			{
				var dest = Path.Combine(options.Cwd, r.RelativePath);
				if (File.Exists(dest))
				{
					var rel = r.RelativePath.Replace('/', Path.DirectorySeparatorChar);
					var backupTarget = Path.Combine(backupDir, rel);
					Directory.CreateDirectory(Path.GetDirectoryName(backupTarget)!);
					File.Copy(dest, backupTarget, overwrite: true);
				}
			}
			Log($"Backup: {Path.GetRelativePath(options.Cwd, backupDir)}");
		}

		// 7. Write generated files.
		foreach (var f in plan)
		{
			var dest = Path.Combine(options.Cwd, f.RelativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
			File.WriteAllText(dest, f.Content);
			Log($"  wrote {f.RelativePath}");
		}

		// 8. Copy resources.
		foreach (var r in resources)
		{
			var dest = Path.Combine(options.Cwd, r.RelativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
			File.Copy(r.Source, dest, overwrite: true);
			Log($"  wrote {r.RelativePath}");
		}

		// 9. Strip directives from user file (last, so if anything above fails the original file is untouched).
		File.WriteAllText(detection.UserFilePath, detection.SourceWithoutDirectives);
		Log($"  stripped #: directives from {Path.GetFileName(detection.UserFilePath)}");

		// 10. Optional build.
		bool buildAttempted = false;
		int? buildExit = null;
		if (options.Build)
		{
			var hostTfm = ResolveHostTfm();
			if (hostTfm is null)
			{
				Log("--build: host platform not supported for build verification in v1; skipping.");
			}
			else
			{
				buildAttempted = true;
				Log($"Building -f {hostTfm} ...");
				var runner = options.ProcessRunner ?? new DefaultGoUpgradeProcessRunner();
				var (exit, tail) = await runner.RunDotnetBuildAsync(options.Cwd, hostTfm, line => log?.Invoke(line), ct).ConfigureAwait(false);
				buildExit = exit;
				if (exit != 0)
				{
					Log("");
					Log("Scaffold succeeded; build verification failed.");
					Log("Last lines of build output:");
					foreach (var line in tail) Log("  " + line);
					if (backupDir is not null)
						Log($"Original .cs preserved at: {Path.GetRelativePath(options.Cwd, backupDir)}");
					return new GoUpgradeResult(exit, detection, messages, backupDir, true, exit);
				}
				Log("Build OK.");
			}
		}

		Log("");
		Log("Upgrade complete.");
		Log("Next:");
		Log("  dotnet build -t:Run -f net11.0-ios");

		return new GoUpgradeResult(0, detection, messages, backupDir, buildAttempted, buildExit);
	}

	static string? ResolveHostTfm()
	{
		if (OperatingSystem.IsMacOS()) return "net11.0-maccatalyst";
		// Windows/Linux: skipped in v1 (Windows TFM not generated; Android needs SDK probing).
		return null;
	}
}

internal static class GoFileBasedDetector
{
	static readonly Regex DirectiveLine = new(@"^#:(\w+)\s+(.*)$", RegexOptions.Compiled);
	static readonly Regex FileScopedNs = new(@"^\s*namespace\s+([A-Za-z_][\w.]*)\s*;\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
	static readonly Regex BlockNs = new(@"^\s*namespace\s+([A-Za-z_][\w.]*)\s*\{", RegexOptions.Multiline | RegexOptions.Compiled);
	// Class deriving from View. Tolerates `: View`, `: View, IFoo`, or `: SomeBase, View`.
	// Captures the class name; we then check the base list for `View`.
	static readonly Regex ClassDecl = new(@"^\s*(?:public|internal|sealed|abstract|partial|static|\s)*\s*class\s+([A-Za-z_]\w*)\s*(?::\s*([^\{]+))?\{?", RegexOptions.Multiline | RegexOptions.Compiled);
	// Any type declaration (class, record, struct, interface) for collision detection.
	// Handles compound forms: `record class`, `record struct`.
	static readonly Regex TypeDecl = new(@"^\s*(?:public|internal|sealed|abstract|partial|static|ref|readonly|\s)*\s*(?:record\s+class|record\s+struct|class|record|struct|interface)\s+([A-Za-z_]\w*)", RegexOptions.Multiline | RegexOptions.Compiled);

	static readonly string[] CollisionTypeNames = new[] { "MauiProgram", "AppDelegate", "MainActivity", "MainApplication", "Program" };

	public static (GoUpgradeDetection? detection, IList<string> errors) Detect(string cwd, bool force)
	{
		var errors = new List<string>();

		// 1. Reject if csproj / Platforms / MauiProgram.cs already present (unless --force).
		if (!force)
		{
			if (Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).Any())
			{
				errors.Add("Refusing to upgrade: a .csproj already exists here. Use --force to re-run.");
				return (null, errors);
			}
			if (Directory.Exists(Path.Combine(cwd, "Platforms")))
			{
				errors.Add("Refusing to upgrade: Platforms/ already exists here. Use --force to re-run.");
				return (null, errors);
			}
			if (File.Exists(Path.Combine(cwd, "MauiProgram.cs")))
			{
				errors.Add("Refusing to upgrade: MauiProgram.cs already exists here. Use --force to re-run.");
				return (null, errors);
			}
		}

		// 2. Find candidate .cs files at top level.
		var allCs = Directory.EnumerateFiles(cwd, "*.cs", SearchOption.TopDirectoryOnly).ToList();
		// Filter out files we'd generate (MauiProgram.cs) since they may exist under --force re-run.
		var candidates = allCs
			.Where(p => !string.Equals(Path.GetFileName(p), "MauiProgram.cs", StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (candidates.Count == 0)
		{
			errors.Add("No .cs file found in the current directory. Run `maui go create <Name>` first.");
			return (null, errors);
		}
		if (candidates.Count > 1)
		{
			errors.Add($"Found {candidates.Count} .cs files; v1 upgrade supports a single-file Comet Go project only.");
			return (null, errors);
		}

		var userFile = candidates[0];
		var source = File.ReadAllText(userFile);

		// 3. Parse leading #: directives.
		var (directives, sourceWithoutDirectives) = ParseDirectives(source);
		var hasCometMarker = directives.OfType<GoCometPackageDirective>().Any();
		if (!hasCometMarker)
		{
			errors.Add($"`{Path.GetFileName(userFile)}` does not declare `#:package Comet`; not a Comet Go project.");
			return (null, errors);
		}

		// Warn about malformed directives (don't abort, just inform).
		foreach (var bad in directives.OfType<GoMalformedDirective>())
		{
			Console.Error.WriteLine($"Warning: skipped malformed directive: {bad.Reason}");
		}

		// 4. Find root view class.
		var rootClass = FindRootViewClass(sourceWithoutDirectives, errors);
		if (rootClass is null) return (null, errors);

		// 5. Namespace inference (read from the *post-strip* source for stable line offsets).
		var ns = ResolveNamespace(sourceWithoutDirectives);
		if (string.IsNullOrEmpty(ns))
		{
			errors.Add("Could not infer a namespace from the file. v1 requires a `namespace X;` declaration.");
			return (null, errors);
		}

		// 6. App-class collision check.
		var collisions = ScanCollisions(sourceWithoutDirectives);
		var dirName = new DirectoryInfo(cwd).Name;
		var safeName = ToSafeName(dirName);
		var appClassName = $"{safeName}CometApp";
		var generatedTypes = new HashSet<string>(StringComparer.Ordinal) { "MauiProgram", appClassName, "AppDelegate", "MainActivity", "MainApplication", "Program" };
		var collided = collisions.Where(c => generatedTypes.Contains(c)).ToList();
		if (collided.Count > 0)
		{
			errors.Add("The user file declares types that collide with the upgrade scaffold. Rename them and re-run:");
			foreach (var c in collided) errors.Add($"  {c}");
			return (null, errors);
		}

		// 7. Repo root + Comet ProjectReference path.
		var repoRoot = FindRepoRoot(cwd);
		if (repoRoot is null)
		{
			errors.Add("`maui go upgrade` must be run inside the maui-labs repository (Comet is not yet on NuGet).");
			return (null, errors);
		}
		var cometCsproj = Path.Combine(repoRoot, "src", "Comet", "src", "Comet", "Comet.csproj");
		if (!File.Exists(cometCsproj))
		{
			errors.Add($"Could not locate Comet.csproj at expected path: {cometCsproj}");
			return (null, errors);
		}
		var rel = Path.GetRelativePath(cwd, cometCsproj).Replace(Path.DirectorySeparatorChar, '/');

		var appId = ToApplicationId(dirName);

		return (new GoUpgradeDetection(
			UserFilePath: userFile,
			OriginalSource: source,
			SourceWithoutDirectives: sourceWithoutDirectives,
			Namespace: ns,
			RootViewClassName: rootClass,
			DirName: dirName,
			SafeName: safeName,
			AppClassName: appClassName,
			ApplicationId: appId,
			Directives: directives,
			CometProjectRelativePath: rel,
			RepoRoot: repoRoot), errors);
	}

	internal static (IReadOnlyList<GoDirective> directives, string sourceWithoutDirectives) ParseDirectives(string source)
	{
		var lines = source.Split('\n');
		var directives = new List<GoDirective>();
		int strippedThrough = -1; // index into lines of last directive line (or trailing blank)

		for (int i = 0; i < lines.Length; i++)
		{
			var raw = lines[i].TrimEnd('\r');
			if (string.IsNullOrWhiteSpace(raw))
			{
				// allow blank lines among/after directives; advance strippedThrough only if we already saw at least one directive
				if (directives.Count > 0) strippedThrough = i;
				continue;
			}
			var m = DirectiveLine.Match(raw);
			if (!m.Success)
			{
				break; // first non-directive non-blank line — stop.
			}
			var kind = m.Groups[1].Value;
			var rest = m.Groups[2].Value.Trim();
			directives.Add(ParseSingleDirective(kind, rest, raw));
			strippedThrough = i;
		}

		if (strippedThrough < 0)
			return (directives, source);

		// Rebuild source without the leading directive block.
		// Trim a single trailing blank line if it follows the last directive (cosmetic).
		var keep = lines.Skip(strippedThrough + 1).ToArray();
		// Drop one leading blank if the user separated directives from code with one.
		// (Already handled because trailing blanks were absorbed into strippedThrough.)
		var rebuilt = string.Join("\n", keep);
		return (directives, rebuilt);
	}

	static GoDirective ParseSingleDirective(string kind, string rest, string raw)
	{
		switch (kind)
		{
			case "package":
			{
				// Form: <Name>[@<version>]
				var at = rest.IndexOf('@');
				string name; string? ver;
				if (at >= 0) { name = rest[..at].Trim(); ver = rest[(at + 1)..].Trim(); }
				else { name = rest.Trim(); ver = null; }
				if (string.IsNullOrEmpty(name))
					return new GoMalformedDirective($"`#:package` directive missing package name: {raw}") { OriginalLine = raw };
				if (!IsValidPackageName(name))
					return new GoMalformedDirective($"`#:package` invalid package name `{name}`: {raw}") { OriginalLine = raw };
				if (ver is not null && !IsValidVersionString(ver))
					return new GoMalformedDirective($"`#:package` invalid version `{ver}`: {raw}") { OriginalLine = raw };
				if (string.Equals(name, "Comet", StringComparison.OrdinalIgnoreCase))
					return new GoCometPackageDirective(ver) { OriginalLine = raw };
				return new GoPackageDirective(name, ver) { OriginalLine = raw };
			}
			case "property":
			{
				var eq = rest.IndexOf('=');
				if (eq <= 0)
					return new GoMalformedDirective($"`#:property` requires `Name=Value`: {raw}") { OriginalLine = raw };
				var name = rest[..eq].Trim();
				var val = rest[(eq + 1)..].Trim();
				if (string.IsNullOrEmpty(name))
					return new GoMalformedDirective($"`#:property` missing name: {raw}") { OriginalLine = raw };
				if (!IsValidMsbuildPropertyName(name))
					return new GoMalformedDirective($"`#:property` invalid name `{name}` (must match `[A-Za-z_][A-Za-z0-9_]*`): {raw}") { OriginalLine = raw };
				return new GoPropertyDirective(name, val) { OriginalLine = raw };
			}
			case "sdk":
			{
				if (string.IsNullOrEmpty(rest))
					return new GoMalformedDirective($"`#:sdk` requires a value: {raw}") { OriginalLine = raw };
				if (!IsValidSdkName(rest))
					return new GoMalformedDirective($"`#:sdk` invalid value `{rest}`: {raw}") { OriginalLine = raw };
				return new GoSdkDirective(rest) { OriginalLine = raw };
			}
			default:
				return new GoMalformedDirective($"Unknown directive `#:{kind}`: {raw}") { OriginalLine = raw };
		}
	}

	static bool IsValidMsbuildPropertyName(string s)
		=> System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Za-z_][A-Za-z0-9_]*$");

	static bool IsValidPackageName(string s)
		=> System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Za-z0-9][A-Za-z0-9._-]*$");

	static bool IsValidVersionString(string s)
		=> System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Za-z0-9][A-Za-z0-9.+\\-]*$");

	static bool IsValidSdkName(string s)
		=> System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Za-z0-9][A-Za-z0-9._/-]*$");

	static string? FindRootViewClass(string source, IList<string> errors)
	{
		var matches = ClassDecl.Matches(source);
		var viewClasses = new List<string>();
		foreach (Match m in matches)
		{
			var name = m.Groups[1].Value;
			var bases = m.Groups[2].Success ? m.Groups[2].Value : string.Empty;
			// Tokenize on commas, trim generic args.
			if (BaseListContainsView(bases))
				viewClasses.Add(name);
		}
		if (viewClasses.Count == 0)
		{
			errors.Add("No class deriving from `View` found in the file. v1 requires at least one `class X : View`.");
			return null;
		}
		// Prefer a class named MainPage (the `go create` default).
		var main = viewClasses.FirstOrDefault(n => string.Equals(n, "MainPage", StringComparison.Ordinal));
		if (main is not null) return main;
		if (viewClasses.Count == 1) return viewClasses[0];
		errors.Add($"Found {viewClasses.Count} classes deriving from `View`. v1 requires exactly one. (Found: {string.Join(", ", viewClasses)}.)");
		return null;
	}

	static bool BaseListContainsView(string bases)
	{
		if (string.IsNullOrWhiteSpace(bases)) return false;
		// Remove generic arguments.
		var stripped = Regex.Replace(bases, @"<[^>]*>", string.Empty);
		var tokens = stripped.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (var t in tokens)
		{
			// Allow fully qualified `Comet.View`.
			if (t == "View" || t.EndsWith(".View", StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	static string ResolveNamespace(string source)
	{
		var fileScoped = FileScopedNs.Match(source);
		if (fileScoped.Success) return fileScoped.Groups[1].Value;
		var block = BlockNs.Match(source);
		if (block.Success) return block.Groups[1].Value;
		return string.Empty;
	}

	static IReadOnlyList<string> ScanCollisions(string source)
	{
		var found = new List<string>();
		foreach (Match m in TypeDecl.Matches(source))
		{
			var name = m.Groups[1].Value;
			if (CollisionTypeNames.Contains(name) || name.EndsWith("CometApp", StringComparison.Ordinal))
				found.Add(name);
		}
		return found;
	}

	internal static string ToSafeName(string raw)
	{
		var sb = new StringBuilder();
		foreach (var ch in raw)
		{
			sb.Append((char.IsLetterOrDigit(ch)) ? ch : '_');
		}
		var s = sb.ToString();
		if (s.Length == 0) return "App";
		if (char.IsDigit(s[0])) s = "_" + s;
		return s;
	}

	internal static string ToApplicationId(string dirName)
	{
		var sb = new StringBuilder();
		foreach (var ch in dirName)
		{
			if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
				sb.Append(char.ToLowerInvariant(ch));
		}
		var slug = sb.ToString();
		if (string.IsNullOrEmpty(slug))
		{
			// Stable fallback hash
			var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(dirName)))
				.ToLowerInvariant().Substring(0, 8);
			return $"com.cometgo.app{hash}";
		}
		if (char.IsDigit(slug[0])) slug = "app" + slug;
		return $"com.cometgo.{slug}";
	}

	internal static string? FindRepoRoot(string startDir)
	{
		var dir = new DirectoryInfo(Path.GetFullPath(startDir));
		while (dir is not null)
		{
			if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
				|| File.Exists(Path.Combine(dir.FullName, "MauiLabs.sln")))
				return dir.FullName;
			dir = dir.Parent;
		}
		return null;
	}
}

internal sealed record GoScaffoldFile(string RelativePath, string Content);

internal sealed record GoResourceCopy(string RelativePath, string Source);

internal static class GoUpgradeScaffolder
{
	public static IReadOnlyList<GoScaffoldFile> BuildPlan(GoUpgradeDetection d)
	{
		var files = new List<GoScaffoldFile>
		{
			new($"{d.SafeName}.csproj", GoUpgradeTemplates.RenderCsproj(d)),
			new("MauiProgram.cs", GoUpgradeTemplates.RenderMauiProgram(d)),
			new("Platforms/iOS/Program.cs", GoUpgradeTemplates.RenderIosProgram(d)),
			new("Platforms/iOS/AppDelegate.cs", GoUpgradeTemplates.RenderIosAppDelegate(d)),
			new("Platforms/iOS/Info.plist", GoUpgradeTemplates.IosInfoPlist),
			new("Platforms/MacCatalyst/Program.cs", GoUpgradeTemplates.RenderIosProgram(d)),
			new("Platforms/MacCatalyst/AppDelegate.cs", GoUpgradeTemplates.RenderIosAppDelegate(d)),
			new("Platforms/MacCatalyst/Info.plist", GoUpgradeTemplates.MacCatalystInfoPlist),
			new("Platforms/MacCatalyst/Entitlements.Debug.plist", GoUpgradeTemplates.MacCatalystEntitlementsDebugPlist),
			new("Platforms/Android/MainActivity.cs", GoUpgradeTemplates.RenderAndroidMainActivity(d)),
			new("Platforms/Android/MainApplication.cs", GoUpgradeTemplates.RenderAndroidMainApplication(d)),
			new("Platforms/Android/AndroidManifest.xml", GoUpgradeTemplates.AndroidManifest),
		};
		return files;
	}

	public static IReadOnlyList<GoResourceCopy> ResourceSources(string repoRoot)
	{
		var src = Path.Combine(repoRoot, "src", "Comet", "sample", "Comet.Sample", "Resources");
		return new[]
		{
			new GoResourceCopy("Resources/AppIcon/appicon.svg", Path.Combine(src, "AppIcon", "appicon.svg")),
			new GoResourceCopy("Resources/AppIcon/appiconfg.svg", Path.Combine(src, "AppIcon", "appiconfg.svg")),
			new GoResourceCopy("Resources/Splash/splash.svg", Path.Combine(src, "Splash", "splash.svg")),
		};
	}
}

internal static class GoUpgradeTemplates
{
	public static string RenderCsproj(GoUpgradeDetection d)
	{
		var sdk = d.Directives.OfType<GoSdkDirective>().LastOrDefault()?.Sdk ?? "Microsoft.NET.Sdk";
		var props = string.Concat(d.Directives.OfType<GoPropertyDirective>()
			.Select(p => $"    <{p.Name}>{System.Security.SecurityElement.Escape(p.Value)}</{p.Name}>{Environment.NewLine}"));
		var pkgs = string.Concat(d.Directives.OfType<GoPackageDirective>()
			.Select(p => p.Version is null
				? $"    <PackageReference Include=\"{p.Name}\" />{Environment.NewLine}"
				: $"    <PackageReference Include=\"{p.Name}\" Version=\"{p.Version}\" />{Environment.NewLine}"));

		return $"""
<Project Sdk="{sdk}">

  <PropertyGroup>
    <TargetFrameworks>net11.0-android;net11.0-ios;net11.0-maccatalyst</TargetFrameworks>
    <OutputType>Exe</OutputType>
    <RootNamespace>{d.Namespace}</RootNamespace>
    <UseMaui>true</UseMaui>
    <SingleProject>true</SingleProject>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ApplicationTitle>{System.Security.SecurityElement.Escape(d.DirName)}</ApplicationTitle>
    <ApplicationId>{d.ApplicationId}</ApplicationId>
    <ApplicationVersion>1</ApplicationVersion>
    <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
    <NoWarn>XC0103</NoWarn>
    <SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">21.0</SupportedOSPlatformVersion>
    <SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">17.0</SupportedOSPlatformVersion>
    <SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'">17.0</SupportedOSPlatformVersion>
{props}  </PropertyGroup>

  <ItemGroup>
    <MauiIcon Include="Resources\AppIcon\appicon.svg" ForegroundFile="Resources\AppIcon\appiconfg.svg" Color="#000000" />
    <MauiSplashScreen Include="Resources\Splash\splash.svg" Color="#000000" />
    <MauiImage Include="Resources\Images\*" />
    <MauiFont Include="Resources\Fonts\*.ttf" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="{System.Security.SecurityElement.Escape(d.CometProjectRelativePath)}" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" />
{pkgs}  </ItemGroup>

</Project>
""";
	}

	public static string RenderMauiProgram(GoUpgradeDetection d) => $$"""
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace {{d.Namespace}};

public sealed class {{d.AppClassName}} : CometApp
{
	public {{d.AppClassName}}()
	{
		Body = CreateRootView;
	}

	public static View CreateRootView() => new {{d.RootViewClassName}}();
}

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseCometApp<{{d.AppClassName}}>();
		return builder.Build();
	}
}
""";

	public static string RenderIosProgram(GoUpgradeDetection d) => $$"""
using ObjCRuntime;
using UIKit;

namespace {{d.Namespace}};

public class Program
{
	static void Main(string[] args)
	{
		UIApplication.Main(args, null, typeof(AppDelegate));
	}
}
""";

	public static string RenderIosAppDelegate(GoUpgradeDetection d) => $$"""
using Foundation;
using UIKit;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace {{d.Namespace}};

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
""";

	public const string IosInfoPlist = """
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>LSRequiresIPhoneOS</key>
	<true/>
	<key>UIDeviceFamily</key>
	<array>
		<integer>1</integer>
		<integer>2</integer>
	</array>
	<key>UIRequiredDeviceCapabilities</key>
	<array>
		<string>arm64</string>
	</array>
	<key>UISupportedInterfaceOrientations</key>
	<array>
		<string>UIInterfaceOrientationPortrait</string>
		<string>UIInterfaceOrientationLandscapeLeft</string>
		<string>UIInterfaceOrientationLandscapeRight</string>
	</array>
	<key>UISupportedInterfaceOrientations~ipad</key>
	<array>
		<string>UIInterfaceOrientationPortrait</string>
		<string>UIInterfaceOrientationPortraitUpsideDown</string>
		<string>UIInterfaceOrientationLandscapeLeft</string>
		<string>UIInterfaceOrientationLandscapeRight</string>
	</array>
	<key>XSAppIconAssets</key>
	<string>Assets.xcassets/appicon.appiconset</string>
	<key>UIUserInterfaceStyle</key>
	<string>Light</string>
</dict>
</plist>
""";

	public const string MacCatalystInfoPlist = """
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>LSRequiresIPhoneOS</key>
	<true/>
	<key>UIDeviceFamily</key>
	<array>
		<integer>1</integer>
		<integer>2</integer>
		<integer>6</integer>
	</array>
	<key>UIRequiredDeviceCapabilities</key>
	<array>
		<string>arm64</string>
	</array>
	<key>UISupportedInterfaceOrientations</key>
	<array>
		<string>UIInterfaceOrientationPortrait</string>
		<string>UIInterfaceOrientationLandscapeLeft</string>
		<string>UIInterfaceOrientationLandscapeRight</string>
	</array>
	<key>UISupportedInterfaceOrientations~ipad</key>
	<array>
		<string>UIInterfaceOrientationPortrait</string>
		<string>UIInterfaceOrientationPortraitUpsideDown</string>
		<string>UIInterfaceOrientationLandscapeLeft</string>
		<string>UIInterfaceOrientationLandscapeRight</string>
	</array>
	<key>XSAppIconAssets</key>
	<string>Assets.xcassets/appicon.appiconset</string>
	<key>UIUserInterfaceStyle</key>
	<string>Light</string>
</dict>
</plist>
""";

	public const string MacCatalystEntitlementsDebugPlist = """
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
</dict>
</plist>
""";

	public static string RenderAndroidMainActivity(GoUpgradeDetection d) => $$"""
using Microsoft.Maui;
using Android.App;
using Android.Content.PM;
using Android.OS;

namespace {{d.Namespace}};

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
""";

	public static string RenderAndroidMainApplication(GoUpgradeDetection d) => $$"""
using System;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Android.App;
using Android.Runtime;

namespace {{d.Namespace}};

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
""";

	public const string AndroidManifest = """
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
	<application android:allowBackup="true" android:icon="@mipmap/appicon" android:roundIcon="@mipmap/appicon_round" android:supportsRtl="true"></application>
	<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
	<uses-permission android:name="android.permission.INTERNET" />
</manifest>
""";
}
