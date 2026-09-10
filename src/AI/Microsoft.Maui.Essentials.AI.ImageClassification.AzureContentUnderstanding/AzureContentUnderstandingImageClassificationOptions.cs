// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Essentials.AI.ImageClassification.AzureContentUnderstanding;

/// <summary>Configures image classification with Azure Content Understanding.</summary>
public sealed class AzureContentUnderstandingImageClassificationOptions
{
	/// <summary>Gets or sets the identifier of the classifier analyzer to invoke.</summary>
	public required string AnalyzerId { get; set; }
}
