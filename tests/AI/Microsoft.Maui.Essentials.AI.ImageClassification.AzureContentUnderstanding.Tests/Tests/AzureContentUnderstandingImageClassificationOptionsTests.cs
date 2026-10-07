// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Essentials.AI.ImageClassification.AzureContentUnderstanding;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.ImageClassification.AzureContentUnderstanding.Tests;

public class AzureContentUnderstandingImageClassificationOptionsTests
{
	[Fact]
	public void AnalyzerId_SetAndGet_RoundTripsValue()
	{
		var options = new AzureContentUnderstandingImageClassificationOptions
		{
			AnalyzerId = "product-classifier-v1"
		};

		Assert.Equal("product-classifier-v1", options.AnalyzerId);

		options.AnalyzerId = "wildlife-classifier-v2";

		Assert.Equal("wildlife-classifier-v2", options.AnalyzerId);
	}

	[Fact]
	public void AnalyzerId_PropertyHasRequiredPublicWritableStringContract()
	{
		Type optionsType = typeof(AzureContentUnderstandingImageClassificationOptions);
		System.Reflection.PropertyInfo property = Assert.Single(
			optionsType.GetProperties(
				System.Reflection.BindingFlags.Public |
				System.Reflection.BindingFlags.Instance |
				System.Reflection.BindingFlags.DeclaredOnly));

		Assert.Equal(nameof(AzureContentUnderstandingImageClassificationOptions.AnalyzerId), property.Name);
		Assert.Equal(typeof(string), property.PropertyType);
		Assert.True(property.GetMethod?.IsPublic);
		Assert.True(property.SetMethod?.IsPublic);
		Assert.NotNull(property.GetCustomAttributes(
			typeof(System.Runtime.CompilerServices.RequiredMemberAttribute),
			inherit: false).SingleOrDefault());
	}
}
