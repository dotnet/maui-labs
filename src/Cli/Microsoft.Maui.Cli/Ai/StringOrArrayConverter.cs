// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.Ai;

/// <summary>
/// Handles JSON fields that can be either a single string or an array of strings.
/// Per the plugin.json spec, "skills" and "agents" accept both formats.
/// </summary>
internal sealed class StringOrArrayConverter : JsonConverter<string[]>
{
	public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			var value = reader.GetString();
			return value is not null ? [value] : [];
		}

		if (reader.TokenType == JsonTokenType.StartArray)
		{
			var list = new List<string>();
			while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
			{
				if (reader.TokenType == JsonTokenType.String)
				{
					var item = reader.GetString();
					if (item is not null)
						list.Add(item);
				}
				else
				{
					reader.Skip();
				}
			}
			return [.. list];
		}

		return [];
	}

	public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		foreach (var item in value)
			writer.WriteStringValue(item);
		writer.WriteEndArray();
	}
}
