// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.Models;

public sealed record PortCheckResult
{
[JsonPropertyName("port")]
public int Port { get; init; }

[JsonPropertyName("in_use")]
public bool InUse { get; init; }

[JsonPropertyName("listeners")]
public List<PortListenerResult> Listeners { get; init; } = [];
}

public sealed record PortListenerResult
{
[JsonPropertyName("pid")]
public int Pid { get; init; }

[JsonPropertyName("process_name")]
public string ProcessName { get; init; } = string.Empty;

[JsonPropertyName("address")]
public string Address { get; init; } = string.Empty;

[JsonPropertyName("family")]
public string Family { get; init; } = "ipv4";

[JsonPropertyName("state")]
public string State { get; init; } = "listen";
}