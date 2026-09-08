using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace AndroidX.Compose.SourceGenerators;

internal sealed class GenerationResult
{
    public GenerationResult(string? source, string? hintName, IReadOnlyList<Diagnostic> diagnostics)
    {
        Source = source;
        HintName = hintName;
        Diagnostics = diagnostics;
    }

    public string? Source { get; }
    public string? HintName { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
