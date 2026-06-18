// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Providers.Android;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="SdkManager.TryParseInstallProgressLine"/>, the pure parser that turns
/// streamed <c>sdkmanager</c> stdout lines into (phase, percent) so the install progress bar
/// reflects real download/extraction progress instead of appearing to hang.
/// </summary>
public class SdkManagerProgressParserTests
{
	[Fact]
	public void TryParse_DownloadingLineWithPercent_ReturnsDownloadingAndPercent()
	{
		var ok = SdkManager.TryParseInstallProgressLine(
			"[=========                    ] 33% Downloading system-image.zip",
			out var phase, out var percent);

		Assert.True(ok);
		Assert.Equal("Downloading", phase);
		Assert.Equal(33, percent);
	}

	[Fact]
	public void TryParse_UnzippingLineWithPercent_ReturnsUnzippingAndPercent()
	{
		var ok = SdkManager.TryParseInstallProgressLine(
			"[====================         ] 78% Unzipping... google_apis/arm64-v8a/system.img",
			out var phase, out var percent);

		Assert.True(ok);
		Assert.Equal("Unzipping", phase);
		Assert.Equal(78, percent);
	}

	[Fact]
	public void TryParse_PercentWithoutSpaceBeforeSign_IsParsed()
	{
		var ok = SdkManager.TryParseInstallProgressLine("[=====] 5 % Downloading", out var phase, out var percent);

		Assert.True(ok);
		Assert.Equal("Downloading", phase);
		Assert.Equal(5, percent);
	}

	[Fact]
	public void TryParse_PercentWithUnknownTrailingText_ReturnsPercentWithEmptyPhase()
	{
		var ok = SdkManager.TryParseInstallProgressLine("[==========          ] 50%", out var phase, out var percent);

		Assert.True(ok);
		Assert.Equal(string.Empty, phase);
		Assert.Equal(50, percent);
	}

	[Fact]
	public void TryParse_PhaseOnlyLineWithoutPercent_ReturnsPhaseAndUnknownPercent()
	{
		var ok = SdkManager.TryParseInstallProgressLine("Downloading system-images;android-37.0 ...",
			out var phase, out var percent);

		Assert.True(ok);
		Assert.Equal("Downloading", phase);
		Assert.Equal(-1, percent);
	}

	[Fact]
	public void TryParse_CarriageReturnInPlaceUpdates_UsesLastSegment()
	{
		// sdkmanager rewrites the bar in place using '\r'; the parser should read the latest state.
		var ok = SdkManager.TryParseInstallProgressLine(
			"[==        ] 10% Downloading\r[=====     ] 45% Downloading",
			out var phase, out var percent);

		Assert.True(ok);
		Assert.Equal("Downloading", phase);
		Assert.Equal(45, percent);
	}

	[Fact]
	public void TryParse_PercentAboveOneHundred_IsClamped()
	{
		var ok = SdkManager.TryParseInstallProgressLine("[==========] 120% Unzipping", out var phase, out var percent);

		Assert.True(ok);
		Assert.Equal("Unzipping", phase);
		Assert.Equal(100, percent);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("Warning: A newer version of the tools is available")]
	[InlineData("Loading local repository...")]
	[InlineData("[======================================] 100%")] // handled separately
	public void TryParse_LinesWithNoProgress_ReturnFalseExceptBareBar(string? line)
	{
		var ok = SdkManager.TryParseInstallProgressLine(line, out var phase, out var percent);

		if (line is not null && line.Contains('%'))
		{
			// A bare progress bar with a percentage is still progress, just without a named phase.
			Assert.True(ok);
			Assert.Equal(string.Empty, phase);
			Assert.Equal(100, percent);
		}
		else
		{
			Assert.False(ok);
			Assert.Equal(string.Empty, phase);
			Assert.Equal(-1, percent);
		}
	}

	[Fact]
	public void TryParse_MalformedLine_ReturnsFalse()
	{
		var ok = SdkManager.TryParseInstallProgressLine("done.", out var phase, out var percent);

		Assert.False(ok);
		Assert.Equal(string.Empty, phase);
		Assert.Equal(-1, percent);
	}
}
