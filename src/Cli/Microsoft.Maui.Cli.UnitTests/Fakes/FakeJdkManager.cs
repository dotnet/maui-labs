// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;

namespace Microsoft.Maui.Cli.UnitTests.Fakes;

/// <summary>
/// Minimal fake for <see cref="IJdkManager"/> used in unit tests.
/// </summary>
public class FakeJdkManager : IJdkManager
{
	public string? DetectedJdkPath { get; set; }
	public int? DetectedJdkVersion { get; set; }
	public bool IsInstalled { get; set; } = true;

	public Task<HealthCheck> CheckHealthAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(new HealthCheck
		{
			Category = "jdk",
			Name = "JDK",
			Status = IsInstalled ? CheckStatus.Ok : CheckStatus.Error,
			Message = IsInstalled ? null : "JDK not found"
		});

	public Task InstallAsync(int version = 17, string? installPath = null, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public Task InstallAsync(int version, string? installPath, Action<double, string>? onProgress, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public IEnumerable<int> GetAvailableVersions() => new[] { 17, 21 };
}
