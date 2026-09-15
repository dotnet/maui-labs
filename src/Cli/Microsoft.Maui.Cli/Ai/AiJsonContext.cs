// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MarketplaceManifest))]
[JsonSerializable(typeof(MarketplaceOwner))]
[JsonSerializable(typeof(PluginEntry))]
[JsonSerializable(typeof(PluginEntry[]))]
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(SkillInfo))]
[JsonSerializable(typeof(List<SkillInfo>))]
[JsonSerializable(typeof(DetectedEnvironment))]
[JsonSerializable(typeof(List<DetectedEnvironment>))]
[JsonSerializable(typeof(InstalledSkillVersion))]
internal sealed partial class AiJsonContext : JsonSerializerContext;
