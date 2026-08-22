namespace Microsoft.Maui.AI.GenerativeUI.Composition;

/// <summary>
/// Deterministically validates the strict component-layout DSL before it reaches the renderer.
/// </summary>
public sealed class ComponentLayoutValidator
{
    public ComponentLayoutValidationResult Validate(
        ComponentLayoutDocument document,
        AdaptiveSurfaceContext context,
        ComponentLayoutDocument? currentLayout = null,
        string? expectedLayoutId = null,
        int? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<ComponentLayoutValidationError>();
        var nodes = document.Nodes ?? [];
        var regions = document.Regions ?? [];
        var nodesById = new Dictionary<string, ComponentLayoutNode>(StringComparer.Ordinal);
        var regionByRoot = new Dictionary<string, AdaptiveRegionPlan>(StringComparer.Ordinal);
        var regionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedRegions = context.Surface.Regions.ToDictionary(region => region.Name, StringComparer.OrdinalIgnoreCase);
        var catalog = context.ComponentCatalog.ToDictionary(component => component.Alias, StringComparer.OrdinalIgnoreCase);

        if (!string.Equals(document.Surface, context.Surface.Surface, StringComparison.Ordinal))
            Add("surface_mismatch", "$.surface", $"Expected surface '{context.Surface.Surface}'.");

        if (string.IsNullOrWhiteSpace(document.LayoutId))
            Add("missing_layout_id", "$.layoutId", "A stable layout ID is required.");
        else if (expectedLayoutId is not null &&
                 !string.Equals(document.LayoutId, expectedLayoutId, StringComparison.Ordinal))
            Add("unexpected_layout_id", "$.layoutId", $"Expected layout ID '{expectedLayoutId}'.");

        if (document.Revision < 1)
            Add("invalid_revision", "$.revision", "Revision must be at least 1.");
        else if (expectedRevision is not null && document.Revision != expectedRevision)
            Add("unexpected_revision", "$.revision", $"Expected revision {expectedRevision}.");

        if (nodes.Count > context.Surface.MaxNodes)
            Add("node_limit_exceeded", "$.nodes", $"At most {context.Surface.MaxNodes} nodes are allowed.");

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var path = $"$.nodes[{index}]";
            if (node is null)
            {
                Add("null_node", path, "Layout nodes cannot be null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.Id))
            {
                Add("missing_node_id", $"{path}.id", "Every node requires a stable ID.");
            }
            else if (!nodesById.TryAdd(node.Id, node))
            {
                Add("duplicate_node_id", $"{path}.id", $"Node ID '{node.Id}' is duplicated.");
            }

            if (node.Order < 0)
                Add("invalid_order", $"{path}.order", "Order cannot be negative.");

            if (string.IsNullOrWhiteSpace(node.Reason))
                Add("missing_reason", $"{path}.reason", "Every node must explain why it was selected.");
        }

        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];
            var path = $"$.regions[{index}]";
            if (region is null)
            {
                Add("null_region", path, "Region plans cannot be null.");
                continue;
            }

            if (!allowedRegions.ContainsKey(region.Region))
                Add("unknown_region", $"{path}.region", $"Region '{region.Region}' is not available on this surface.");
            else if (!regionNames.Add(region.Region))
                Add("duplicate_region", $"{path}.region", $"Region '{region.Region}' is defined more than once.");

            if (!regionByRoot.TryAdd(region.RootNodeId, region))
                Add("duplicate_region_root", $"{path}.rootNodeId", $"Root node '{region.RootNodeId}' is used by multiple regions.");

            if (!nodesById.TryGetValue(region.RootNodeId, out var root))
                Add("unknown_region_root", $"{path}.rootNodeId", $"Root node '{region.RootNodeId}' does not exist.");
            else if (root.ParentId is not null)
                Add("region_root_has_parent", $"{path}.rootNodeId", "A region root node cannot have a parent.");
        }

        var validNodes = nodes.OfType<ComponentLayoutNode>().ToArray();
        var validRegions = regions.OfType<AdaptiveRegionPlan>().ToArray();
        foreach (var required in context.Surface.Regions.Where(region => region.IsRequired))
        {
            if (!validRegions.Any(region => string.Equals(region.Region, required.Name, StringComparison.OrdinalIgnoreCase)))
                Add("missing_required_region", "$.regions", $"Required region '{required.Name}' is missing.");
        }

        var siblingOrders = new HashSet<(string ParentId, int Order)>();
        for (var index = 0; index < validNodes.Length; index++)
        {
            var node = validNodes[index];
            var path = NodePath(node.Id, nodes);
            if (node.ParentId is not null && !nodesById.ContainsKey(node.ParentId))
                Add("unknown_parent", $"{path}.parentId", $"Parent node '{node.ParentId}' does not exist.");

            var siblingParent = node.ParentId ??
                (regionByRoot.TryGetValue(node.Id, out var rootRegion)
                    ? $"$region:{rootRegion.Region}"
                    : $"$orphan:{node.Id}");
            var siblingKey = (siblingParent, node.Order);
            if (!siblingOrders.Add(siblingKey))
                Add("duplicate_sibling_order", $"{path}.order", "Sibling order values must be unique.");

            ValidateKind(node, path, validNodes, catalog, regionByRoot, nodesById, Add);
        }

        foreach (var node in validNodes)
        {
            if (!TryFindRegion(node, nodesById, regionByRoot, context.Surface.MaxDepth, out var region, out var error))
            {
                Add(error.Code, NodePath(node.Id, nodes), error.Message);
                continue;
            }

            if (node.Kind == ComponentLayoutNodeKind.Component &&
                catalog.TryGetValue(node.Component ?? string.Empty, out var entry) &&
                !entry.AllowedRegions.Contains(region.Region, StringComparer.OrdinalIgnoreCase))
            {
                Add(
                    "component_region_not_allowed",
                    $"{NodePath(node.Id, nodes)}.component",
                    $"Component '{node.Component}' is not allowed in region '{region.Region}'.");
            }
        }

        if (currentLayout is not null)
            ValidateContinuity(document, currentLayout, issues);

        return new ComponentLayoutValidationResult(issues);

        void Add(string code, string path, string message, bool warning = false)
            => issues.Add(new ComponentLayoutValidationError(code, path, message, warning));
    }

    private static void ValidateKind(
        ComponentLayoutNode node,
        string path,
        IReadOnlyList<ComponentLayoutNode> nodes,
        IReadOnlyDictionary<string, AdaptiveComponentCatalogEntry> catalog,
        IReadOnlyDictionary<string, AdaptiveRegionPlan> regionByRoot,
        IReadOnlyDictionary<string, ComponentLayoutNode> nodesById,
        Action<string, string, string, bool> add)
    {
        var children = nodes.Where(candidate => string.Equals(candidate.ParentId, node.Id, StringComparison.Ordinal)).ToArray();
        if (node.Orientation is { } orientation && !Enum.IsDefined(orientation))
            add("unknown_orientation", $"{path}.orientation", $"Orientation '{orientation}' is not supported.", false);

        if (node.GridPreset is { } gridPreset && !Enum.IsDefined(gridPreset))
            add("unknown_grid_preset", $"{path}.gridPreset", $"Grid preset '{gridPreset}' is not supported.", false);

        if (!Enum.IsDefined(node.Kind))
        {
            add("unknown_node_kind", $"{path}.kind", $"Node kind '{node.Kind}' is not supported.", false);
            return;
        }
        if (node.Kind == ComponentLayoutNodeKind.Component)
        {
            if (children.Length > 0)
                add("component_has_children", path, "Whole-component nodes must be leaves.", false);

            if (string.IsNullOrWhiteSpace(node.Component))
            {
                add("missing_component", $"{path}.component", "A component alias is required.", false);
                return;
            }

            if (!catalog.TryGetValue(node.Component, out var component))
            {
                add("unknown_component", $"{path}.component", $"Component '{node.Component}' is not registered.", false);
            }
            else
            {
                if (!component.Available)
                    add("unavailable_component", $"{path}.component", component.UnavailableReason ?? "The component is unavailable.", false);

                if (string.IsNullOrWhiteSpace(node.DataPath) ||
                    !component.CompatibleDataPaths.Contains(node.DataPath, StringComparer.Ordinal))
                {
                    add(
                        "incompatible_data_path",
                        $"{path}.dataPath",
                        $"Component '{node.Component}' cannot bind to data path '{node.DataPath}'.",
                        false);
                }

                if (!string.IsNullOrWhiteSpace(node.Variant) &&
                    !component.Variants.Contains(node.Variant, StringComparer.OrdinalIgnoreCase))
                {
                    add("unknown_variant", $"{path}.variant", $"Variant '{node.Variant}' is not registered.", false);
                }
            }

            if (node.Orientation is not null || node.GridPreset is not null || node.Title is not null)
                add("component_layout_properties", path, "Component nodes cannot define layout properties.", false);

            return;
        }

        if (node.Component is not null || node.DataPath is not null || node.Variant is not null)
            add("layout_component_properties", path, "Layout nodes cannot select components or data paths.", false);

        if (node.Kind == ComponentLayoutNodeKind.Stack && node.Orientation is null)
            add("missing_orientation", $"{path}.orientation", "Stack nodes require an orientation.", false);
        else if (node.Kind != ComponentLayoutNodeKind.Stack && node.Orientation is not null)
            add("unexpected_orientation", $"{path}.orientation", "Only Stack nodes can define orientation.", false);

        if (node.Kind == ComponentLayoutNodeKind.Grid && node.GridPreset is null)
            add("missing_grid_preset", $"{path}.gridPreset", "Grid nodes require a grid preset.", false);
        else if (node.Kind != ComponentLayoutNodeKind.Grid && node.GridPreset is not null)
            add("unexpected_grid_preset", $"{path}.gridPreset", "Only Grid nodes can define a grid preset.", false);

        if (node.Kind == ComponentLayoutNodeKind.Tabs &&
            children.Any(child => child.Kind != ComponentLayoutNodeKind.Section))
        {
            add("tabs_require_sections", path, "Tabs may contain only Section nodes.", false);
        }

        if (node.Kind == ComponentLayoutNodeKind.Section && children.Length > 1)
            add("section_child_limit", path, "A Section may contain at most one child.", false);

        if (children.Length == 0)
            add("empty_layout_node", path, "Layout nodes must contain at least one child.", false);

        if (node.ParentId is null && !regionByRoot.ContainsKey(node.Id))
            add("orphan_root", path, "A root layout node must be assigned to a region.", false);
        else if (node.ParentId is not null && !nodesById.ContainsKey(node.ParentId))
            add("unknown_parent", $"{path}.parentId", $"Parent node '{node.ParentId}' does not exist.", false);
    }

    private static bool TryFindRegion(
        ComponentLayoutNode node,
        IReadOnlyDictionary<string, ComponentLayoutNode> nodesById,
        IReadOnlyDictionary<string, AdaptiveRegionPlan> regionsByRoot,
        int maxDepth,
        out AdaptiveRegionPlan region,
        out (string Code, string Message) error)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = node;
        var depth = 0;
        while (true)
        {
            if (!visited.Add(current.Id))
            {
                region = null!;
                error = ("layout_cycle", "The node hierarchy contains a cycle.");
                return false;
            }

            if (depth > maxDepth)
            {
                region = null!;
                error = ("layout_depth_exceeded", $"Layout depth exceeds the maximum of {maxDepth}.");
                return false;
            }

            if (current.ParentId is null)
            {
                if (regionsByRoot.TryGetValue(current.Id, out region!))
                {
                    error = default;
                    return true;
                }

                region = null!;
                error = ("orphan_node", "The node is not reachable from a surface region.");
                return false;
            }

            if (!nodesById.TryGetValue(current.ParentId, out current!))
            {
                region = null!;
                error = ("unknown_parent", "The node refers to a parent that does not exist.");
                return false;
            }

            depth++;
        }
    }

    private static void ValidateContinuity(
        ComponentLayoutDocument document,
        ComponentLayoutDocument current,
        ICollection<ComponentLayoutValidationError> issues)
    {
        if (!string.Equals(document.LayoutId, current.LayoutId, StringComparison.Ordinal))
        {
            issues.Add(new(
                "layout_id_changed",
                "$.layoutId",
                $"Follow-up layouts must preserve layout ID '{current.LayoutId}'."));
        }

        if (document.Revision != current.Revision + 1)
        {
            issues.Add(new(
                "unexpected_revision",
                "$.revision",
                $"Expected revision {current.Revision + 1}."));
        }

        var currentNodes = current.Nodes.OfType<ComponentLayoutNode>().ToArray();
        var currentIds = currentNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var node in document.Nodes.OfType<ComponentLayoutNode>().Where(node => !currentIds.Contains(node.Id)))
        {
            if (currentNodes.Any(candidate => SemanticallyEquivalent(candidate, node)))
            {
                issues.Add(new(
                    "unstable_node_id",
                    NodePath(node.Id, document.Nodes),
                    $"Node '{node.Id}' appears to rename an existing semantic node. The renderer will reconcile it.",
                    IsWarning: true));
            }
        }
    }

    internal static bool SemanticallyEquivalent(ComponentLayoutNode left, ComponentLayoutNode right)
        => left.Kind == right.Kind &&
           string.Equals(left.ParentId, right.ParentId, StringComparison.Ordinal) &&
           string.Equals(left.Component, right.Component, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.DataPath, right.DataPath, StringComparison.Ordinal) &&
           string.Equals(NormalizeVariant(left.Variant), NormalizeVariant(right.Variant), StringComparison.OrdinalIgnoreCase) &&
           left.Order == right.Order;

    private static string NormalizeVariant(string? variant)
        => string.IsNullOrWhiteSpace(variant) ? "default" : variant;

    private static string NodePath(string id, IReadOnlyList<ComponentLayoutNode> nodes)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is { } node &&
                string.Equals(node.Id, id, StringComparison.Ordinal))
                return $"$.nodes[{index}]";
        }

        return "$.nodes";
    }
}
