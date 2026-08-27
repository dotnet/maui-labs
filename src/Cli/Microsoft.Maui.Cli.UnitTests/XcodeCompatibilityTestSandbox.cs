// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.Services;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.ManualTests.XcodeCompatibility;

/// <summary>
/// Manual test sandbox for Xcode compatibility check feature.
/// This app demonstrates the end-to-end flow without requiring full CLI integration.
/// </summary>
class Program
{
	static async Task Main(string[] args)
	{
		Console.WriteLine("=== Xcode Compatibility Check - Manual Test Sandbox ===\n");

		if (!PlatformDetector.IsMacOS)
		{
			Console.WriteLine("⚠️  This test only runs on macOS");
			return;
		}

		try
		{
			// Test 1: Direct XcodeCompatibilityChecker usage
			Console.WriteLine("Test 1: Direct XcodeCompatibilityChecker");
			Console.WriteLine("----------------------------------------");
			TestDirectChecker();
			Console.WriteLine();

			// Test 2: AppleProvider integration
			Console.WriteLine("Test 2: AppleProvider CheckHealth Integration");
			Console.WriteLine("--------------------------------------------");
			await TestAppleProviderIntegration();
			Console.WriteLine();

			// Test 3: DoctorService integration
			Console.WriteLine("Test 3: DoctorService Integration");
			Console.WriteLine("---------------------------------");
			await TestDoctorServiceIntegration();
			Console.WriteLine();

			// Test 4: Fix application
			Console.WriteLine("Test 4: Fix Application (Dry Run)");
			Console.WriteLine("-------------------------------");
			await TestFixApplication();
			Console.WriteLine();

			Console.WriteLine("✅ All manual tests completed successfully");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"❌ Test failed: {ex.Message}");
			Console.WriteLine(ex.StackTrace);
		}
	}

	static void TestDirectChecker()
	{
		var checker = new XcodeCompatibilityChecker(xcodeManager: null);
		var result = checker.CheckXcodeCompatibility();

		Console.WriteLine($"Category: {result.Category}");
		Console.WriteLine($"Name: {result.Name}");
		Console.WriteLine($"Status: {result.Status}");
		Console.WriteLine($"Message: {result.Message}");
		Console.WriteLine($"Details: {(result.Details != null ? "Present" : "None")}");
		Console.WriteLine($"Fix: {(result.Fix != null ? "Present" : "None")}");

		if (result.Fix != null)
		{
			Console.WriteLine($"  - Issue ID: {result.Fix.IssueId}");
			Console.WriteLine($"  - Description: {result.Fix.Description}");
			Console.WriteLine($"  - Auto-Fixable: {result.Fix.AutoFixable}");
			Console.WriteLine($"  - Command: {result.Fix.Command}");
		}
	}

	static async Task TestAppleProviderIntegration()
	{
		try
		{
			var appleProvider = new AppleProvider();
			var checks = appleProvider.CheckHealth();

			Console.WriteLine($"Total health checks: {checks.Count}");

			var compatibilityCheck = checks.FirstOrDefault(c => c.Name == "Xcode Compatibility");
			if (compatibilityCheck != null)
			{
				Console.WriteLine($"\nXcode Compatibility Check Found:");
				Console.WriteLine($"  Status: {compatibilityCheck.Status}");
				Console.WriteLine($"  Message: {compatibilityCheck.Message}");
				Console.WriteLine($"  Auto-Fixable: {compatibilityCheck.Fix?.AutoFixable ?? false}");

				if (compatibilityCheck.Details != null)
				{
					Console.WriteLine($"  Details: {compatibilityCheck.Details}");
				}
			}
			else
			{
				Console.WriteLine("⚠️  Xcode Compatibility Check not found in health checks");
				Console.WriteLine($"Available checks: {string.Join(", ", checks.Select(c => c.Name))}");
			}

			await Task.CompletedTask;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"⚠️  AppleProvider test skipped or failed: {ex.Message}");
		}
	}

	static async Task TestDoctorServiceIntegration()
	{
		try
		{
			var doctorService = new DoctorService();
			var report = await doctorService.RunAllChecksAsync();

			Console.WriteLine($"Doctor Report Status: {report.Status}");
			Console.WriteLine($"Summary: {report.Summary.Ok} OK, {report.Summary.Warning} Warning, {report.Summary.Error} Error");
			Console.WriteLine($"Total Checks: {report.Summary.Total}");

			var fixableChecks = report.Checks.Where(c => c.Fix?.AutoFixable == true).ToList();
			Console.WriteLine($"\nAuto-Fixable Issues: {fixableChecks.Count}");
			foreach (var check in fixableChecks)
			{
				Console.WriteLine($"  - {check.Name}: {check.Fix?.Command}");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"⚠️  DoctorService test failed: {ex.Message}");
		}
	}

	static async Task TestFixApplication()
	{
		var doctorService = new DoctorService();

		// Test with a simple echo command (harmless dry run)
		var fix = new FixInfo
		{
			IssueId = "E1001",
			Description = "Test fix (dry run)",
			AutoFixable = true,
			Command = "echo xcode-select would be called here"
		};

		Console.WriteLine($"Attempting to apply fix: {fix.Description}");
		Console.WriteLine($"Command: {fix.Command}");

		var success = await doctorService.TryFixAsync(fix);
		Console.WriteLine($"Fix Result: {(success ? "✅ Success" : "❌ Failed")}");

		// Test parsing
		var (fileName, args) = DoctorService.ParseCommand("xcode-select -s /Applications/Xcode-26.5.app");
		Console.WriteLine($"\nCommand Parsing Test:");
		Console.WriteLine($"  File: {fileName}");
		Console.WriteLine($"  Args: [{string.Join(", ", args.Select(a => $"\"{a}\""))}]");
	}
}
