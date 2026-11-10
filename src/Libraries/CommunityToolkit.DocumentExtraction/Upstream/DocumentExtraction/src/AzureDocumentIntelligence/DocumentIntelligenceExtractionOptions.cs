// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence;

/// <summary>Configures defaults for Azure Document Intelligence extraction.</summary>
public sealed class DocumentIntelligenceExtractionOptions
{
    /// <summary>Gets or sets the model used when the extraction request does not select one.</summary>
    public string DefaultModel { get; set; } = "prebuilt-layout";

    /// <summary>Gets or sets whether to request and download extracted figures.</summary>
    public bool IncludeImages { get; set; }
}
