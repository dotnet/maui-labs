// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class PlatformsTests
{
	[Theory]
	[InlineData("android", true)]
	[InlineData("ios", true)]
	[InlineData("maccatalyst", true)]
	[InlineData("windows", true)]
	[InlineData("all", true)]
	[InlineData("ANDROID", true)]
	[InlineData("IOS", true)]
	[InlineData("invalid", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void IsValid_ReturnsCorrectResult(string? platform, bool expected)
	{
		Assert.Equal(expected, Platforms.IsValid(platform));
	}

	[Theory]
	[InlineData("android", "android")]
	[InlineData("ANDROID", "android")]
	[InlineData("ios", "ios")]
	[InlineData("apple", "ios")]
	[InlineData("iphone", "ios")]
	[InlineData("ipad", "ios")]
	[InlineData("mac", "maccatalyst")]
	[InlineData("macos", "maccatalyst")]
	[InlineData("catalyst", "maccatalyst")]
	[InlineData("maccatalyst", "maccatalyst")]
	[InlineData("windows", "windows")]
	[InlineData("win", "windows")]
	[InlineData("win32", "windows")]
	[InlineData("win64", "windows")]
	[InlineData(null, "all")]
	[InlineData("unknown", "unknown")]
	public void Normalize_ReturnsCorrectResult(string? input, string expected)
	{
		Assert.Equal(expected, Platforms.Normalize(input));
	}

	[Fact]
	public void Supported_ContainsEveryValueAcceptedByIsValid()
	{
		Assert.All(Platforms.Supported, p => Assert.True(Platforms.IsValid(p), $"'{p}' is listed as supported but rejected by IsValid"));
		Assert.Equal(new[] { "android", "ios", "maccatalyst", "windows", "all" }, Platforms.Supported);
	}

	[Fact]
	public void Supported_IsJoinableAsAUserFacingList()
	{
		// Regression: `string.Join(", ", Platforms.All)` bound to the IEnumerable<char>
		// overload and rendered "a, l, l" in the unknown-platform warning.
		Assert.Equal("android, ios, maccatalyst, windows, all", string.Join(", ", Platforms.Supported));
	}
}
