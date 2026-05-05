// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.Services;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class ServiceConfigurationTests
{
	[Fact]
	public void CreateServiceProvider_RegistersAllServices()
	{
		// Act
		var provider = ServiceConfiguration.CreateServiceProvider();

		// Assert
		Assert.NotNull(provider.GetService<IJdkManager>());
		Assert.NotNull(provider.GetService<IAndroidProvider>());
		Assert.NotNull(provider.GetService<IAppleProvider>());
		Assert.NotNull(provider.GetService<IDoctorService>());
		Assert.NotNull(provider.GetService<IDeviceManager>());
		Assert.NotNull(provider.GetService<IDevFlowOutputWriter>());
	}

	[Fact]
	public void CreateServiceProvider_ReturnsSingletonForProviders()
	{
		// Act
		var provider = ServiceConfiguration.CreateServiceProvider();
		var android1 = provider.GetService<IAndroidProvider>();
		var android2 = provider.GetService<IAndroidProvider>();
		var apple1 = provider.GetService<IAppleProvider>();
		var apple2 = provider.GetService<IAppleProvider>();

		// Assert
		Assert.Same(android1, android2);
		Assert.Same(apple1, apple2);
	}

	[Fact]
	public void CreateTestServiceProvider_UsesProvidedFakes()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider();
		var fakeApple = new FakeAppleProvider();

		// Act
		var provider = ServiceConfiguration.CreateTestServiceProvider(
			androidProvider: fakeAndroid,
			appleProvider: fakeApple);

		// Assert
		Assert.Same(fakeAndroid, provider.GetService<IAndroidProvider>());
		Assert.Same(fakeApple, provider.GetService<IAppleProvider>());
	}

	[Fact]
	public void CreateTestServiceProvider_CreatesMissingServices()
	{
		// Arrange - only provide android fake
		var fakeAndroid = new FakeAndroidProvider();

		// Act
		var provider = ServiceConfiguration.CreateTestServiceProvider(
			androidProvider: fakeAndroid);

		// Assert - should create real services for everything else
		Assert.Same(fakeAndroid, provider.GetService<IAndroidProvider>());
		Assert.NotNull(provider.GetService<IAppleProvider>());
		Assert.NotNull(provider.GetService<IDoctorService>());
	}

	[Fact]
	public void Program_Services_CanBeOverridden()
	{
		try
		{
			// Arrange
			var fakeAndroid = new FakeAndroidProvider();
			var fakeApple = new FakeAppleProvider();
			var testProvider = ServiceConfiguration.CreateTestServiceProvider(
				androidProvider: fakeAndroid,
				appleProvider: fakeApple);

			// Act
			Program.Services = testProvider;

			// Assert
			Assert.Same(fakeAndroid, Program.AndroidProvider);
			Assert.Same(fakeApple, Program.AppleProvider);
		}
		finally
		{
			// Cleanup
			Program.ResetServices();
		}
	}
}
