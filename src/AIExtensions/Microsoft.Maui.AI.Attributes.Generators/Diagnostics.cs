using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.Maui.AI.Attributes.Generators;

internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(Location? location)
    {
        if (location is null || !location.IsInSource)
            return null;
        return new LocationInfo(
            location.SourceTree!.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }

    public Location ToLocation()
        => Location.Create(FilePath, TextSpan, LineSpan);
}

internal sealed record DiagnosticInfo(
    string Id,
    DiagnosticSeverity Severity,
    string Message,
    LocationInfo? Location)
{
    public Diagnostic ToDiagnostic()
    {
        var desc = new DiagnosticDescriptor(
            Id,
            Id,
            Message,
            "Microsoft.Maui.AI.Attributes",
            Severity,
            isEnabledByDefault: true);
        return Diagnostic.Create(desc, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None);
    }

    public static DiagnosticInfo NoExportableMethods(string typeName, Location? location) =>
        new(
            "MAUIAI003",
            DiagnosticSeverity.Warning,
            $"[AIToolSource(typeof({typeName}))] references a type with no [ExportAIFunction] members.",
            LocationInfo.From(location));

    public static DiagnosticInfo UnserializableParameter(string methodName, string paramName, string typeName, Location? location) =>
        new(
            "MAUIAI002",
            DiagnosticSeverity.Warning,
            $"Parameter '{paramName}' ({typeName}) on '{methodName}' is unlikely to be JSON-serializable. Consider annotating it with [FromServices]/[FromKeyedServices] or using a supported type.",
            LocationInfo.From(location));

    public static DiagnosticInfo UnsupportedSignature(string methodName, string reason, Location? location) =>
        new(
            "MAUIAI004",
            DiagnosticSeverity.Error,
            $"[ExportAIFunction] method '{methodName}' has an unsupported signature: {reason}",
            LocationInfo.From(location));

    public static DiagnosticInfo FilteredToolNotFound(string toolName, string typeName, Location? location) =>
        new(
            "MAUIAI006",
            DiagnosticSeverity.Warning,
            $"IncludeTools lists '{toolName}' but no [ExportAIFunction] method or property with that name was found on '{typeName}'.",
            LocationInfo.From(location));
}
