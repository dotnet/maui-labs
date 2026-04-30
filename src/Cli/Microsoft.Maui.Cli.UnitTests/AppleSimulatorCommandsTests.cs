// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class AppleSimulatorCommandsTests
{
	[Fact]
	public void SimulatorCommand_HasCreateSubcommand()
	{
		var root = Program.BuildRootCommand();
		var apple = root.Subcommands.FirstOrDefault(c => c.Name == "apple");
		var simulator = apple?.Subcommands.FirstOrDefault(c => c.Name == "simulator");
		Assert.NotNull(simulator);
		Assert.Contains(simulator!.Subcommands, c => c.Name == "create");
	}

	[Fact]
	public void SimulatorCommand_HasEraseSubcommand()
	{
		var root = Program.BuildRootCommand();
		var apple = root.Subcommands.FirstOrDefault(c => c.Name == "apple");
		var simulator = apple?.Subcommands.FirstOrDefault(c => c.Name == "simulator");
		Assert.NotNull(simulator);
		Assert.Contains(simulator!.Subcommands, c => c.Name == "erase");
	}

	[Fact]
	public void CreateCommand_HasDeviceTypeArgument()
	{
		var root = Program.BuildRootCommand();
		var createCmd = root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator").Subcommands
			.First(c => c.Name == "create");
		Assert.Contains(createCmd.Arguments, a => a.Name == "device-type");
	}

	[Fact]
	public void CreateCommand_HasNameAndRuntimeOptions()
	{
		var root = Program.BuildRootCommand();
		var createCmd = root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator").Subcommands
			.First(c => c.Name == "create");
		Assert.Contains(createCmd.Options, o => o.Name == "--name");
		Assert.Contains(createCmd.Options, o => o.Name == "--runtime");
	}

	[Fact]
	public void EraseCommand_HasNameOrUdidArgument()
	{
		var root = Program.BuildRootCommand();
		var eraseCmd = root.Subcommands
			.First(c => c.Name == "apple").Subcommands
			.First(c => c.Name == "simulator").Subcommands
			.First(c => c.Name == "erase");
		Assert.Contains(eraseCmd.Arguments, a => a.Name == "name-or-udid");
	}

	[Fact]
	public void FakeAppleProvider_CreateSimulator_TracksCall()
	{
		var fake = new FakeAppleProvider { CreateSimulatorResult = "test-udid-1234" };
		var udid = fake.CreateSimulator("My iPhone 15", "com.apple.CoreSimulator.SimDeviceType.iPhone-15", "com.apple.CoreSimulator.SimRuntime.iOS-17-2");
		Assert.Equal("test-udid-1234", udid);
		Assert.Single(fake.CreatedSimulators);
		Assert.Equal(("My iPhone 15", "com.apple.CoreSimulator.SimDeviceType.iPhone-15", "com.apple.CoreSimulator.SimRuntime.iOS-17-2"), fake.CreatedSimulators[0]);
	}

	[Fact]
	public void FakeAppleProvider_CreateSimulator_ReturnsNull_WhenResultIsNull()
	{
		var fake = new FakeAppleProvider { CreateSimulatorResult = null };
		var udid = fake.CreateSimulator("Ghost", "com.apple.CoreSimulator.SimDeviceType.iPhone-15");
		Assert.Null(udid);
	}

	[Fact]
	public void FakeAppleProvider_EraseSimulator_TracksCall()
	{
		var fake = new FakeAppleProvider { EraseSimulatorResult = true };
		var result = fake.EraseSimulator("ABC-DEF-123");
		Assert.True(result);
		Assert.Single(fake.ErasedSimulators);
		Assert.Equal("ABC-DEF-123", fake.ErasedSimulators[0]);
	}

	[Fact]
	public void FakeAppleProvider_EraseSimulator_ReturnsFalse_WhenConfigured()
	{
		var fake = new FakeAppleProvider { EraseSimulatorResult = false };
		var result = fake.EraseSimulator("nonexistent");
		Assert.False(result);
	}

	[Fact]
	public void SimulatorCreateResult_SerializesToSnakeCase()
	{
		var model = new SimulatorCreateResult
		{
			Udid = "AABBCCDD-1234-5678-ABCD-000000000001",
			Name = "iPhone 15",
			DeviceType = "com.apple.CoreSimulator.SimDeviceType.iPhone-15",
			Runtime = "com.apple.CoreSimulator.SimRuntime.iOS-17-2"
		};
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorCreateResult);
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.Equal("AABBCCDD-1234-5678-ABCD-000000000001", root.GetProperty("udid").GetString());
		Assert.Equal("iPhone 15", root.GetProperty("name").GetString());
		Assert.Equal("com.apple.CoreSimulator.SimDeviceType.iPhone-15", root.GetProperty("device_type").GetString());
		Assert.Equal("com.apple.CoreSimulator.SimRuntime.iOS-17-2", root.GetProperty("runtime").GetString());
	}

	[Fact]
	public void SimulatorCreateResult_OmitsNullRuntime()
	{
		var model = new SimulatorCreateResult { Udid = "AABBCCDD-1234", Name = "iPhone 15", DeviceType = "com.apple.CoreSimulator.SimDeviceType.iPhone-15" };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorCreateResult);
		using var doc = JsonDocument.Parse(json);
		Assert.False(doc.RootElement.TryGetProperty("runtime", out _));
	}

	[Fact]
	public void SimulatorEraseResult_SerializesToSnakeCase()
	{
		var model = new SimulatorEraseResult { Target = "My iPhone 15", Erased = true };
		var json = JsonSerializer.Serialize(model, MauiCliJsonContext.Default.SimulatorEraseResult);
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.Equal("My iPhone 15", root.GetProperty("target").GetString());
		Assert.True(root.GetProperty("erased").GetBoolean());
	}

	[Fact]
	public void SimulatorCreateFailed_ErrorResult_HasCorrectCode()
	{
		var ex = new MauiToolException(ErrorCodes.AppleSimulatorCreateFailed, "Create failed");
		var error = ErrorResult.FromException(ex);
		Assert.Equal("E2207", error.Code);
		Assert.Equal("platform", error.Category);
	}

	[Fact]
	public void SimulatorEraseFailed_ErrorResult_HasCorrectCode()
	{
		var ex = new MauiToolException(ErrorCodes.AppleSimulatorEraseFailed, "Erase failed");
		var error = ErrorResult.FromException(ex);
		Assert.Equal("E2208", error.Code);
		Assert.Equal("platform", error.Category);
	}
}
