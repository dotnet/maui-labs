// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Providers.Apple;

namespace Microsoft.Maui.Cli.Output;

[JsonSourceGenerationOptions(
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
	UseStringEnumConverter = true,
	WriteIndented = true)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<Dictionary<string, string>>))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(DoctorReport))]
[JsonSerializable(typeof(DoctorSummary))]
[JsonSerializable(typeof(HealthCheck))]
[JsonSerializable(typeof(List<HealthCheck>))]
[JsonSerializable(typeof(FixInfo))]
[JsonSerializable(typeof(ErrorResult))]
[JsonSerializable(typeof(RemediationResult))]
[JsonSerializable(typeof(Device))]
[JsonSerializable(typeof(List<Device>))]
[JsonSerializable(typeof(DeviceListResult))]
[JsonSerializable(typeof(AvdInfo))]
[JsonSerializable(typeof(List<AvdInfo>))]
[JsonSerializable(typeof(SdkPackage))]
[JsonSerializable(typeof(List<SdkPackage>))]
[JsonSerializable(typeof(XcodeInstallation))]
[JsonSerializable(typeof(List<XcodeInstallation>))]
[JsonSerializable(typeof(RuntimeInfo))]
[JsonSerializable(typeof(List<RuntimeInfo>))]
[JsonSerializable(typeof(SimulatorInfo))]
[JsonSerializable(typeof(List<SimulatorInfo>))]
[JsonSerializable(typeof(StatusMessageResult))]
[JsonSerializable(typeof(VersionResult))]
[JsonSerializable(typeof(CliCommandResult))]
internal sealed partial class MauiCliJsonContext : JsonSerializerContext;
