// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CommunityToolkit.DocumentExtraction.Mistral;

/// <summary>Configures a native Mistral OCR operation independently of its hosting service and authentication.</summary>
public sealed class MistralDocumentExtractionClientOptions
{
    /// <summary>Gets the complete OCR operation URI, including its path and any required query parameters. No route is appended.</summary>
    public required Uri OcrEndpoint { get; init; }

    /// <summary>Gets the model or deployment selector accepted by the configured endpoint.</summary>
    public required string DefaultModel { get; init; }

    /// <summary>Gets whether to request base64 image content by default.</summary>
    public bool IncludeImages { get; init; }

    /// <summary>Gets the Entra token scope. Used only with a <c>TokenCredential</c>; API keys are sent directly as bearer credentials.</summary>
    public string TokenScope { get; init; } = "https://cognitiveservices.azure.com/.default";
}
