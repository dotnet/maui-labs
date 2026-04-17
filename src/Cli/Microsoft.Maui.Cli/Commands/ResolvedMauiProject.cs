// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Commands;

internal sealed record ResolvedMauiProject
{
	public required string ProjectPath { get; init; }
	public required string ProjectDirectory { get; init; }
	public required string ProjectName { get; init; }
	public required IReadOnlyList<string> TargetFrameworks { get; init; }
}
