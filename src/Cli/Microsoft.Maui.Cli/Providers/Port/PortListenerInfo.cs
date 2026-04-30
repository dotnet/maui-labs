// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Providers.Port;

internal sealed record PortListenerInfo(int Port, int Pid, string ProcessName, string Address, string Family, string State = "listen");