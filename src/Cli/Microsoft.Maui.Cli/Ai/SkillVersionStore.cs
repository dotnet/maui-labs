// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

/// <summary>
/// Reads and writes <c>.skill-version</c> JSON files that track installed skill metadata.
/// Backward compatible with the legacy format containing only commit, updatedAt, and branch.
/// </summary>
internal static class SkillVersionStore
{
	private const string VersionFileName = ".skill-version";
	private static readonly AiJsonContext s_indentedJsonContext = new(new JsonSerializerOptions(AiJsonContext.Default.Options)
	{
		WriteIndented = true
	});

	/// <summary>
	/// Reads the <c>.skill-version</c> file from the specified skill directory.
	/// </summary>
	/// <param name="skillDir">Absolute path to the skill installation directory.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The deserialized version info, or <c>null</c> if not found or unreadable.</returns>
	public static async Task<InstalledSkillVersion?> ReadAsync(string skillDir, CancellationToken ct = default)
	{
		var path = Path.Combine(skillDir, VersionFileName);
		if (!File.Exists(path))
			return null;

		try
		{
			var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
			return JsonSerializer.Deserialize(json, AiJsonContext.Default.InstalledSkillVersion);
		}
		catch (JsonException)
		{
			return null;
		}
		catch (IOException)
		{
			return null;
		}
	}

	/// <summary>
	/// Writes a <c>.skill-version</c> file to the specified skill directory.
	/// Creates the directory if it does not exist.
	/// </summary>
	/// <param name="skillDir">Absolute path to the skill installation directory.</param>
	/// <param name="version">Version metadata to persist.</param>
	/// <param name="ct">Cancellation token.</param>
	public static async Task WriteAsync(string skillDir, InstalledSkillVersion version, CancellationToken ct = default)
	{
		Directory.CreateDirectory(skillDir);
		var path = Path.Combine(skillDir, VersionFileName);

		var json = JsonSerializer.Serialize(version, s_indentedJsonContext.InstalledSkillVersion);
		await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
	}
}
