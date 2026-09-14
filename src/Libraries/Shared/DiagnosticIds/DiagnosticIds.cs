// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Minimal local shim of `Microsoft.Shared.DiagnosticIds.DiagnosticIds` from dotnet/extensions'
// `src/Shared/DiagnosticIds/DiagnosticIds.cs`, trimmed to the members actually referenced by the
// vendored `Microsoft.Extensions.DocumentExtraction(.Abstractions)` sources in this repo
// (`DiagnosticIds.Experiments.DocumentExtraction` and `DiagnosticIds.UrlFormat`). Linked into both
// projects; do not edit the copied upstream implementation files to accommodate this shim.

#pragma warning disable CA1716
namespace Microsoft.Shared.DiagnosticIds;
#pragma warning restore CA1716

/// <summary>
///  Various diagnostic IDs reported by this repo.
/// </summary>
internal static class DiagnosticIds
{
#pragma warning disable S1075 // URIs should not be hardcoded
    internal const string UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}";
#pragma warning restore S1075 // URIs should not be hardcoded

    /// <summary>
    ///  Experiments supported by this repo.
    /// </summary>
    internal static class Experiments
    {
        // All Document Extraction experiments share a diagnostic ID but have different
        // constants to manage which experiment each API belongs to.
        internal const string DocumentExtraction = DocumentExtractionExperiments;

        private const string DocumentExtractionExperiments = "MEDE0001";
    }
}
