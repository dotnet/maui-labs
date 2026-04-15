// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Services;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class DoctorServiceTests
{
	[Fact]
	public async Task RunAllChecksAsync_IncludesDotNetChecks()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider();
		var service = new DoctorService(fakeAndroid);

		// Act
		var report = await service.RunAllChecksAsync();

		// Assert
		Assert.NotNull(report);
		Assert.True(report.Checks.Any(c => c.Category == "dotnet"));
	}

	[Fact]
	public async Task RunAllChecksAsync_IncludesAndroidChecks_WhenProviderReturnsChecks()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider
		{
			HealthChecks = new List<HealthCheck>
			{
				new HealthCheck
				{
					Category = "android",
					Name = "JDK",
					Status = CheckStatus.Ok,
					Message = "JDK 17"
				},
				new HealthCheck
				{
					Category = "android",
					Name = "Android SDK",
					Status = CheckStatus.Ok
				}
			}
		};

		var service = new DoctorService(fakeAndroid);

		// Act
		var report = await service.RunAllChecksAsync();

		// Assert
		Assert.Contains(report.Checks, c => c.Category == "android" && c.Name == "JDK");
		Assert.Contains(report.Checks, c => c.Category == "android" && c.Name == "Android SDK");
	}

	[Fact]
	public async Task RunAllChecksAsync_IncludesAppleChecks_WhenProviderReturnsChecks()
	{
		// Arrange
		var fakeApple = new FakeAppleProvider
		{
			HealthChecks = new List<HealthCheck>
			{
				new HealthCheck
				{
					Category = "apple",
					Name = "Xcode",
					Status = CheckStatus.Ok,
					Message = "Xcode 16.0"
				},
				new HealthCheck
				{
					Category = "apple",
					Name = "Command Line Tools",
					Status = CheckStatus.Ok,
					Message = "CLT installed"
				}
			}
		};

		var service = new DoctorService(appleProvider: fakeApple);

		// Act
		var report = await service.RunAllChecksAsync();

		// Assert
		Assert.Contains(report.Checks, c => c.Category == "apple" && c.Name == "Xcode");
		Assert.Contains(report.Checks, c => c.Category == "apple" && c.Name == "Command Line Tools");
	}

	[Fact]
	public async Task RunAllChecksAsync_CalculatesCorrectSummary()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider
		{
			HealthChecks = new List<HealthCheck>
			{
				new HealthCheck { Category = "android", Name = "JDK", Status = CheckStatus.Ok },
				new HealthCheck { Category = "android", Name = "SDK", Status = CheckStatus.Warning },
				new HealthCheck { Category = "android", Name = "AVD", Status = CheckStatus.Error }
			}
		};

		var service = new DoctorService(fakeAndroid);

		// Act
		var report = await service.RunAllChecksAsync();

		// Assert - should have dotnet checks + android checks
		Assert.True(report.Summary.Total >= 3); // At least 3 checks
		Assert.True(report.Summary.Warning >= 1); // At least 1 warning
		Assert.True(report.Summary.Error >= 1); // At least 1 error
	}

	[Fact]
	public async Task RunCategoryChecksAsync_SetsStatusBasedOnChecks()
	{
		// Arrange - all OK android checks only (avoids environment-dependent dotnet checks)
		var fakeAndroid = new FakeAndroidProvider
		{
			HealthChecks = new List<HealthCheck>
			{
				new HealthCheck { Category = "android", Name = "JDK", Status = CheckStatus.Ok }
			}
		};

		var service = new DoctorService(fakeAndroid);

		// Act - use category check to isolate from dotnet/workload environment checks
		var report = await service.RunCategoryChecksAsync("android");

		// Assert - status should reflect worst check (all OK → not unhealthy)
		Assert.NotEqual(HealthStatus.Unhealthy, report.Status);
	}

	[Fact]
	public async Task RunCategoryChecksAsync_AppleCategory_ReturnsAppleChecks()
	{
		// Arrange
		var fakeApple = new FakeAppleProvider
		{
			HealthChecks = new List<HealthCheck>
			{
				new HealthCheck { Category = "apple", Name = "Xcode", Status = CheckStatus.Ok, Message = "Xcode 16.0" }
			}
		};

		var service = new DoctorService(appleProvider: fakeApple);

		// Act
		var report = await service.RunCategoryChecksAsync("apple");

		// Assert
		Assert.Contains(report.Checks, c => c.Category == "apple" && c.Name == "Xcode");
	}

	[Fact]
	public async Task RunAllChecksAsync_IncludesAndroidChecks_WhenProviderReturnsAndroidOnly()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider
		{
			HealthChecks = new List<HealthCheck>
			{
				new HealthCheck { Category = "android", Name = "JDK", Status = CheckStatus.Ok }
			}
		};

		var service = new DoctorService(fakeAndroid);

		// Act
		var report = await service.RunAllChecksAsync();

		// Assert - android checks should be present
		Assert.Contains(report.Checks, c => c.Category == "android" && c.Name == "JDK");
	}

	[Fact]
	public async Task RunAllChecksAsync_BothProviders_IncludesBothChecks()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider
		{
			HealthChecks = new List<HealthCheck>
			{
				new HealthCheck { Category = "android", Name = "JDK", Status = CheckStatus.Ok }
			}
		};
		var fakeApple = new FakeAppleProvider
		{
			HealthChecks = new List<HealthCheck>
			{
				new HealthCheck { Category = "apple", Name = "Xcode", Status = CheckStatus.Ok }
			}
		};

		var service = new DoctorService(fakeAndroid, fakeApple);

		// Act
		var report = await service.RunAllChecksAsync();

		// Assert
		Assert.Contains(report.Checks, c => c.Category == "android");
		Assert.Contains(report.Checks, c => c.Category == "apple");
	}
}
