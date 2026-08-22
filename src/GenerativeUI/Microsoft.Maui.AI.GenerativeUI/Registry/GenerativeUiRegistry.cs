using System.Text;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Registry;

/// <summary>
/// The mutable catalog of app-registered styles, controls, and screens that extend the built-in DSL
/// vocabulary. Populated during <c>AddGenerativeUi(...)</c> and/or at runtime by resolving it from DI.
/// The library pre-registers a base style set. See
/// <c>docs/GenerativeUI/spec/appendix-extensibility.md</c>.
/// </summary>
public sealed class GenerativeUiRegistry
{
    private readonly Dictionary<string, UiStyleRegistration> _styles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UiControlRegistration> _controls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UiScreenRegistration> _screens = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UiComponentRegistration> _components = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UiScaffoldRegistration> _scaffolds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public GenerativeUiRegistry() => RegisterBaseStyles();

    // ── Styles ──────────────────────────────────────────────────────────────────────────────────

    public GenerativeUiRegistry AddStyle(string name, string description, IReadOnlyList<string> appliesTo, string? resourceKey = null)
    {
        lock (_gate)
        {
            _styles[name] = new UiStyleRegistration
            {
                Name = name,
                Description = description,
                AppliesTo = appliesTo,
                ResourceKey = resourceKey ?? name,
            };
        }
        return this;
    }

    // ── Controls ────────────────────────────────────────────────────────────────────────────────

    public GenerativeUiRegistry AddControl<TControl>(string name, string description, IReadOnlyList<UiProp>? props = null)
        where TControl : notnull
    {
        lock (_gate)
        {
            if (IsBuiltInNodeType(name))
                throw new InvalidOperationException($"Control '{name}' shadows a built-in DSL node type.");
            _controls[name] = new UiControlRegistration
            {
                Name = name,
                Description = description,
                ControlType = typeof(TControl),
                Props = props ?? [],
            };
        }
        return this;
    }

    // ── Screens ─────────────────────────────────────────────────────────────────────────────────

    public GenerativeUiRegistry AddScreen<TScreen>(string name, string description, IReadOnlyList<UiProp>? inputs = null)
        where TScreen : notnull
    {
        lock (_gate)
        {
            _screens[name] = new UiScreenRegistration
            {
                Name = name,
                Description = description,
                ScreenType = typeof(TScreen),
                Inputs = inputs ?? [],
            };
        }
        return this;
    }

    // ── Composition components & scaffolds ─────────────────────────────────────────────────────

    public GenerativeUiRegistry AddComponent<TComponent>(ComponentDescriptor descriptor)
        where TComponent : notnull
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.DataContract);

        lock (_gate)
        {
            _components[descriptor.Alias] = new UiComponentRegistration
            {
                Descriptor = descriptor,
                ComponentType = typeof(TComponent),
            };
        }

        return this;
    }

    public GenerativeUiRegistry AddScaffold<TScaffold>(
        string name,
        string description,
        IReadOnlyList<CompositionSlotDescriptor> slots)
        where TScaffold : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(slots);

        lock (_gate)
        {
            _scaffolds[name] = new UiScaffoldRegistration
            {
                Name = name,
                Description = description,
                ScaffoldType = typeof(TScaffold),
                Slots = slots,
            };
        }

        return this;
    }

    /// <summary>Removes a style, control, or screen by name (base styles cannot be removed).</summary>
    public bool Remove(string name)
    {
        lock (_gate)
        {
            if (_styles.TryGetValue(name, out var s) && s.IsBuiltIn)
                return false;
            return _styles.Remove(name) |
                   _controls.Remove(name) |
                   _screens.Remove(name) |
                   _components.Remove(name) |
                   _scaffolds.Remove(name);
        }
    }

    // ── Lookups ─────────────────────────────────────────────────────────────────────────────────

    public UiStyleRegistration? GetStyle(string name)
    {
        lock (_gate) return _styles.GetValueOrDefault(name);
    }

    public UiControlRegistration? GetControl(string name)
    {
        lock (_gate) return _controls.GetValueOrDefault(name);
    }

    public UiScreenRegistration? GetScreen(string name)
    {
        lock (_gate) return _screens.GetValueOrDefault(name);
    }

    public UiComponentRegistration? GetComponent(string name)
    {
        lock (_gate) return _components.GetValueOrDefault(name);
    }

    public UiScaffoldRegistration? GetScaffold(string name)
    {
        lock (_gate) return _scaffolds.GetValueOrDefault(name);
    }

    public IReadOnlyList<UiStyleRegistration> Styles
    {
        get { lock (_gate) return [.. _styles.Values]; }
    }

    public IReadOnlyList<UiControlRegistration> Controls
    {
        get { lock (_gate) return [.. _controls.Values]; }
    }

    public IReadOnlyList<UiScreenRegistration> Screens
    {
        get { lock (_gate) return [.. _screens.Values]; }
    }

    public IReadOnlyList<UiComponentRegistration> Components
    {
        get { lock (_gate) return [.. _components.Values]; }
    }

    public IReadOnlyList<UiScaffoldRegistration> Scaffolds
    {
        get { lock (_gate) return [.. _scaffolds.Values]; }
    }

    /// <summary>
    /// A compact catalog of the current registrations (names + descriptions + appliesTo/props/inputs),
    /// suitable for seeding into the system prompt so the model knows what it can use.
    /// </summary>
    public string DescribeCatalog()
    {
        var sb = new StringBuilder();
        lock (_gate)
        {
            sb.AppendLine("Registered styles (style tokens):");
            foreach (var s in _styles.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"- {s.Name} (on {string.Join("/", s.AppliesTo)}): {s.Description}");

            if (_controls.Count > 0)
            {
                sb.AppendLine().AppendLine("Registered controls (node types with a 'props' object):");
                foreach (var c in _controls.Values)
                {
                    sb.AppendLine($"- {c.Name}: {c.Description}");
                    foreach (var p in c.Props)
                        sb.AppendLine($"    · {p.Name}{(p.Editable ? " (editable)" : "")}: {p.Description}");
                }
            }

            if (_screens.Count > 0)
            {
                sb.AppendLine().AppendLine("Registered screens (use present_screen):");
                foreach (var s in _screens.Values)
                {
                    sb.AppendLine($"- {s.Name}: {s.Description}");
                    foreach (var i in s.Inputs)
                        sb.AppendLine($"    · {i.Name}: {i.Description}");
                }
            }

            if (_components.Count > 0)
            {
                sb.AppendLine().AppendLine("Registered composition components:");
                foreach (var component in _components.Values.OrderBy(
                             component => component.Descriptor.Alias,
                             StringComparer.OrdinalIgnoreCase))
                {
                    var descriptor = component.Descriptor;
                    sb.AppendLine(
                        $"- {descriptor.Alias} ({descriptor.DataContract}; slots: {string.Join("/", descriptor.AllowedSlots)}): " +
                        descriptor.Description);
                    if (descriptor.RequiredBindings.Count > 0)
                        sb.AppendLine($"    · required bindings: {string.Join(", ", descriptor.RequiredBindings)}");
                    if (descriptor.OptionalBindings.Count > 0)
                        sb.AppendLine($"    · optional bindings: {string.Join(", ", descriptor.OptionalBindings)}");
                    if (descriptor.Variants.Count > 0)
                        sb.AppendLine($"    · variants: {string.Join(", ", descriptor.Variants)}");
                }
            }

            if (_scaffolds.Count > 0)
            {
                sb.AppendLine().AppendLine("Registered composition scaffolds:");
                foreach (var scaffold in _scaffolds.Values.OrderBy(
                             scaffold => scaffold.Name,
                             StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine(
                        $"- {scaffold.Name} (slots: {string.Join("/", scaffold.Slots.Select(slot => slot.Slot))}): " +
                        scaffold.Description);
                }
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Built-in DSL node types (cannot be shadowed by registered controls).</summary>
    public static bool IsBuiltInNodeType(string type) => BuiltInNodeTypes.Contains(type);

    public static readonly IReadOnlySet<string> BuiltInNodeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Stack", "Grid", "Card", "Scroll", "Separator", "Spacer",
        "Label", "Image", "Badge", "Icon",
        "Button", "Field", "Entry",
        "List", "Screen",
    };

    private void RegisterBaseStyles()
    {
        void Base(string name, string description, params string[] appliesTo) =>
            _styles[name] = new UiStyleRegistration
            {
                Name = name,
                Description = description,
                AppliesTo = appliesTo,
                ResourceKey = name,
                IsBuiltIn = true,
            };

        Base("Title", "Large emphasized heading text.", "Label");
        Base("Subtitle", "Secondary heading / prominent value text.", "Label");
        Base("Body", "Default body text.", "Label");
        Base("Caption", "Small muted supporting text.", "Label");
        Base("Mono", "Monospace text (codes, SKUs).", "Label");

        Base("primary", "Emphasized call-to-action — the single main action on a screen.", "Button");
        Base("secondary", "Secondary / less prominent action.", "Button");
        Base("danger", "Destructive action (delete, remove, clear). Signals irreversible intent.", "Button");
    }
}
