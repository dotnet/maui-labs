// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xamarin.MacDev;

namespace Microsoft.Maui.Cli.UnitTests.Fakes;

/// <summary>
/// <see cref="PhysicalDeviceService"/> that returns canned <c>devicectl</c> output instead of running it,
/// so physical device discovery can be tested on any host.
/// </summary>
public sealed class FakePhysicalDeviceService : PhysicalDeviceService
{
	readonly DeviceCtlRunResult _result;

	/// <summary>Reports the given devices, built with <see cref="DeviceCtlFixtures.IPhone"/>.</summary>
	public FakePhysicalDeviceService(params object[] devices)
		: this(DeviceCtlRunResult.FromJson(DeviceCtlFixtures.Document(devices)))
	{
	}

	public FakePhysicalDeviceService(DeviceCtlRunResult result)
		: base(new StandardErrorToolsLogger(TextWriter.Null))
	{
		_result = result;
	}

	public int ListCallCount { get; private set; }

	protected override Task<DeviceCtlRunResult> ExecuteDeviceCtlAsync(CancellationToken cancellationToken)
	{
		ListCallCount++;
		return Task.FromResult(_result);
	}
}
