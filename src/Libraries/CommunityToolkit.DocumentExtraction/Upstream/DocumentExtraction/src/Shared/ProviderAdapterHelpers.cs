// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction;

internal static class ProviderAdapterHelpers
{
    public static void ValidateInput(Stream document, string identifier, string mediaType)
    {
        Throw.IfNull(document);
        Throw.IfNullOrWhitespace(identifier);
        Throw.IfNullOrWhitespace(mediaType);
        if (!document.CanRead)
        {
            throw new ArgumentException("The input document stream must be readable.", nameof(document));
        }
    }

    public static void AddElement(
        ExtractionDocumentSection section,
        ExtractionDocumentElement element,
        IEnumerable<ExtractionDocumentSourceReference>? sources = null)
    {
        if (sources is not null)
        {
            element.Sources.AddRange(sources);
        }

        section.Elements.Add(element);
    }

    public static ExtractionDocumentSection GetSection(
        ExtractionDocument document, IReadOnlyList<ExtractionDocumentSourceReference> sources)
    {
        ExtractionDocumentSourcePage? page = sources.FirstOrDefault()?.Page;
        ExtractionDocumentSection? section = document.Sections.FirstOrDefault(candidate =>
            page is null ? candidate.Sources.Count == 0 :
            candidate.Sources.Any(source => ReferenceEquals(source.Page, page)));
        if (section is null)
        {
            section = new ExtractionDocumentSection();
            document.Sections.Add(section);
        }

        return section;
    }

    public static DocumentExtractionOptions NormalizeOptions(DocumentExtractionOptions? options)
        => options?.Clone() ?? new DocumentExtractionOptions();

    public static void RejectUnsupportedProperties(
        DocumentExtractionOptions options, string providerName, params string[] supportedProperties)
    {
        var supported = new HashSet<string>(supportedProperties, StringComparer.Ordinal);
        string[] unsupported = options.AdditionalProperties.Keys
            .Where(key => !supported.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        if (unsupported.Length > 0)
        {
            throw new NotSupportedException(
                $"{providerName} does not support extraction option(s): {string.Join(", ", unsupported)}.");
        }
    }

    public static string ResolveExtractor(
        DocumentExtractionOptions options, string defaultExtractor, string providerName, bool allowOverride = true)
    {
        if (options.ExtractorName is null)
        {
            return defaultExtractor;
        }

        if (!allowOverride && !string.Equals(options.ExtractorName, defaultExtractor, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"{providerName} is configured for extractor '{defaultExtractor}' and does not support per-request overrides.");
        }

        return options.ExtractorName;
    }

    public static T? GetProperty<T>(DocumentExtractionOptions options, string name)
    {
        if (!options.AdditionalProperties.TryGetValue(name, out object? value))
        {
            return default;
        }

        if (value is T typed)
        {
            return typed;
        }

        throw new ArgumentException(
            $"Extraction option '{name}' must be a {typeof(T).Name}, but received {value?.GetType().Name ?? "null"}.",
            nameof(options));
    }

    public static (ExtractionDocumentSourcePage SourcePage, ExtractionDocumentSourceReference Reference) CreatePageFrame(
        string identifier,
        string providerName,
        int? pageNumber,
        double? width,
        double? height,
        ExtractionDocumentCoordinateUnit? unit,
        ExtractionDocumentCoordinateOrigin? origin)
    {
        var sourcePage = new ExtractionDocumentSourcePage(
            $"{identifier}:{providerName}:page:{pageNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unnumbered"}",
            pageNumber,
            width.HasValue && height.HasValue
                ? new ExtractionDocumentPageDimensions(width.Value, height.Value)
                : null,
            unit, origin);
        return (sourcePage, new ExtractionDocumentSourceReference(sourcePage));
    }

    public static ExtractionDocumentSourceReference CreateSource(
        ExtractionDocumentSourcePage sourcePage, IEnumerable<float>? coordinates, object? nativeRepresentation = null)
    {
        float[] values = coordinates?.ToArray() ?? [];
        if (values.Length % 2 != 0)
        {
            throw new InvalidDataException("A provider source polygon must contain complete coordinate pairs.");
        }

        var points = new List<ExtractionDocumentPoint>(values.Length / 2);
        for (int index = 0; index < values.Length; index += 2)
        {
            points.Add(new ExtractionDocumentPoint(values[index], values[index + 1]));
        }

        return new ExtractionDocumentSourceReference(sourcePage, points, nativeRepresentation);
    }

    public static ExtractionDocumentSourceReference CreateSource(
        ExtractionDocumentSourcePage sourcePage,
        IEnumerable<System.Drawing.PointF>? points,
        object? nativeRepresentation = null)
        => new(sourcePage,
            (points ?? []).Select(point => new ExtractionDocumentPoint(point.X, point.Y)).ToArray(),
            nativeRepresentation);

    public static string? NormalizeTextRole(string? role)
        => role?.ToLowerInvariant() switch
        {
            null or "paragraph" or "plain" or "text" => null,
            "title" or "sectionheading" or "sectionheader" or "heading" => "heading",
            "pageheader" => "pageHeader",
            "pagefooter" => "pageFooter",
            _ => role,
        };
}
