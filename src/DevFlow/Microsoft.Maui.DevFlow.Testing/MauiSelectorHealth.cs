using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Converts an agent tree into the explicitly value-free observation shape used by selector
/// health. It intentionally does not copy <see cref="ElementInfo.Text"/> or
/// <see cref="ElementInfo.Value"/>.
/// </summary>
public static class MauiSelectorObservationFactory
{
    public static MauiSelectorObservation Create(
        ElementInfo target,
        IEnumerable<ElementInfo> tree,
        MauiSelectorObservationContext? context = null,
        bool? truncated = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tree);

        var elements = Flatten(tree)
            .Where(static element => !string.IsNullOrWhiteSpace(element.Id))
            .GroupBy(static element => element.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
        var lookup = elements.ToDictionary(static element => element.Id, StringComparer.Ordinal);
        var targetFact = ToObservationElement(target, lookup);
        var projected = elements.Select(element => ToObservationElement(element, lookup)).ToList();
        if (!projected.Any(element => string.Equals(element.Id, targetFact.Id, StringComparison.Ordinal)))
            projected.Insert(0, targetFact);

        return new MauiSelectorObservation
        {
            Target = targetFact,
            Elements = projected,
            Context = context,
            Truncated = truncated,
        };
    }

    internal static MauiSelectorObservationElement ToObservationElement(
        ElementInfo element,
        IReadOnlyDictionary<string, ElementInfo> all)
    {
        var (nativeIdentity, nativeKind) = GetNativeIdentity(element);
        return new MauiSelectorObservationElement
        {
            Id = element.Id,
            ParentId = element.ParentId,
            Type = element.Type,
            FullType = element.FullType,
            Framework = element.Framework,
            AutomationId = element.AutomationId,
            NativeAutomationIdentity = nativeIdentity,
            NativeAutomationIdentityKind = nativeKind,
            Role = element.Role,
            Traits = element.Traits?.OrderBy(static trait => trait, StringComparer.Ordinal).ToList(),
            IsVisible = element.IsVisible,
            IsEnabled = element.IsEnabled,
            IsFocused = element.IsFocused,
            Bounds = Copy(element.Bounds),
            WindowBounds = Copy(element.WindowBounds),
            SourceFile = element.SourceFile,
            SourceLine = element.SourceLine,
            SourceColumn = element.SourceColumn,
            SourceHash = element.SourceHash,
            SourceConfidence = element.SourceConfidence,
            StableItemKey = element.StableItemKey,
            CollectionScope = element.CollectionScope ?? FindCollectionScope(element, all),
            TemplateKind = element.TemplateKind ?? InferTemplateKind(element),
            IsVirtualized = element.IsVirtualized ?? IsVirtualized(element, all),
        };
    }

    private static IEnumerable<ElementInfo> Flatten(IEnumerable<ElementInfo> roots)
    {
        foreach (var element in roots)
        {
            yield return element;
            if (element.Children is { Count: > 0 })
            {
                foreach (var child in Flatten(element.Children))
                    yield return child;
            }
        }
    }

    private static (string? value, string? kind) GetNativeIdentity(ElementInfo element)
    {
        if (!string.IsNullOrWhiteSpace(element.NativeAutomationIdentity))
            return (element.NativeAutomationIdentity, element.NativeAutomationIdentityKind ?? "platform-automation-id");
        if (element.NativeProperties is null)
            return default;
        if (element.NativeProperties.TryGetValue("accessibilityIdentifier", out var accessibilityIdentifier) &&
            !string.IsNullOrWhiteSpace(accessibilityIdentifier))
        {
            return (accessibilityIdentifier, "accessibility-identifier");
        }
        if (element.NativeProperties.TryGetValue("automationId", out var automationId) &&
            !string.IsNullOrWhiteSpace(automationId))
        {
            return (automationId, "automation-id");
        }
        return default;
    }

    private static BoundsInfo? Copy(BoundsInfo? bounds) => bounds is null
        ? null
        : new BoundsInfo
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
        };

    private static string? FindCollectionScope(
        ElementInfo element,
        IReadOnlyDictionary<string, ElementInfo> all)
    {
        var current = element;
        for (var depth = 0; depth < 32 && !string.IsNullOrWhiteSpace(current.ParentId); depth++)
        {
            if (!all.TryGetValue(current.ParentId, out var parent))
                break;
            if (IsCollection(parent.Type))
                return parent.AutomationId;
            current = parent;
        }
        return null;
    }

    private static bool IsVirtualized(ElementInfo element, IReadOnlyDictionary<string, ElementInfo> all)
        => IsCollection(element.Type) || FindCollectionScope(element, all) is not null;

    private static string? InferTemplateKind(ElementInfo element)
    {
        var type = element.FullType ?? element.Type;
        return type?.Contains("ControlTemplate", StringComparison.OrdinalIgnoreCase) == true
            ? "ControlTemplate"
            : type?.Contains("DataTemplate", StringComparison.OrdinalIgnoreCase) == true
                ? "DataTemplate"
                : null;
    }

    internal static bool IsCollection(string? type)
        => type is "CollectionView" or "ListView" or "CarouselView" ||
           type?.Contains("Recycler", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Builds deterministic value-free fingerprints from live or recorded selector facts.</summary>
public static class MauiElementFingerprintBuilder
{
    public static MauiElementFingerprint Build(
        MauiSelectorObservation observation,
        string? currentSourceHash = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Target is null)
            throw new ArgumentException("A selector observation requires a target.", nameof(observation));

        return Build(observation.Target, observation.Elements, observation.Context, currentSourceHash);
    }

    public static MauiElementFingerprint Build(
        MauiSelectorObservationElement target,
        IReadOnlyList<MauiSelectorObservationElement> elements,
        MauiSelectorObservationContext? context = null,
        string? currentSourceHash = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(elements);

        var all = EnsureTarget(target, elements);
        var byId = all
            .Where(static element => !string.IsNullOrWhiteSpace(element.Id))
            .GroupBy(static element => element.Id!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var contextValue = CopyContext(context);
        var source = BuildSource(target, currentSourceHash);
        var topology = BuildTopology(target, all, byId);
        var collection = BuildCollection(target);
        var fingerprint = new MauiElementFingerprint
        {
            AppId = contextValue.AppId,
            BuildFingerprint = contextValue.AppBuild,
            Platform = contextValue.Platform,
            Route = contextValue.Route,
            Window = contextValue.Window,
            Modal = contextValue.Modal,
            ManagedType = Normalize(target.Type),
            FullType = Normalize(target.FullType),
            Role = Normalize(target.Role),
            Traits = (target.Traits ?? [])
                .Where(static trait => !string.IsNullOrWhiteSpace(trait))
                .Select(static trait => trait.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static trait => trait, StringComparer.Ordinal)
                .ToList(),
            AutomationId = Normalize(target.AutomationId),
            NativeAutomationId = Normalize(target.NativeAutomationIdentity),
            SourceAnchor = target.SourceFile is { Length: > 0 } && target.SourceLine is > 0
                ? $"{target.SourceFile}:{target.SourceLine}"
                : null,
            SourceHash = Normalize(target.SourceHash),
            SourceConfidence = Normalize(target.SourceConfidence),
            AncestorTopologyHash = topology.AncestorHash,
            SiblingTopologyHash = topology.SiblingHash,
            CollectionKey = Normalize(target.CollectionScope),
            ItemKey = Normalize(target.StableItemKey),
            Locale = contextValue.Locale,
            Theme = contextValue.Theme,
            Orientation = contextValue.Orientation,
            DisplayProfile = contextValue.DisplayProfile,
            Context = contextValue,
            Managed = new MauiManagedElementIdentity
            {
                Type = Normalize(target.Type),
                FullType = Normalize(target.FullType),
                Framework = Normalize(target.Framework),
                Role = Normalize(target.Role),
                Traits = (target.Traits ?? [])
                    .Where(static trait => !string.IsNullOrWhiteSpace(trait))
                    .Select(static trait => trait.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static trait => trait, StringComparer.Ordinal)
                    .ToList(),
                AutomationId = Normalize(target.AutomationId),
            },
            Native = string.IsNullOrWhiteSpace(target.NativeAutomationIdentity)
                ? null
                : new MauiNativeAutomationIdentity
                {
                    Identity = Normalize(target.NativeAutomationIdentity),
                    Kind = Normalize(target.NativeAutomationIdentityKind) ?? "platform-automation-id",
                    Authoritative = IsAuthoritativeNativeKind(target.NativeAutomationIdentityKind),
                },
            Source = source,
            Topology = topology,
            Collection = collection,
            NormalizedBounds = NormalizeBounds(target.Bounds, target.WindowBounds),
            ObservedAt = context?.ObservedAt,
            CapabilityVersion = Normalize(context?.CapabilityVersion) ?? "selector-observation-v1",
        };
        fingerprint.FingerprintId = "fp1_" + Hash(StableFingerprintMaterial(fingerprint))[..24];
        fingerprint.EvidenceRefs.Add($"fingerprint:{fingerprint.FingerprintId}");
        if (source?.State is { } sourceState)
            fingerprint.EvidenceRefs.Add($"source:{sourceState}");
        if (!string.IsNullOrWhiteSpace(topology.AncestorHash))
            fingerprint.EvidenceRefs.Add($"topology:{topology.AncestorHash}");
        return fingerprint;
    }

    private static List<MauiSelectorObservationElement> EnsureTarget(
        MauiSelectorObservationElement target,
        IReadOnlyList<MauiSelectorObservationElement> elements)
    {
        var result = elements.ToList();
        if (!result.Any(element => string.Equals(element.Id, target.Id, StringComparison.Ordinal)))
            result.Insert(0, target);
        return result;
    }

    private static MauiElementFingerprintContext CopyContext(MauiSelectorObservationContext? source) => new()
    {
        AppId = Normalize(source?.AppId),
        AppBuild = Normalize(source?.AppBuild),
        Platform = Normalize(source?.Platform),
        Route = Normalize(source?.Route),
        Window = Normalize(source?.Window),
        Modal = Normalize(source?.Modal),
        Locale = Normalize(source?.Locale),
        Theme = Normalize(source?.Theme),
        Orientation = Normalize(source?.Orientation),
        DisplayProfile = Normalize(source?.DisplayProfile),
    };

    private static MauiSourceAnchor BuildSource(
        MauiSelectorObservationElement target,
        string? currentSourceHash)
    {
        var buildHash = Normalize(target.SourceHash);
        var current = Normalize(currentSourceHash);
        var hasAnchor = !string.IsNullOrWhiteSpace(target.SourceFile) && target.SourceLine is > 0;
        var state = !hasAnchor
            ? "missing"
            : string.Equals(target.SourceConfidence, "ambiguous", StringComparison.OrdinalIgnoreCase)
                ? "ambiguous"
                : current is not null && buildHash is not null &&
                  !string.Equals(current, buildHash, StringComparison.Ordinal)
                    ? "stale"
                    : "current";
        return new MauiSourceAnchor
        {
            File = Normalize(target.SourceFile),
            Line = target.SourceLine,
            Column = target.SourceColumn,
            BuildHash = buildHash,
            CurrentHash = current,
            State = state,
            Confidence = Normalize(target.SourceConfidence) ?? (hasAnchor ? "mapped" : "none"),
        };
    }

    private static MauiTopologySignature BuildTopology(
        MauiSelectorObservationElement target,
        IReadOnlyList<MauiSelectorObservationElement> all,
        IReadOnlyDictionary<string, MauiSelectorObservationElement> byId)
    {
        var ancestors = new List<MauiSelectorObservationElement>();
        var current = target;
        for (var depth = 0; depth < 32 && !string.IsNullOrWhiteSpace(current.ParentId); depth++)
        {
            if (!byId.TryGetValue(current.ParentId, out var parent))
                break;
            ancestors.Add(parent);
            current = parent;
        }

        var siblingMaterial = all
            .Where(element => string.Equals(element.ParentId, target.ParentId, StringComparison.Ordinal))
            .OrderBy(static element => element.Type, StringComparer.Ordinal)
            .ThenBy(static element => element.AutomationId, StringComparer.Ordinal)
            .Select(static element => $"{Normalize(element.Type)}|{Normalize(element.AutomationId)}");
        var childMaterial = all
            .Where(element => string.Equals(element.ParentId, target.Id, StringComparison.Ordinal))
            .OrderBy(static element => element.Type, StringComparer.Ordinal)
            .ThenBy(static element => element.AutomationId, StringComparer.Ordinal)
            .Select(static element => $"{Normalize(element.Type)}|{Normalize(element.AutomationId)}");
        var stableAncestor = ancestors
            .Select(static element => Normalize(element.AutomationId))
            .FirstOrDefault(IsAppOwnedAutomationId);

        return new MauiTopologySignature
        {
            AncestorHash = ancestors.Count == 0
                ? null
                : Hash(string.Join(">", ancestors.Select(static element =>
                    $"{Normalize(element.Type)}|{Normalize(element.AutomationId)}")))[..24],
            SiblingHash = Hash(string.Join(">", siblingMaterial))[..24],
            ChildHash = Hash(string.Join(">", childMaterial))[..24],
            StableAncestorAutomationId = stableAncestor,
        };
    }

    private static MauiCollectionIdentity? BuildCollection(MauiSelectorObservationElement target)
    {
        if (string.IsNullOrWhiteSpace(target.CollectionScope) &&
            string.IsNullOrWhiteSpace(target.StableItemKey) &&
            string.IsNullOrWhiteSpace(target.TemplateKind) &&
            target.IsVirtualized is not true)
        {
            return null;
        }

        return new MauiCollectionIdentity
        {
            Scope = Normalize(target.CollectionScope),
            ItemKey = Normalize(target.StableItemKey),
            TemplateKind = Normalize(target.TemplateKind),
            Virtualized = target.IsVirtualized,
        };
    }

    private static MauiNormalizedBounds? NormalizeBounds(BoundsInfo? bounds, BoundsInfo? window)
    {
        if (bounds is null || window is null || window.Width <= 0 || window.Height <= 0)
            return null;
        return new MauiNormalizedBounds
        {
            X = Round((bounds.X - window.X) / window.Width),
            Y = Round((bounds.Y - window.Y) / window.Height),
            Width = Round(bounds.Width / window.Width),
            Height = Round(bounds.Height / window.Height),
        };
    }

    private static string StableFingerprintMaterial(MauiElementFingerprint fingerprint) => string.Join(
        "\u001f",
        fingerprint.Context.AppId,
        fingerprint.Context.AppBuild,
        fingerprint.Context.Platform,
        fingerprint.Context.Route,
        fingerprint.Context.Window,
        fingerprint.Context.Modal,
        fingerprint.Managed.Type,
        fingerprint.Managed.FullType,
        fingerprint.Managed.Role,
        string.Join(",", fingerprint.Managed.Traits),
        fingerprint.Managed.AutomationId,
        fingerprint.Native?.Identity,
        fingerprint.Native?.Kind,
        fingerprint.Source?.File,
        fingerprint.Source?.Line?.ToString(CultureInfo.InvariantCulture),
        fingerprint.Source?.BuildHash,
        fingerprint.Topology.AncestorHash,
        fingerprint.Topology.SiblingHash,
        fingerprint.Topology.ChildHash,
        fingerprint.Collection?.Scope,
        fingerprint.Collection?.ItemKey);

    internal static bool IsAppOwnedAutomationId(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.StartsWith("_mauidevflow_", StringComparison.OrdinalIgnoreCase) &&
           !value.StartsWith("runtime", StringComparison.OrdinalIgnoreCase) &&
           !value.StartsWith("generated", StringComparison.OrdinalIgnoreCase);

    internal static bool IsAuthoritativeNativeKind(string? kind)
        => kind is null ||
           kind.Equals("automation-id", StringComparison.OrdinalIgnoreCase) ||
           kind.Equals("accessibility-identifier", StringComparison.OrdinalIgnoreCase) ||
           kind.Equals("platform-automation-id", StringComparison.OrdinalIgnoreCase);

    internal static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private static double Round(double value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Generates and ranks a bounded candidate shortlist according to the fixed selector-health
/// priority policy. It does not write a flow, test candidates, or alter normal replay.
/// </summary>
public static class MauiSelectorCandidateGenerator
{
    public static MauiSelectorCandidateGenerationResult Generate(
        MauiSelectorObservation observation,
        MauiSelectorCandidateGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Target is null)
        {
            return new MauiSelectorCandidateGenerationResult
            {
                Omissions =
                [
                    new MauiSelectorEvidenceOmission
                    {
                        Kind = "fingerprint",
                        Reason = "A target observation is required before candidates can be generated.",
                    },
                ],
            };
        }

        return Generate(
            MauiElementFingerprintBuilder.Build(observation, options?.CurrentSourceHash),
            observation.Target,
            observation.Elements,
            options);
    }

    public static MauiSelectorCandidateGenerationResult Generate(
        MauiElementFingerprint fingerprint,
        MauiSelectorObservationElement target,
        IReadOnlyList<MauiSelectorObservationElement> liveElements,
        MauiSelectorCandidateGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(liveElements);

        options ??= new MauiSelectorCandidateGenerationOptions();
        var max = Math.Clamp(options.MaxCandidates, 1, 32);
        var all = liveElements.ToList();
        if (!all.Any(element => string.Equals(element.Id, target.Id, StringComparison.Ordinal)))
            all.Insert(0, target);

        var candidates = new List<MauiSelectorCandidate>();
        var omissions = new List<MauiSelectorEvidenceOmission>();
        var scope = ScopeFrom(fingerprint, options);

        // 1. A unique app-owned AutomationId.
        if (MauiElementFingerprintBuilder.IsAppOwnedAutomationId(target.AutomationId))
        {
            var count = CountResolvable(all, element =>
                string.Equals(element.AutomationId, target.AutomationId, StringComparison.Ordinal));
            if (count == 1)
            {
                Add(candidates, CreateCandidate(
                    fingerprint, scope, priority: 1, origin: "app-automation-id",
                    selector: new MauiSelectorCandidateSelector
                    {
                        Kind = "automation-id",
                        AutomationId = target.AutomationId,
                    },
                    rationale: ["unique-app-owned-automation-id"],
                    risks: [],
                    matchCount: count,
                    options));
            }
            else
            {
                Omit(omissions, "automation-id", $"AutomationId is not unique in the live scope ({count} matches).", count);
            }
        }
        else if (!string.IsNullOrWhiteSpace(target.AutomationId))
        {
            Omit(omissions, "automation-id", "The observed AutomationId is not app-owned or is reserved.", null);
        }

        // 2. A stable key only when it is scoped to one collection and one realized row.
        var collection = fingerprint.Collection;
        if (!string.IsNullOrWhiteSpace(collection?.ItemKey))
        {
            if (string.IsNullOrWhiteSpace(fingerprint.Managed.AutomationId))
            {
                Omit(omissions, "stable-item-key", "A repeated child also needs an AutomationId.", null);
            }
            else if (string.IsNullOrWhiteSpace(collection.Scope))
            {
                Omit(omissions, "stable-item-key", "An unscoped collection item key is not durable.", null);
            }
            else
            {
                var count = CountResolvable(all, element =>
                    string.Equals(element.AutomationId, fingerprint.Managed.AutomationId, StringComparison.Ordinal) &&
                    string.Equals(element.StableItemKey, collection.ItemKey, StringComparison.Ordinal) &&
                    string.Equals(element.CollectionScope, collection.Scope, StringComparison.Ordinal));
                if (count == 1)
                {
                    Add(candidates, CreateCandidate(
                        fingerprint, scope, priority: 2, origin: "stable-item-key",
                        selector: new MauiSelectorCandidateSelector
                        {
                            Kind = "stable-item-key",
                            AutomationId = fingerprint.Managed.AutomationId,
                            StableItemKey = collection.ItemKey,
                            AncestorAutomationId = collection.Scope,
                        },
                        rationale: ["stable-item-key-scoped-to-unique-collection"],
                        risks: collection.Virtualized == true ? ["virtualization"] : [],
                        matchCount: count,
                        options));
                }
                else
                {
                    Omit(omissions, "stable-item-key", $"The scoped item key is not unique in the live scope ({count} matches).", count);
                }
            }
        }
        else if (collection?.Virtualized == true)
        {
            Omit(omissions, "virtualized-row", "An unscoped virtualized row cannot produce a durable candidate.", null);
        }

        // 3. Native identity only when the platform identified it as authoritative.
        if (fingerprint.Native is { Authoritative: true, Identity: { Length: > 0 } nativeIdentity })
        {
            var count = CountResolvable(all, element =>
                string.Equals(element.NativeAutomationIdentity, nativeIdentity, StringComparison.Ordinal));
            if (count == 1)
            {
                Add(candidates, CreateCandidate(
                    fingerprint, scope, priority: 3, origin: "native-automation-identity",
                    selector: new MauiSelectorCandidateSelector
                    {
                        Kind = "native-automation-id",
                        NativeAutomationIdentity = nativeIdentity,
                    },
                    rationale: ["unique-authoritative-native-automation-identity"],
                    risks: [],
                    matchCount: count,
                    options));
            }
            else
            {
                Omit(omissions, "native-automation-id", $"The native automation identity is not unique ({count} matches).", count);
            }
        }

        // 4. Role/type only with a stable app-owned ancestor and a unique scope match.
        var stableAncestor = fingerprint.Topology.StableAncestorAutomationId;
        if (!string.IsNullOrWhiteSpace(stableAncestor) &&
            (!string.IsNullOrWhiteSpace(target.Role) || !string.IsNullOrWhiteSpace(target.Type)))
        {
            var count = CountResolvable(all, element =>
                string.Equals(element.Role, target.Role, StringComparison.Ordinal) &&
                string.Equals(element.Type, target.Type, StringComparison.Ordinal) &&
                string.Equals(FindStableAncestor(element, all), stableAncestor, StringComparison.Ordinal));
            if (count == 1)
            {
                Add(candidates, CreateCandidate(
                    fingerprint, scope, priority: 4, origin: "role-type-ancestor",
                    selector: new MauiSelectorCandidateSelector
                    {
                        Kind = "role-type-ancestor",
                        Role = target.Role,
                        Type = target.Type,
                        AncestorAutomationId = stableAncestor,
                    },
                    rationale: ["role-type-with-unique-stable-ancestor"],
                    risks: [],
                    matchCount: count,
                    options));
            }
            else
            {
                Omit(omissions, "role-type-ancestor", $"Role/type is not unique under the stable ancestor ({count} matches).", count);
            }
        }

        // 5. Source anchor only with current source and topology corroboration.
        if (fingerprint.Source is { State: "current", File: { Length: > 0 }, Line: > 0 } source &&
            !string.IsNullOrWhiteSpace(fingerprint.Topology.AncestorHash))
        {
            var count = CountResolvable(all, element =>
                string.Equals(element.SourceFile, source.File, StringComparison.Ordinal) &&
                element.SourceLine == source.Line);
            if (count == 1)
            {
                Add(candidates, CreateCandidate(
                    fingerprint, scope, priority: 5, origin: "source-topology",
                    selector: new MauiSelectorCandidateSelector
                    {
                        Kind = "source-topology",
                        SourceAnchor = $"{source.File}:{source.Line}",
                    },
                    rationale: ["current-source-anchor", "topology-corroborated"],
                    risks: [],
                    matchCount: count,
                    options));
            }
            else
            {
                Omit(omissions, "source-topology", $"The source anchor is not unique in the live scope ({count} matches).", count);
            }
        }
        else if (fingerprint.Source?.State is not "current")
        {
            Omit(omissions, "source-topology", "A current unambiguous source anchor is required.", null);
        }

        // 6. Exact text is opt-in and only valid with an explicit locale contract. The supplied
        // option is intentional: no text is read from a fingerprint or inferred from an element.
        if (!string.IsNullOrWhiteSpace(options.ExactText))
        {
            if (string.IsNullOrWhiteSpace(options.LocaleAssumption))
            {
                Omit(omissions, "exact-text", "Exact-text candidates require an explicit locale assumption.", null);
            }
            else if (options.ExactTextMatchCount != 1)
            {
                Omit(
                    omissions,
                    "exact-text",
                    "Exact-text candidates require an explicit unique live match count.",
                    options.ExactTextMatchCount);
            }
            else
            {
                Add(candidates, CreateCandidate(
                    fingerprint, scope, priority: 6, origin: "exact-text",
                    selector: new MauiSelectorCandidateSelector
                    {
                        Kind = "exact-text",
                        ExactText = options.ExactText,
                    },
                    rationale: ["exact-text-with-explicit-locale-assumption"],
                    risks: ["localization"],
                    matchCount: options.ExactTextMatchCount,
                    options));
            }
        }

        if (candidates.Count == 0)
            Omit(omissions, "candidate", "No safe, unique durable selector candidate was generated.", null);

        var ordered = candidates
            .OrderBy(static candidate => candidate.Priority ?? int.MaxValue)
            .ThenByDescending(static candidate => candidate.Scores.DeterministicRankScore)
            .ThenBy(static candidate => candidate.CandidateId, StringComparer.Ordinal)
            .Take(max)
            .ToList();
        if (candidates.Count > ordered.Count)
            Omit(omissions, "candidates", "The candidate shortlist cap was reached.", candidates.Count - ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
            ordered[index].Rank = index + 1;

        return new MauiSelectorCandidateGenerationResult
        {
            Fingerprint = fingerprint,
            Candidates = ordered,
            Omissions = omissions,
        };
    }

    private static void Add(List<MauiSelectorCandidate> candidates, MauiSelectorCandidate candidate)
    {
        if (!candidates.Any(existing => string.Equals(existing.CandidateId, candidate.CandidateId, StringComparison.Ordinal)))
            candidates.Add(candidate);
    }

    private static MauiSelectorCandidate CreateCandidate(
        MauiElementFingerprint fingerprint,
        MauiSelectorCandidateScope scope,
        int priority,
        string origin,
        MauiSelectorCandidateSelector selector,
        IReadOnlyList<string> rationale,
        IReadOnlyList<string> risks,
        int? matchCount,
        MauiSelectorCandidateGenerationOptions options)
    {
        var riskFlags = risks
            .Concat(CommonRisks(fingerprint, options))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToList();
        var scores = BuildScores(fingerprint, selector, scope, riskFlags);
        var candidateId = "sc1_" + MauiElementFingerprintBuilder.Hash(string.Join(
            "\u001f",
            origin,
            selector.Kind,
            selector.AutomationId,
            selector.StableItemKey,
            selector.NativeAutomationIdentity,
            selector.Role,
            selector.Type,
            selector.AncestorAutomationId,
            selector.SourceAnchor,
            selector.ExactText,
            scope.Route,
            scope.Window,
            scope.Modal,
            scope.CollectionScope,
            scope.LocaleAssumption))[..24];
        var candidate = new MauiSelectorCandidate
        {
            CandidateId = candidateId,
            Priority = priority,
            Selector = ToFlowSelector(selector),
            SelectorDescriptor = selector,
            Scope = new MauiFlowCheckpoint
            {
                Route = scope.Route,
                Window = scope.Window,
                Modal = scope.Modal,
                CollectionItemKey = scope.CollectionScope,
                Locale = scope.LocaleAssumption,
            },
            ScopeDescriptor = scope,
            Origin = origin,
            OriginCodes = [origin],
            RationaleCodes = rationale.OrderBy(static value => value, StringComparer.Ordinal).ToList(),
            RiskFlags = riskFlags,
            Score = scores.DeterministicRankScore,
            ScoreComponents = ToScoreComponents(scores),
            Unique = matchCount is null ? null : matchCount == 1,
            PlatformValidated = options.PlatformDivergent ? false : null,
            CalibrationStatus = MauiSelectorHealthRules.Uncalibrated,
            Fingerprint = fingerprint,
            Validation = new MauiSelectorCandidateValidation
            {
                Unique = matchCount is null ? null : matchCount == 1,
                MatchCount = matchCount,
                PlatformState = options.PlatformDivergent ? "divergent" : "not-validated",
                SourceState = fingerprint.Source?.State,
                Accepted = matchCount is null || matchCount == 1,
            },
            Calibration = new MauiSelectorCandidateCalibration(),
            Scores = scores,
            EvidenceRefs = fingerprint.EvidenceRefs.ToList(),
        };
        candidate.EvidenceRefs.Add($"candidate:{candidate.CandidateId}");
        return candidate;
    }

    private static MauiSelectorCandidateScores BuildScores(
        MauiElementFingerprint fingerprint,
        MauiSelectorCandidateSelector selector,
        MauiSelectorCandidateScope scope,
        IReadOnlyCollection<string> risks)
    {
        var native = fingerprint.Native;
        var agreement = native?.Identity is null || fingerprint.Managed.AutomationId is null
            ? 0.5
            : string.Equals(native.Identity, fingerprint.Managed.AutomationId, StringComparison.Ordinal)
                ? 1d
                : 0d;
        var scores = new MauiSelectorCandidateScores
        {
            AppOwnedIdentifier = selector.Kind == "automation-id" ? 1d : 0d,
            ScopeMatch = !string.IsNullOrWhiteSpace(scope.Route) ||
                         !string.IsNullOrWhiteSpace(scope.Window)
                ? 1d
                : 0.5d,
            ManagedNativeAgreement = agreement,
            SourceAnchorMatch = fingerprint.Source?.State == "current" ? 1d : 0d,
            TopologySimilarity = !string.IsNullOrWhiteSpace(fingerprint.Topology.AncestorHash) ? 1d : 0d,
            NormalizedGeometryCorroboration = fingerprint.NormalizedBounds is null ? 0d : 1d,
            LocalizationPenalty = risks.Contains("localization", StringComparer.Ordinal) ? 0.15d : 0d,
            VirtualizationPenalty = risks.Contains("virtualization", StringComparer.Ordinal) ? 0.20d : 0d,
            StaleSourcePenalty = risks.Contains("stale-source", StringComparer.Ordinal) ? 0.20d : 0d,
            PlatformDivergencePenalty = risks.Contains("platform-divergence", StringComparer.Ordinal) ? 0.20d : 0d,
            AmbiguityPenalty = risks.Contains("ambiguity", StringComparer.Ordinal) ? 0.40d : 0d,
        };
        scores.DeterministicRankScore = Math.Round(
            Math.Max(
                0d,
                (0.45d * scores.AppOwnedIdentifier) +
                (0.20d * scores.ScopeMatch) +
                (0.12d * scores.ManagedNativeAgreement) +
                (0.10d * scores.SourceAnchorMatch) +
                (0.08d * scores.TopologySimilarity) +
                (0.05d * scores.NormalizedGeometryCorroboration) -
                scores.LocalizationPenalty -
                scores.VirtualizationPenalty -
                scores.StaleSourcePenalty -
                scores.PlatformDivergencePenalty -
                scores.AmbiguityPenalty),
            4,
            MidpointRounding.AwayFromZero);
        return scores;
    }

    private static FlowSelector? ToFlowSelector(MauiSelectorCandidateSelector selector) => selector.Kind switch
    {
        "automation-id" => new FlowSelector { AutomationId = selector.AutomationId },
        "stable-item-key" when !string.IsNullOrWhiteSpace(selector.AutomationId) &&
                                    !string.IsNullOrWhiteSpace(selector.StableItemKey) &&
                                    !string.IsNullOrWhiteSpace(selector.AncestorAutomationId)
            => new FlowSelector
            {
                AutomationId = selector.AutomationId,
                StableItemKey = selector.StableItemKey,
                CollectionScope = selector.AncestorAutomationId,
                MatchCount = 1,
                Quality = "stable-item-key",
            },
        "exact-text" => new FlowSelector { Text = selector.ExactText },
        _ => null,
    };

    private static Dictionary<string, double> ToScoreComponents(MauiSelectorCandidateScores scores) => new(StringComparer.Ordinal)
    {
        ["appOwnedIdentifier"] = scores.AppOwnedIdentifier,
        ["scopeMatch"] = scores.ScopeMatch,
        ["managedNativeAgreement"] = scores.ManagedNativeAgreement,
        ["sourceAnchorMatch"] = scores.SourceAnchorMatch,
        ["topologySimilarity"] = scores.TopologySimilarity,
        ["normalizedGeometryCorroboration"] = scores.NormalizedGeometryCorroboration,
        ["localizationPenalty"] = scores.LocalizationPenalty,
        ["virtualizationPenalty"] = scores.VirtualizationPenalty,
        ["staleSourcePenalty"] = scores.StaleSourcePenalty,
        ["platformDivergencePenalty"] = scores.PlatformDivergencePenalty,
        ["ambiguityPenalty"] = scores.AmbiguityPenalty,
        ["deterministicRankScore"] = scores.DeterministicRankScore,
    };

    private static IEnumerable<string> CommonRisks(
        MauiElementFingerprint fingerprint,
        MauiSelectorCandidateGenerationOptions options)
    {
        if (fingerprint.Collection?.Virtualized == true)
            yield return "virtualization";
        if (fingerprint.Source?.State == "stale")
            yield return "stale-source";
        if (options.PlatformDivergent)
            yield return "platform-divergence";
    }

    private static MauiSelectorCandidateScope ScopeFrom(
        MauiElementFingerprint fingerprint,
        MauiSelectorCandidateGenerationOptions options) => new()
    {
        Route = fingerprint.Context.Route,
        Window = fingerprint.Context.Window,
        Modal = fingerprint.Context.Modal,
        CollectionScope = fingerprint.Collection?.Scope,
        LocaleAssumption = options.LocaleAssumption,
    };

    private static int CountResolvable(
        IEnumerable<MauiSelectorObservationElement> elements,
        Func<MauiSelectorObservationElement, bool> predicate)
        => elements.Count(predicate);

    private static string? FindStableAncestor(
        MauiSelectorObservationElement element,
        IReadOnlyList<MauiSelectorObservationElement> all)
    {
        var byId = all
            .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(static item => item.Id!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var current = element;
        for (var depth = 0; depth < 32 && !string.IsNullOrWhiteSpace(current.ParentId); depth++)
        {
            if (!byId.TryGetValue(current.ParentId, out var parent))
                break;
            if (MauiElementFingerprintBuilder.IsAppOwnedAutomationId(parent.AutomationId))
                return parent.AutomationId;
            current = parent;
        }
        return null;
    }

    private static void Omit(
        List<MauiSelectorEvidenceOmission> omissions,
        string kind,
        string reason,
        int? count)
    {
        if (omissions.Any(item => string.Equals(item.Kind, kind, StringComparison.Ordinal) &&
                                 string.Equals(item.Reason, reason, StringComparison.Ordinal)))
        {
            return;
        }
        omissions.Add(new MauiSelectorEvidenceOmission { Kind = kind, Reason = reason, Count = count });
    }
}

/// <summary>
/// Pure selector/testability diagnostics. It is deterministic, performs no I/O, and neither
/// mutates a flow nor calls a driver.
/// </summary>
public static class MauiSelectorHealthAnalyzer
{
    public static MauiSelectorHealthAnalysis Analyze(MauiSelectorHealthAnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var analysis = new MauiSelectorHealthAnalysis();
        var flow = input.Flow;
        if (flow is null)
            return analysis;

        AddDuplicateAutomationIdFindings(analysis, input);
        AnalyzeSteps(analysis, input, flow);
        AnalyzePlanCoverage(analysis, input, flow);
        AnalyzePlatforms(analysis, input, flow);
        AddCoverage(analysis, input, flow);
        analysis.Findings = analysis.Findings
            .OrderBy(static finding => SeverityOrder(finding.Severity))
            .ThenBy(static finding => finding.DiagnosticId, StringComparer.Ordinal)
            .ThenBy(static finding => finding.StepId, StringComparer.Ordinal)
            .ThenBy(static finding => finding.FindingId, StringComparer.Ordinal)
            .ToList();
        return analysis;
    }

    private static void AddDuplicateAutomationIdFindings(
        MauiSelectorHealthAnalysis analysis,
        MauiSelectorHealthAnalysisInput input)
    {
        foreach (var group in input.LiveElements
                     .Where(element => IsReachableActionable(element) &&
                                       !string.IsNullOrWhiteSpace(element.AutomationId))
                     .GroupBy(static element => element.AutomationId!, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            AddFinding(
                analysis,
                MauiSelectorHealthDiagnosticIds.DuplicateAutomationId,
                "error",
                "selector",
                stepId: null,
                source: null,
                platforms: PlatformList(input.Context?.Platform),
                message: $"A reachable actionable AutomationId is duplicated across {group.Count()} controls.",
                rationale: ["duplicate-automation-id", "simultaneously-reachable-actionable-controls"],
                evidence: [$"automation-id:{Token(group.Key)}"]);
        }
    }

    private static void AnalyzeSteps(
        MauiSelectorHealthAnalysis analysis,
        MauiSelectorHealthAnalysisInput input,
        MauiFlow flow)
    {
        foreach (var step in flow.Steps.OrderBy(static step => step.Seq))
        {
            var stepId = MauiFlowStepIdentity.Get(step);
            var selector = FlowValidator.EffectiveSelector(step);
            var evidence = step.SelectorEvidence;
            var fingerprint = evidence?.Fingerprint;
            var platform = fingerprint?.Context.Platform ?? input.Context?.Platform ?? flow.Platform;
            var source = fingerprint?.Source?.File;

            if (RequiresTarget(step.Action) && !HasDurableId(selector, fingerprint))
            {
                AddFinding(
                    analysis,
                    MauiSelectorHealthDiagnosticIds.MissingDurableId,
                    "warning",
                    "selector",
                    stepId,
                    source,
                    PlatformList(platform),
                    "The recorded actionable target has no durable AutomationId, stable item key, or authoritative native identity.",
                    ["missing-durable-id"],
                    Evidence(fingerprint, stepId));
            }

            if (selector?.Id is { Length: > 0 } && string.IsNullOrWhiteSpace(selector.AutomationId) &&
                string.IsNullOrWhiteSpace(selector.Text) && selector.TypeIndex is null)
            {
                AddFinding(
                    analysis,
                    MauiSelectorHealthDiagnosticIds.RuntimeIdOrTypeIndex,
                    "warning",
                    "selector",
                    stepId,
                    source,
                    PlatformList(platform),
                    "The active selector uses a raw runtime id and cannot be durable across tree rebuilds.",
                    ["runtime-id-selector"],
                    Evidence(fingerprint, stepId));
            }
            if (selector?.TypeIndex is not null ||
                string.Equals(selector?.SelectorKind, "typeIndex", StringComparison.Ordinal))
            {
                AddFinding(
                    analysis,
                    MauiSelectorHealthDiagnosticIds.RuntimeIdOrTypeIndex,
                    "warning",
                    "selector",
                    stepId,
                    source,
                    PlatformList(platform),
                    "The active selector uses type/index ordering and can drift when hierarchy or collection realization changes.",
                    ["type-index-selector"],
                    Evidence(fingerprint, stepId));
            }
            if (!string.IsNullOrWhiteSpace(selector?.Text))
            {
                AddFinding(
                    analysis,
                    MauiSelectorHealthDiagnosticIds.LocalizedOrDynamicText,
                    "warning",
                    "selector",
                    stepId,
                    source,
                    PlatformList(platform),
                    "The active selector uses exact text and requires an explicit locale assumption; bound or localized text can change.",
                    ["exact-text-selector", "locale-assumption-required"],
                    Evidence(fingerprint, stepId));
            }

            if (fingerprint?.Collection?.Virtualized == true ||
                fingerprint?.Collection?.TemplateKind is not null ||
                selector?.TypeIndex is not null)
            {
                AddFinding(
                    analysis,
                    MauiSelectorHealthDiagnosticIds.TemplateOrVirtualization,
                    "warning",
                    "selector",
                    stepId,
                    source,
                    PlatformList(platform),
                    "The target is in a template or virtualized collection scope; index and unscoped-row selectors are unsafe.",
                    ["template-or-virtualization-risk"],
                    Evidence(fingerprint, stepId));
            }

            var sourceState = fingerprint?.Source?.State ?? "missing";
            if (sourceState is "stale" or "ambiguous" or "missing")
            {
                AddFinding(
                    analysis,
                    MauiSelectorHealthDiagnosticIds.SourceAnchor,
                    sourceState == "stale" ? "warning" : "info",
                    "source",
                    stepId,
                    source,
                    PlatformList(platform),
                    $"The recorded source anchor is {sourceState}; selector evidence cannot treat it as a current unique source proof.",
                    [$"source-anchor-{sourceState}"],
                    Evidence(fingerprint, stepId));
            }

            if (fingerprint?.Native is { Identity: { Length: > 0 } nativeValue } &&
                fingerprint.Managed.AutomationId is { Length: > 0 } managedId &&
                !string.Equals(nativeValue, managedId, StringComparison.Ordinal))
            {
                AddFinding(
                    analysis,
                    MauiSelectorHealthDiagnosticIds.ManagedNativeDivergence,
                    "warning",
                    "platform",
                    stepId,
                    source,
                    PlatformList(platform),
                    "Managed and authoritative native automation identities diverge for the recorded target.",
                    ["managed-native-identity-divergence"],
                    Evidence(fingerprint, stepId));
            }

            if (RequiresPostcondition(step.Action) && !HasHardAssertion(step))
            {
                AddFinding(
                    analysis,
                    MauiSelectorHealthDiagnosticIds.MissingHardPostcondition,
                    "warning",
                    "assertion",
                    stepId,
                    source,
                    PlatformList(platform),
                    "The action has no meaningful hard postcondition assertion.",
                    ["hard-postcondition-required"],
                    Evidence(fingerprint, stepId));
            }
        }
    }

    private static void AnalyzePlanCoverage(
        MauiSelectorHealthAnalysis analysis,
        MauiSelectorHealthAnalysisInput input,
        MauiFlow flow)
    {
        var criteria = input.Plan?.AcceptanceCriteria
            .Where(static criterion => criterion.Required && !string.IsNullOrWhiteSpace(criterion.CriterionId))
            .OrderBy(static criterion => criterion.CriterionId, StringComparer.Ordinal)
            ?? Enumerable.Empty<MauiAcceptanceCriterion>();
        foreach (var criterion in criteria)
        {
            var covered = flow.Steps.Any(step =>
                (step.AcceptanceCriterionIds?.Contains(criterion.CriterionId!, StringComparer.Ordinal) ?? false) &&
                HasHardAssertion(step));
            if (!covered)
            {
                AddFinding(
                    analysis,
                    MauiSelectorHealthDiagnosticIds.AcceptanceCriterionUncovered,
                    "warning",
                    "plan",
                    stepId: null,
                    source: null,
                    platforms: input.Plan?.RequiredPlatforms.OrderBy(static platform => platform, StringComparer.Ordinal).ToList() ?? [],
                    message: "A required plan acceptance criterion is not linked to a hard assertion.",
                    rationale: ["acceptance-criterion-not-covered-by-hard-assertion"],
                    evidence: [$"criterion:{Token(criterion.CriterionId!)}"]);
            }
        }
    }

    private static void AnalyzePlatforms(
        MauiSelectorHealthAnalysis analysis,
        MauiSelectorHealthAnalysisInput input,
        MauiFlow flow)
    {
        var snapshots = input.PlatformSnapshots.ToList();
        snapshots.AddRange(input.RunHistory
            .Where(static report => !string.IsNullOrWhiteSpace(report.Target?.Platform))
            .GroupBy(static report => report.Target!.Platform!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MauiSelectorHealthPlatformSnapshot
            {
                Platform = group.Key,
                Fingerprints = group.SelectMany(static report => report.Steps)
                    .Select(static step => step.Fingerprint)
                    .Where(static fingerprint => fingerprint is not null)
                    .Cast<MauiElementFingerprint>()
                    .ToList(),
                Candidates = group.SelectMany(static report => report.Steps)
                    .SelectMany(static step => step.SelectorCandidates)
                    .ToList(),
            }));
        if (!string.IsNullOrWhiteSpace(input.Context?.Platform))
        {
            snapshots.Add(new MauiSelectorHealthPlatformSnapshot
            {
                Platform = input.Context.Platform,
                Fingerprints = flow.Steps
                    .Select(static step => step.SelectorEvidence?.Fingerprint)
                    .Where(static fingerprint => fingerprint is not null)
                    .Cast<MauiElementFingerprint>()
                    .ToList(),
                Candidates = flow.Steps
                    .SelectMany(static step => step.SelectorEvidence?.Candidates ?? [])
                    .ToList(),
            });
        }

        var required = input.Plan?.RequiredPlatforms
            .Where(static platform => !string.IsNullOrWhiteSpace(platform))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static platform => platform, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        foreach (var platform in required)
        {
            var matches = snapshots
                .Where(snapshot => string.Equals(snapshot.Platform, platform, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0)
            {
                AddFinding(
                    analysis,
                    MauiSelectorHealthDiagnosticIds.RequiredPlatform,
                    "warning",
                    "platform",
                    stepId: null,
                    source: null,
                    platforms: [platform],
                    message: "A required platform has no selector candidate or fingerprint observation.",
                    rationale: ["required-platform-candidate-missing"],
                    evidence: ["platform:" + Token(platform)]);
            }
        }

        var byPlatform = snapshots
            .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.Platform))
            .GroupBy(static snapshot => snapshot.Platform!, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static snapshot => snapshot.Platform, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (byPlatform.Count < 2)
            return;

        var baseline = CandidateKinds(byPlatform[0]);
        foreach (var snapshot in byPlatform.Skip(1))
        {
            if (!baseline.SetEquals(CandidateKinds(snapshot)))
            {
                AddFinding(
                    analysis,
                    MauiSelectorHealthDiagnosticIds.RequiredPlatform,
                    "warning",
                    "platform",
                    stepId: null,
                    source: null,
                    platforms: [byPlatform[0].Platform!, snapshot.Platform!],
                    message: "Selector candidate kinds diverge across observed platforms.",
                    rationale: ["required-platform-candidate-divergence"],
                    evidence: ["platform:" + Token(byPlatform[0].Platform!), "platform:" + Token(snapshot.Platform!)]);
            }
        }
    }

    private static void AddCoverage(
        MauiSelectorHealthAnalysis analysis,
        MauiSelectorHealthAnalysisInput input,
        MauiFlow flow)
    {
        var groups = flow.Steps
            .Where(static step => RequiresTarget(step.Action))
            .GroupBy(step => new
            {
                Platform = step.SelectorEvidence?.Fingerprint?.Context.Platform ??
                           input.Context?.Platform ??
                           flow.Platform ??
                           "unknown",
                Route = step.Page ??
                        step.SelectorEvidence?.Fingerprint?.Context.Route ??
                        input.Context?.Route ??
                        "unknown",
            })
            .OrderBy(static group => group.Key.Platform, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.Route, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var summary = new MauiSelectorCoverageSummary
            {
                Platform = group.Key.Platform,
                Route = group.Key.Route,
                TotalTargets = group.Count(),
                DurableTargets = group.Count(step => HasDurableId(
                    FlowValidator.EffectiveSelector(step),
                    step.SelectorEvidence?.Fingerprint)),
                FragileTargets = group.Count(step =>
                {
                    var selector = FlowValidator.EffectiveSelector(step);
                    return selector is not null && !HasDurableId(selector, step.SelectorEvidence?.Fingerprint);
                }),
                MissingTargets = group.Count(step => FlowValidator.EffectiveSelector(step) is null),
            };
            analysis.Coverage.Add(summary);
            AddFinding(
                analysis,
                MauiSelectorHealthDiagnosticIds.CoverageSummary,
                "info",
                "coverage",
                stepId: null,
                source: null,
                platforms: PlatformList(summary.Platform),
                message: $"Selector coverage summary: {summary.DurableTargets}/{summary.TotalTargets} durable targets on the observed route.",
                rationale: ["selector-coverage-summary"],
                evidence:
                [
                    "coverage:" + Token($"{summary.Platform}|{summary.Route}"),
                ]);
        }
    }

    private static bool HasDurableId(FlowSelector? selector, MauiElementFingerprint? fingerprint)
        => !string.IsNullOrWhiteSpace(selector?.AutomationId) ||
           !string.IsNullOrWhiteSpace(fingerprint?.Managed.AutomationId) ||
           !string.IsNullOrWhiteSpace(fingerprint?.Collection?.ItemKey) ||
           fingerprint?.Native?.Authoritative == true;

    private static bool HasHardAssertion(FlowStep step)
        => (step.Asserts ?? []).Any(assertion =>
            assertion.Verify &&
            assertion.Kind is "propEquals" or "exists" or "notExists" or "routeIs");

    private static bool RequiresTarget(string? action)
        => action is FlowActions.Tap or FlowActions.Fill or FlowActions.SetProperty;

    private static bool RequiresPostcondition(string? action)
        => action is FlowActions.Tap or FlowActions.Fill or FlowActions.SetProperty or
           FlowActions.Navigate or FlowActions.Back or FlowActions.SetTheme;

    private static bool IsReachableActionable(MauiSelectorObservationElement element)
    {
        if (!element.IsVisible || !element.IsEnabled)
            return false;
        if (element.Traits?.Contains("interactive", StringComparer.OrdinalIgnoreCase) == true)
            return true;
        return element.Role is "button" or "textbox" or "checkbox" or "radio" or "switch" or "link" ||
               element.Type is "Button" or "ImageButton" or "Entry" or "Editor" or "SearchBar" or
                   "CheckBox" or "RadioButton" or "Switch";
    }

    private static HashSet<string> CandidateKinds(MauiSelectorHealthPlatformSnapshot snapshot)
        => snapshot.Candidates
            .Where(static candidate => candidate.Validation.Accepted)
            .Select(static candidate => candidate.SelectorDescriptor.Kind ?? "unknown")
            .ToHashSet(StringComparer.Ordinal);

    private static void AddFinding(
        MauiSelectorHealthAnalysis analysis,
        string diagnosticId,
        string severity,
        string category,
        string? stepId,
        string? source,
        List<string> platforms,
        string message,
        IReadOnlyList<string> rationale,
        IReadOnlyList<string> evidence)
    {
        var sortedPlatforms = platforms
            .Where(static platform => !string.IsNullOrWhiteSpace(platform))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static platform => platform, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var findingId = diagnosticId + ":" + MauiElementFingerprintBuilder.Hash(string.Join(
            "\u001f",
            diagnosticId,
            stepId,
            source,
            string.Join(",", sortedPlatforms),
            string.Join(",", rationale.OrderBy(static value => value, StringComparer.Ordinal)),
            string.Join(",", evidence.OrderBy(static value => value, StringComparer.Ordinal))))[..16];
        if (analysis.Findings.Any(finding => string.Equals(finding.FindingId, findingId, StringComparison.Ordinal)))
            return;
        analysis.Findings.Add(new MauiSelectorHealthFinding
        {
            DiagnosticId = diagnosticId,
            FindingId = findingId,
            Severity = severity,
            Category = category,
            StepId = stepId,
            Source = source,
            Platforms = sortedPlatforms,
            Message = message,
            RationaleCodes = rationale.OrderBy(static value => value, StringComparer.Ordinal).ToList(),
            EvidenceRefs = evidence.OrderBy(static value => value, StringComparer.Ordinal).ToList(),
        });
    }

    private static List<string> Evidence(MauiElementFingerprint? fingerprint, string stepId)
    {
        var evidence = new List<string> { "flow-step:" + stepId };
        if (!string.IsNullOrWhiteSpace(fingerprint?.FingerprintId))
            evidence.Add("fingerprint:" + fingerprint.FingerprintId);
        return evidence;
    }

    private static List<string> PlatformList(string? platform)
        => string.IsNullOrWhiteSpace(platform) ? [] : [platform];

    private static string Token(string value)
        => MauiElementFingerprintBuilder.Hash(value)[..16];

    private static int SeverityOrder(string severity) => severity switch
    {
        "error" => 0,
        "warning" => 1,
        _ => 2,
    };
}
