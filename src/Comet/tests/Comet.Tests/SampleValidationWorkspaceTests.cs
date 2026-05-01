using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests
{
	public class SampleValidationWorkspaceTests
	{
		[Fact]
		public void InitWorkspaceCreatesScaffoldForAllSamples()
		{
			using var workspace = new TestWorkspace();

			var initResult = RunScript(workspace, "init", "--workspace-root", workspace.RootDirectory, "--repo-root", FindRepoRoot());
			Assert.Equal(0, initResult.ExitCode);

			var report = LoadReport(workspace);
			var sampleNames = report.RootElement.GetProperty("samples")
				.EnumerateArray()
				.Select(sample => sample.GetProperty("name").GetString())
				.ToHashSet(StringComparer.Ordinal);

			var expectedSamples = new HashSet<string>(StringComparer.Ordinal)
			{
				"CometMauiApp",
				"CometBaristaNotes",
				"Comet.Sample",
				"CometFeatureShowcase",
				"CometProjectManager",
				"MauiReference",
				"CometAllTheLists",
				"CometTaskApp",
				"CometWeather",
				"CometStressTest",
			};

			Assert.Equal(expectedSamples, sampleNames);
			Assert.True(File.Exists(IOPath.Combine(workspace.RootDirectory, "sample-validation", "reports", "sample-validation-report.md")));
			Assert.True(File.Exists(IOPath.Combine(workspace.RootDirectory, "sample-validation", "checklists", "CometMauiApp.md")));
			Assert.True(File.Exists(IOPath.Combine(workspace.RootDirectory, "sample-validation", "issues", "CometBaristaNotes.md")));
			Assert.True(Directory.Exists(IOPath.Combine(workspace.RootDirectory, "sample-validation", "comparisons", "MauiReference")));
		}

		[Fact]
		public void ValidateRejectsRuntimeVerifiedSampleWithoutEvidence()
		{
			using var workspace = new TestWorkspace();
			Assert.Equal(0, RunScript(workspace, "init", "--workspace-root", workspace.RootDirectory, "--repo-root", FindRepoRoot()).ExitCode);

			UpdateSample(workspace, "CometMauiApp", sample =>
			{
				var baseline = sample["baseline"]!.AsObject();
				baseline["status"] = "baseline_captured";

				var evolved = sample["evolved"]!.AsObject();
				evolved["status"] = "runtime_verified";

				sample["overall_status"] = "runtime_verified";
			});

			var validateResult = RunScript(workspace, "validate", "--workspace-root", workspace.RootDirectory);
			Assert.NotEqual(0, validateResult.ExitCode);
			Assert.Contains("runtime_verified requires evolved screenshots", validateResult.StandardError, StringComparison.Ordinal);
		}

		[Fact]
		public void ValidateAcceptsRuntimeBlockedSampleWhenBlockerEvidenceExists()
		{
			using var workspace = new TestWorkspace();
			Assert.Equal(0, RunScript(workspace, "init", "--workspace-root", workspace.RootDirectory, "--repo-root", FindRepoRoot()).ExitCode);

			var logRelativePath = "baselines/CometMauiApp/logs/original-launch-attempt.log.txt";
			var logAbsolutePath = IOPath.Combine(workspace.RootDirectory, "sample-validation", "baselines", "CometMauiApp", "logs", "original-launch-attempt.log.txt");
			Directory.CreateDirectory(IOPath.GetDirectoryName(logAbsolutePath)!);
			File.WriteAllText(logAbsolutePath, "launch attempt log");

			UpdateSample(workspace, "CometMauiApp", sample =>
			{
				var baseline = sample["baseline"]!.AsObject();
				baseline["status"] = "runtime_blocked";
				baseline["attempted_commands"] = JsonArray("dotnet build -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64");
				baseline["logs"] = JsonArray(logRelativePath);
				baseline["blockers"] = JsonArray("Missing runtime automation wiring for meaningful interaction capture.");
			});

			var validateResult = RunScript(workspace, "validate", "--workspace-root", workspace.RootDirectory);
			Assert.Equal(0, validateResult.ExitCode);
		}

		[Fact]
		public void ValidateRejectsFixedIssueWithoutRerunEvidence()
		{
			using var workspace = new TestWorkspace();
			Assert.Equal(0, RunScript(workspace, "init", "--workspace-root", workspace.RootDirectory, "--repo-root", FindRepoRoot()).ExitCode);

			var discoveryRelativePath = "baselines/CometBaristaNotes/logs/original-crash.log.txt";
			var discoveryAbsolutePath = IOPath.Combine(workspace.RootDirectory, "sample-validation", discoveryRelativePath);
			Directory.CreateDirectory(IOPath.GetDirectoryName(discoveryAbsolutePath)!);
			File.WriteAllText(discoveryAbsolutePath, "crash details");

			UpdateSample(workspace, "CometBaristaNotes", sample =>
			{
				sample["issues"] = new JsonArray
				{
					new JsonObject
					{
						["id"] = "barista-001",
						["title"] = "Launch crash",
						["severity"] = "blocking",
						["status"] = "fixed",
						["discovery_evidence"] = JsonArray(discoveryRelativePath),
						["rerun_evidence"] = JsonArray(),
					},
				};
			});

			var validateResult = RunScript(workspace, "validate", "--workspace-root", workspace.RootDirectory);
			Assert.NotEqual(0, validateResult.ExitCode);
			Assert.Contains("requires rerun evidence", validateResult.StandardError, StringComparison.Ordinal);
		}

		static void UpdateSample(TestWorkspace workspace, string sampleName, Action<JsonObject> mutate)
		{
			var reportPath = IOPath.Combine(workspace.RootDirectory, "sample-validation", "reports", "sample-validation-report.json");
			var document = JsonNode.Parse(File.ReadAllText(reportPath))!.AsObject();
			var sample = document["samples"]!.AsArray()
				.Select(node => node!.AsObject())
				.Single(item => string.Equals(item["name"]!.GetValue<string>(), sampleName, StringComparison.Ordinal));

			mutate(sample);
			File.WriteAllText(reportPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
		}

		static JsonArray JsonArray(params string[] values)
		{
			var array = new JsonArray();
			foreach (var value in values)
				array.Add(value);

			return array;
		}

		static JsonDocument LoadReport(TestWorkspace workspace)
		{
			var reportPath = IOPath.Combine(workspace.RootDirectory, "sample-validation", "reports", "sample-validation-report.json");
			return JsonDocument.Parse(File.ReadAllText(reportPath));
		}

		static CommandResult RunScript(TestWorkspace workspace, params string[] arguments)
		{
			var repoRoot = FindRepoRoot();
			var toolPath = IOPath.Combine(repoRoot, "tools", "sample_validation_workspace.py");
			var process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = "python3",
					WorkingDirectory = repoRoot,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				},
			};

			process.StartInfo.ArgumentList.Add(toolPath);
			foreach (var argument in arguments)
				process.StartInfo.ArgumentList.Add(argument);

			process.Start();
			var standardOutput = process.StandardOutput.ReadToEnd();
			var standardError = process.StandardError.ReadToEnd();
			process.WaitForExit();

			return new CommandResult(process.ExitCode, standardOutput, standardError);
		}

		static string FindRepoRoot()
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory is not null)
			{
				if (File.Exists(IOPath.Combine(directory.FullName, "global.json")) &&
					File.Exists(IOPath.Combine(directory.FullName, "Directory.Build.props")))
					return directory.FullName;

				directory = directory.Parent;
			}

			throw new InvalidOperationException("Unable to locate repository root.");
		}

		readonly record struct CommandResult(int ExitCode, string StandardOutput, string StandardError);

		sealed class TestWorkspace : IDisposable
		{
			public TestWorkspace()
			{
				RootDirectory = IOPath.Combine(IOPath.GetTempPath(), $"comet-sample-validation-{Guid.NewGuid():N}");
				Directory.CreateDirectory(RootDirectory);
			}

			public string RootDirectory { get; }

			public void Dispose()
			{
				if (Directory.Exists(RootDirectory))
					Directory.Delete(RootDirectory, recursive: true);
			}
		}
	}
}
