// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Maui.Cli.Ai;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class StringOrArrayConverterTests
{
	private static readonly JsonSerializerOptions Options = new()
	{
		Converters = { new StringOrArrayConverter() }
	};

	[Fact]
	public void Read_SingleString_ReturnsArrayWithOneElement()
	{
		var json = "\"./skills/\"";
		var result = JsonSerializer.Deserialize<string[]>(json, Options);

		Assert.NotNull(result);
		Assert.Single(result);
		Assert.Equal("./skills/", result[0]);
	}

	[Fact]
	public void Read_Array_ReturnsAllElements()
	{
		var json = "[\"./skills/\"]";
		var result = JsonSerializer.Deserialize<string[]>(json, Options);

		Assert.NotNull(result);
		Assert.Single(result);
		Assert.Equal("./skills/", result[0]);
	}

	[Fact]
	public void Read_ArrayMultipleElements_ReturnsAll()
	{
		var json = "[\"a\", \"b\", \"c\"]";
		var result = JsonSerializer.Deserialize<string[]>(json, Options);

		Assert.NotNull(result);
		Assert.Equal(3, result.Length);
		Assert.Equal(["a", "b", "c"], result);
	}

	[Fact]
	public void Read_ArrayWithNull_SkipsNull()
	{
		var json = "[\"a\", null, \"b\"]";
		var result = JsonSerializer.Deserialize<string[]>(json, Options);

		Assert.NotNull(result);
		Assert.Equal(2, result.Length);
		Assert.Equal(["a", "b"], result);
	}

	[Fact]
	public void Read_ArrayWithNestedObject_SkipsObject()
	{
		var json = "[\"a\", {\"key\": \"value\"}, \"b\"]";
		var result = JsonSerializer.Deserialize<string[]>(json, Options);

		Assert.NotNull(result);
		Assert.Equal(2, result.Length);
		Assert.Equal(["a", "b"], result);
	}

	[Fact]
	public void Read_ArrayWithNestedArray_SkipsNestedArray()
	{
		var json = "[\"a\", [\"nested\"], \"b\"]";
		var result = JsonSerializer.Deserialize<string[]>(json, Options);

		Assert.NotNull(result);
		Assert.Equal(2, result.Length);
		Assert.Equal(["a", "b"], result);
	}

	[Fact]
	public void Read_ArrayWithNumber_SkipsNumber()
	{
		var json = "[\"a\", 42, \"b\"]";
		var result = JsonSerializer.Deserialize<string[]>(json, Options);

		Assert.NotNull(result);
		Assert.Equal(2, result.Length);
		Assert.Equal(["a", "b"], result);
	}

	[Fact]
	public void Read_EmptyArray_ReturnsEmpty()
	{
		var json = "[]";
		var result = JsonSerializer.Deserialize<string[]>(json, Options);

		Assert.NotNull(result);
		Assert.Empty(result);
	}

	[Fact]
	public void Write_RoundTrips()
	{
		var original = new[] { "x", "y" };
		var json = JsonSerializer.Serialize(original, Options);
		var result = JsonSerializer.Deserialize<string[]>(json, Options);

		Assert.Equal(original, result);
	}
}
