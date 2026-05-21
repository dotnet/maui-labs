// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.Maui.Cli.Services;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class MauiVersionFeedServiceTests
{
	[Fact]
	public async Task GetVersionsAsync_StableWithoutPrerelease_FiltersAndSortsVersions()
	{
		var service = CreateService("""
			{"versions":["10.0.5","11.0.0-preview.1.1","10.0.60","10.0.12"]}
			""");

		var versions = await service.GetVersionsAsync(MauiVersionChannel.Stable, includePrerelease: false);

		Assert.Equal(["10.0.5", "10.0.12", "10.0.60"], versions.Select(version => version.Version).ToArray());
	}

	[Fact]
	public async Task GetLatestVersionAsync_Nightly_SortsCiVersions()
	{
		var service = CreateService("""
			{"versions":["9.0.90-ci.main.25354.1","9.0.90-ci.main.25353.3","9.0.90-ci.main.25354.2"]}
			""");

		var version = await service.GetLatestVersionAsync(MauiVersionChannel.Nightly, includePrerelease: true);

		Assert.NotNull(version);
		Assert.Equal("9.0.90-ci.main.25354.2", version.Version);
	}

	static MauiVersionFeedService CreateService(string response) =>
		new(new HttpClient(new StubHandler(response)));

	sealed class StubHandler(string response) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(response),
			});
	}
}
