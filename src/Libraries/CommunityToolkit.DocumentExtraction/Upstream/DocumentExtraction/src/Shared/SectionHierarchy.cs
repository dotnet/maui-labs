// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction;

internal static class SectionHierarchy
{
    internal sealed record CellContent(
        ExtractionDocumentTableElement Table,
        ExtractionDocumentTableCell Cell,
        IReadOnlyList<string> Elements);

    public static void Order(ExtractionDocument document, IReadOnlyDictionary<ExtractionDocumentElement, int> offsets)
    {
        var projected = new List<(ExtractionDocumentSection Page, ExtractionDocumentElement Element)>();
        foreach (ExtractionDocumentSection page in document.Sections.ToArray())
        {
            foreach (ExtractionDocumentElement element in page.Elements.Where(IsCurrent).ToArray())
            {
                page.Elements.Remove(element);
                projected.AddRange(Project(element));
            }
        }

        foreach (ExtractionDocumentSection page in document.Sections)
        {
            // Offsets belong to one SDK content result, not to earlier CU contents.
            page.Elements.AddRange(projected.Where(item => ReferenceEquals(item.Page, page))
                .OrderBy(item => Position(item.Element)).Select(item => item.Element));
        }

        bool IsCurrent(ExtractionDocumentElement element)
            => offsets.ContainsKey(element) ||
                element is ExtractionDocumentSection section && section.Elements.Any(IsCurrent);

        int Position(ExtractionDocumentElement element)
        {
            if (offsets.TryGetValue(element, out int offset))
            {
                return offset;
            }

            if (element is ExtractionDocumentSection section)
            {
                foreach (ExtractionDocumentElement child in section.Elements)
                {
                    int position = Position(child);
                    if (position != int.MaxValue)
                    {
                        return position;
                    }
                }
            }

            return int.MaxValue;
        }

        IReadOnlyList<(ExtractionDocumentSection Page, ExtractionDocumentElement Element)> Project(ExtractionDocumentElement element)
        {
            if (element is not ExtractionDocumentSection section || section.Elements.Count == 0)
            {
                return [(ProviderAdapterHelpers.GetSection(document, element.Sources), element)];
            }

            var groups = new List<(ExtractionDocumentSection Page, ExtractionDocumentSection Fragment)>();
            foreach (ExtractionDocumentElement child in section.Elements)
            {
                foreach (var item in Project(child))
                {
                    var group = groups.FirstOrDefault(candidate => ReferenceEquals(candidate.Page, item.Page));
                    if (group.Fragment is null)
                    {
                        var fragment = new ExtractionDocumentSection(section.Content);
                        fragment.Sources.AddRange(section.Sources);
                        group = (item.Page, fragment);
                        groups.Add(group);
                    }

                    group.Fragment.Elements.Add(item.Element);
                }
            }

            if (groups.Count == 1)
            {
                section.Elements.Clear();
                section.Elements.AddRange(groups[0].Fragment.Elements);
                return [(groups[0].Page, section)];
            }

            // Continuations retain native attribution; unowned text is never adopted.
            return groups.Select(group => (group.Page, (ExtractionDocumentElement)group.Fragment)).ToArray();
        }
    }

    public static void Apply(
        ExtractionDocument document,
        IReadOnlyDictionary<string, ExtractionDocumentElement> elements,
        IReadOnlyList<(ExtractionDocumentSection Section, IReadOnlyList<string> Elements)> sections,
        IReadOnlyList<CellContent>? cells = null)
    {
        if (sections.Count == 0 && (cells is null || cells.Count == 0))
        {
            return;
        }

        var paths = elements.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        for (int index = 0; index < sections.Count; index++)
        {
            paths.Add($"/sections/{index}", sections[index].Section);
        }

        var parents = new Dictionary<ExtractionDocumentElement, ExtractionDocumentElement>();
        var cellOwners = new Dictionary<ExtractionDocumentElement, ExtractionDocumentTableElement>();
        var cellContainers = new List<(ExtractionDocumentSection Section, ExtractionDocumentTableElement Table)>();
        foreach (CellContent cell in cells ?? [])
        {
            if (cell.Elements.Count == 0)
            {
                continue;
            }

            var container = (ExtractionDocumentSection)cell.Cell.Content!;
            parents.Add(container, cell.Table);
            cellContainers.Add((container, cell.Table));
            foreach (string reference in cell.Elements)
            {
                ExtractionDocumentElement element = Resolve(reference);
                if (parents.ContainsKey(element))
                {
                    throw new InvalidDataException($"The provider element '{reference}' belongs to more than one cell or is repeated.");
                }

                parents.Add(element, container);
                cellOwners.Add(element, cell.Table);
                container.Elements.Add(element);
            }
        }

        var redundantMemberships = new List<(ExtractionDocumentSection Section, ExtractionDocumentTableElement Table)>();
        var retainedCellMemberships = new List<(ExtractionDocumentSection Section, ExtractionDocumentSection Container)>();
        var sectionClaims = new Dictionary<ExtractionDocumentElement, List<ExtractionDocumentSection>>();
        foreach (var (section, references) in sections)
        {
            var seen = new HashSet<ExtractionDocumentElement>();
            foreach (string reference in references)
            {
                ExtractionDocumentElement element = Resolve(reference);
                if (!seen.Add(element))
                {
                    throw new InvalidDataException($"The provider element '{reference}' belongs to more than one section entry.");
                }

                if (cellOwners.TryGetValue(element, out ExtractionDocumentTableElement? table))
                {
                    redundantMemberships.Add((section, table));
                    continue;
                }

                if (!sectionClaims.TryGetValue(element, out List<ExtractionDocumentSection>? claims))
                {
                    claims = [];
                    sectionClaims.Add(element, claims);
                }

                claims.Add(section);
            }
        }

        foreach (var claim in sectionClaims)
        {
            ExtractionDocumentElement element = claim.Key;
            List<ExtractionDocumentSection> claims = claim.Value;
            if (claims.Count == 1)
            {
                parents.Add(element, claims[0]);
                continue;
            }

            ExtractionDocumentSection? owner = null;
            ExtractionDocumentTableElement? ownerTable = null;
            ExtractionDocumentSection? ownerContainer = null;
            // Ancestor sections may repeat descendants of a cell-owned section.
            foreach (ExtractionDocumentSection candidate in claims)
            {
                foreach (var (container, table) in cellContainers)
                {
                    if (CanReach(candidate, container) &&
                        claims.All(other => ReferenceEquals(other, candidate) || CanReach(table, other)))
                    {
                        if (owner is not null && !ReferenceEquals(owner, candidate))
                        {
                            throw new InvalidDataException("A provider element has ambiguous cell-section ownership.");
                        }

                        owner = candidate;
                        ownerTable = table;
                        ownerContainer = container;
                        break;
                    }
                }
            }

            if (owner is null || ownerTable is null || ownerContainer is null)
            {
                throw new InvalidDataException("A provider element belongs to more than one section.");
            }

            parents.Add(element, owner);
            retainedCellMemberships.Add((owner, ownerContainer));
            redundantMemberships.AddRange(claims.Where(section => !ReferenceEquals(section, owner))
                .Select(section => (section, ownerTable)));
        }

        foreach (ExtractionDocumentElement element in parents.Keys)
        {
            var visited = new HashSet<ExtractionDocumentElement>();
            ExtractionDocumentElement current = element;
            while (parents.TryGetValue(current, out ExtractionDocumentElement? parent))
            {
                if (!visited.Add(current))
                {
                    throw new InvalidDataException("The provider returned a cyclic section or table-cell hierarchy.");
                }

                current = parent;
            }
        }

        foreach (var (section, table) in redundantMemberships)
        {
            if (!HasAncestor(table, section))
            {
                throw new InvalidDataException("A cell-owned element has conflicting native section membership.");
            }
        }

        foreach (var (section, container) in retainedCellMemberships)
        {
            if (!HasAncestor(section, container))
            {
                throw new InvalidDataException("A provider section has ambiguous cell ancestry.");
            }
        }

        foreach (ExtractionDocumentSection root in document.Sections)
        {
            root.Elements.RemoveAll(parents.ContainsKey);
        }

        foreach (var (section, references) in sections)
        {
            foreach (string reference in references)
            {
                ExtractionDocumentElement element = Resolve(reference);
                if (parents.TryGetValue(element, out ExtractionDocumentElement? parent) && ReferenceEquals(parent, section))
                {
                    section.Elements.Add(element);
                }
            }

            if (!parents.ContainsKey(section))
            {
                ProviderAdapterHelpers.GetSection(document, section.Sources).Elements.Add(section);
            }
        }

        ExtractionDocumentElement Resolve(string reference)
        {
            if (reference is null || !paths.TryGetValue(reference, out ExtractionDocumentElement? element))
            {
                throw new InvalidDataException($"The provider references an unsupported element '{reference}'.");
            }

            return element;
        }

        bool CanReach(ExtractionDocumentElement element, ExtractionDocumentElement ancestor)
        {
            var pending = new Stack<ExtractionDocumentElement>();
            var visited = new HashSet<ExtractionDocumentElement>();
            pending.Push(element);
            while (pending.Count > 0)
            {
                ExtractionDocumentElement current = pending.Pop();
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                if (!visited.Add(current))
                {
                    continue;
                }

                if (sectionClaims.TryGetValue(current, out List<ExtractionDocumentSection>? claims))
                {
                    foreach (ExtractionDocumentSection section in claims)
                    {
                        pending.Push(section);
                    }
                }
                else if (parents.TryGetValue(current, out ExtractionDocumentElement? parent))
                {
                    pending.Push(parent);
                }
            }

            return false;
        }

        bool HasAncestor(ExtractionDocumentElement element, ExtractionDocumentElement ancestor)
        {
            while (!ReferenceEquals(element, ancestor) && parents.TryGetValue(element, out ExtractionDocumentElement? parent))
            {
                element = parent;
            }

            return ReferenceEquals(element, ancestor);
        }
    }
}
