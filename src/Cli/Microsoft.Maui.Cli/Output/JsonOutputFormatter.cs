// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.Models;

namespace Microsoft.Maui.Cli.Output;

/// <summary>
/// JSON output formatter for machine-readable output.
/// </summary>
public class JsonOutputFormatter : IOutputFormatter
{
	static readonly JsonSerializerOptions s_nodeIndentedOptions = new(MauiCliJsonContext.Default.Options)
	{
		WriteIndented = true
	};

	static readonly JsonSerializerOptions s_nodeCompactOptions = new(MauiCliJsonContext.Default.Options)
	{
		WriteIndented = false
	};

	readonly TextWriter _output;

	public JsonOutputFormatter(TextWriter? output = null)
	{
		_output = output ?? Console.Out;
	}

	public void Write<T>(T result)
	{
		WriteResult(result);
	}

	public void WriteResult<T>(T result)
	{
		_output.WriteLine(SerializeUntyped(result));
	}

	public void WriteError(Exception exception)
	{
		WriteError(ErrorResult.FromException(exception));
	}

	public void WriteError(ErrorResult error)
	{
		WriteResult(error);
	}

	public void WriteSuccess(string message)
	{
		WriteResult(new StatusMessageResult
		{
			Status = "success",
			Message = message
		});
	}

	public void WriteWarning(string message)
	{
		WriteResult(new StatusMessageResult
		{
			Status = "warning",
			Message = message
		});
	}

	public void WriteInfo(string message)
	{
		WriteResult(new StatusMessageResult
		{
			Status = "info",
			Message = message
		});
	}

	public void WriteProgress(string message, int? percentage = null)
	{
		WriteResult(new StatusMessageResult
		{
			Status = "progress",
			Message = message,
			Percentage = percentage
		});
	}

	public void WriteTable<T>(IEnumerable<T> items, params (string Header, Func<T, string> Selector)[] columns)
	{
		var rows = items.Select(item =>
			columns.ToDictionary(c => c.Header.ToLowerInvariant(), c => c.Selector(item)));
		WriteResult(rows.ToList());
	}

	public void WriteVersion(string version, string runtime, string os)
	{
		WriteResult(new VersionResult
		{
			Version = version,
			Runtime = runtime,
			Os = os
		});
	}

	/// <summary>
	/// Serializes an object to JSON string.
	/// </summary>
	public static string Serialize<T>(T obj) => SerializeUntyped(obj);

	/// <summary>
	/// Deserializes JSON string to object.
	/// </summary>
	public static T? Deserialize<T>(string json) => (T?)JsonSerializer.Deserialize(json, typeof(T), MauiCliJsonContext.Default);

	static string SerializeUntyped(object? value)
	{
		if (value is null)
			return "null";

		return value switch
		{
			JsonNode node => node.ToJsonString(s_nodeIndentedOptions),
			JsonElement element => PrettyPrint(element),
			JsonDocument document => PrettyPrint(document.RootElement),
			_ => JsonSerializer.Serialize(value, value.GetType(), MauiCliJsonContext.Default)
		};
	}

	static string PrettyPrint(JsonElement element)
	{
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
		element.WriteTo(writer);
		writer.Flush();
		return System.Text.Encoding.UTF8.GetString(stream.ToArray());
	}
}
