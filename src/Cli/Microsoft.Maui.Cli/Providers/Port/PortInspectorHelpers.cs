// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Providers.Port;

internal static class PortInspectorHelpers
{
	internal static int GetPortFromNetworkDword(uint networkDword)
		=> (int)(((networkDword & 0xFF) << 8) | ((networkDword >> 8) & 0xFF));
}
