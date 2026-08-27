// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Apple;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests.Providers.Apple;

/// <summary>
/// Unit tests for XcodeCompatibilityChecker.
/// 
/// Note: Full integration tests with actual XcodeManager are covered in DoctorServiceTests,
/// which tests the entire CheckHealth() flow via FakeAppleProvider.
/// These unit tests focus on isolated logic that can be tested without external dependencies.
/// </summary>
public class XcodeCompatibilityCheckerTests
{
	[Fact]
	public void CheckXcodeCompatibility_WithNullXcodeManager_ReturnsSkipped()
	{
		// Arrange
		var checker = new XcodeCompatibilityChecker(xcodeManager: null);

		// Act
		var result = checker.CheckXcodeCompatibility();

		// Assert
		Assert.Equal(CheckStatus.Skipped, result.Status);
		Assert.Contains("not available", result.Message);
		Assert.Equal("apple", result.Category);
		Assert.Equal("Xcode Compatibility", result.Name);
	}

	[Theory]
	[InlineData("26.5.1", "26.5")]
	[InlineData("26.5", "26.5")]
	[InlineData("26", "26")]
	[InlineData("16.0.1", "16.0")]
	[InlineData("16.0", "16.0")]
	public void ExtractMajorMinor_WithVersionStrings_ReturnsCorrectFormat(string input, string expected)
	{
		// Act
		var result = InvokeExtractMajorMinor(input);

		// Assert
		Assert.Equal(expected, result);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void ExtractMajorMinor_WithNullOrWhitespaceVersion_ReturnsNull(string? input)
	{
		// Act
		var result = InvokeExtractMajorMinor(input);

		// Assert
		Assert.Null(result);
	}

	// Helper method to invoke the private ExtractMajorMinor static method via reflection
	private static string? InvokeExtractMajorMinor(string? version)
	{
		var method = typeof(XcodeCompatibilityChecker).GetMethod(
			"ExtractMajorMinor",
			System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

		if (method == null)
			throw new InvalidOperationException("ExtractMajorMinor method not found on XcodeCompatibilityChecker");

		return (string?)method.Invoke(null, new object?[] { version });
	}
}
