// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.DocumentExtraction;

namespace CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence;

/// <summary>Converts a native Document Intelligence client to a document extraction client.</summary>
public static class DocumentIntelligenceClientExtensions
{
    /// <summary>Creates an extraction adapter that borrows <paramref name="client"/> and captures the supplied settings.</summary>
    public static DocumentExtractionClient AsDocumentExtractionClient(
        this DocumentIntelligenceClient client, DocumentIntelligenceExtractionOptions? options = null)
        => new DocumentIntelligenceExtractionClient(client, options);
}
