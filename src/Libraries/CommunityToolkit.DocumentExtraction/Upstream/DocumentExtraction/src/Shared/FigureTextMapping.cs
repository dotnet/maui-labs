// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction;

internal static class FigureTextMapping
{
    internal sealed record FigureContent(
        ExtractionDocumentImageElement Image,
        IReadOnlyList<string> Elements);

    public static void Apply(
        ExtractionDocument document,
        IReadOnlyDictionary<string, ExtractionDocumentElement> elements,
        IReadOnlyList<FigureContent> figures,
        IEnumerable<string> ownedElements,
        IEnumerable<string?> descriptions)
    {
        var excluded = new HashSet<string>(ownedElements, StringComparer.Ordinal);
        string[] descriptiveText = descriptions.Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!).ToArray();
        var claims = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (FigureContent figure in figures)
        {
            foreach (string reference in figure.Elements.Distinct(StringComparer.Ordinal))
            {
                claims.TryGetValue(reference, out int count);
                claims[reference] = count + 1;
            }
        }

        foreach (FigureContent figure in figures)
        {
            ExtractionDocumentSourceReference[] bounds = figure.Image.Sources.ToArray();
            var lines = new List<string>();
            foreach (string reference in figure.Elements.Distinct(StringComparer.Ordinal))
            {
                if (excluded.Contains(reference) || claims[reference] != 1 ||
                    !reference.StartsWith("/paragraphs/", StringComparison.Ordinal) ||
                    !elements.TryGetValue(reference, out ExtractionDocumentElement? element) ||
                    element is not ExtractionDocumentTextElement text || !string.IsNullOrEmpty(text.Role) ||
                    text.Content is not TextContent content || string.IsNullOrWhiteSpace(content.Text) ||
                    descriptiveText.Any(description => description.IndexOf(content.Text, StringComparison.Ordinal) >= 0) ||
                    !IsContained(text.Sources, bounds))
                {
                    continue;
                }

                lines.Add(content.Text);
                figure.Image.Sources.AddRange(text.Sources);
                foreach (ExtractionDocumentSection page in document.Sections)
                {
                    page.Elements.Remove(text);
                }
            }

            figure.Image.Text = lines.Count == 0 ? null : string.Join("\n", lines);
        }
    }

    private static bool IsContained(
        IReadOnlyList<ExtractionDocumentSourceReference> text,
        IReadOnlyList<ExtractionDocumentSourceReference> figure)
    {
        if (text.Count == 0)
        {
            return false;
        }

        foreach (var page in text.GroupBy(source => source.Page))
        {
            // A page-only duplicate reference is not additional geometric evidence.
            if (!page.Any(source => source.Polygon.Count >= 3) ||
                page.Where(source => source.Polygon.Count > 0).Any(source =>
                    !figure.Any(bound => ReferenceEquals(bound.Page, source.Page) &&
                        Contains(bound.Polygon, source.Polygon))))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(
        IReadOnlyList<ExtractionDocumentPoint> boundary,
        IReadOnlyList<ExtractionDocumentPoint> points)
    {
        if (boundary.Count < 3 || points.Count < 3)
        {
            return false;
        }

        int orientation = 0;
        for (int index = 0; index < boundary.Count; index++)
        {
            double cross = Cross(boundary[index], boundary[(index + 1) % boundary.Count],
                boundary[(index + 2) % boundary.Count]);
            if (cross != 0)
            {
                int sign = Math.Sign(cross);
                if (orientation != 0 && orientation != sign)
                {
                    // Concave/degenerate bounds do not establish exclusive containment.
                    return false;
                }

                orientation = sign;
            }
        }

        return orientation != 0 && points.All(point =>
            Enumerable.Range(0, boundary.Count).All(index =>
                Cross(boundary[index], boundary[(index + 1) % boundary.Count], point) * orientation >= 0));
    }

    private static double Cross(ExtractionDocumentPoint start, ExtractionDocumentPoint end, ExtractionDocumentPoint point)
        => (end.X - start.X) * (point.Y - start.Y) - (end.Y - start.Y) * (point.X - start.X);
}
