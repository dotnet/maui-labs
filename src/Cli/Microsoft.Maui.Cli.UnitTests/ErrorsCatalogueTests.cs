// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Output;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class ErrorsCatalogueTests
{
	[Fact]
	public void Catalogue_IsNotEmpty() => Assert.NotEmpty(ErrorCodeCatalogue.All);

	[Fact]
	public void Catalogue_AllCodesMatchErrorCodeFormat()
	{
		foreach (var d in ErrorCodeCatalogue.All)
			Assert.Matches(@"^E\d{4}$", d.Code);
	}

	[Fact]
	public void Catalogue_AllNamesAreNonEmpty()
	{
		foreach (var d in ErrorCodeCatalogue.All)
			Assert.False(string.IsNullOrWhiteSpace(d.Name), $"{d.Code} has empty Name");
	}

	[Fact]
	public void Catalogue_AllDescriptionsAreNonEmpty()
	{
		foreach (var d in ErrorCodeCatalogue.All)
			Assert.False(string.IsNullOrWhiteSpace(d.Description), $"{d.Code} has empty Description");
	}

	[Fact]
	public void Catalogue_CodesAreUnique()
	{
		var codes = ErrorCodeCatalogue.All.Select(d => d.Code).ToList();
		Assert.Equal(codes.Count, codes.Distinct().Count());
	}

	[Fact]
	public void Catalogue_CoversEveryConstantInErrorCodes()
	{
		var allConstValues = typeof(ErrorCodes)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
			.Select(f => (string)f.GetRawConstantValue()!)
			.ToHashSet();
		var catalogueCodes = ErrorCodeCatalogue.All.Select(d => d.Code).ToHashSet();
		var missing = allConstValues.Except(catalogueCodes).OrderBy(x => x).ToList();
		Assert.Empty(missing);
	}

	[Fact]
	public void ByCategory_ReturnsOnlyMatchingCategory()
	{
		var toolCodes = ErrorCodeCatalogue.ByCategory("tool");
		Assert.All(toolCodes, d => Assert.Equal("tool", d.Category));
		Assert.NotEmpty(toolCodes);
	}

	[Fact]
	public void ByCategory_IsCaseInsensitive()
	{
		Assert.Equal(ErrorCodeCatalogue.ByCategory("platform").Count, ErrorCodeCatalogue.ByCategory("PLATFORM").Count);
	}

	[Fact]
	public void ByCategory_UnknownCategoryReturnsEmpty() =>
		Assert.Empty(ErrorCodeCatalogue.ByCategory("nonexistent"));

	[Fact]
	public void ByPrefix_ReturnsOnlyCodesWithMatchingPrefix()
	{
		var androidCodes = ErrorCodeCatalogue.ByPrefix("E21");
		Assert.All(androidCodes, d => Assert.StartsWith("E21", d.Code));
		Assert.NotEmpty(androidCodes);
	}

	[Fact]
	public void ByPrefix_IsCaseInsensitive()
	{
		Assert.Equal(ErrorCodeCatalogue.ByPrefix("e21").Count, ErrorCodeCatalogue.ByPrefix("E21").Count);
	}

	[Fact]
	public void ErrorCodeCatalogueResult_CountMatchesCodes()
	{
		var result = new ErrorCodeCatalogueResult { Codes = ErrorCodeCatalogue.All };
		Assert.Equal(ErrorCodeCatalogue.All.Count, result.Count);
	}

	[Fact]
	public void ErrorCodeDescriptor_SerializesToSnakeCase()
	{
		var d = new ErrorCodeDescriptor { Code = "E1001", Name = "InternalError", Category = "tool", Description = "Test" };
		var json = JsonSerializer.Serialize(d, MauiCliJsonContext.Default.ErrorCodeDescriptor);
		Assert.Contains("\"code\"", json);
		Assert.Contains("\"name\"", json);
		Assert.DoesNotContain("\"Code\"", json);
	}

	[Fact]
	public void ErrorCodeDescriptor_NullableFieldsOmittedWhenNull()
	{
		var d = new ErrorCodeDescriptor { Code = "E1004", Name = "InvalidArgument", Category = "tool", Description = "X" };
		var json = JsonSerializer.Serialize(d, MauiCliJsonContext.Default.ErrorCodeDescriptor);
		Assert.DoesNotContain("subcategory", json);
		Assert.DoesNotContain("default_remediation_type", json);
	}

	[Fact]
	public void ErrorsCommand_CanBeConstructed()
	{
		var command = ErrorsCommands.Create();
		Assert.Equal("errors", command.Name);
		Assert.Contains(command.Subcommands, c => c.Name == "list");
	}

	[Fact]
	public void ErrorsListCommand_HasExpectedOptions()
	{
		var listCommand = ErrorsCommands.Create().Subcommands.Single(c => c.Name == "list");
		Assert.Contains(listCommand.Options, o => o.Name == "--category");
		Assert.Contains(listCommand.Options, o => o.Name == "--prefix");
	}

	[Fact]
	public void ErrorsCommand_IsRegisteredInRootCommand()
	{
		var rootCommand = Program.BuildRootCommand();
		Assert.Contains(rootCommand.Subcommands, c => c.Name == "errors");
	}
}
