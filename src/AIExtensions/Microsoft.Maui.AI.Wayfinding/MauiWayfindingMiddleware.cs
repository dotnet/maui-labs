using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.Indexer;
using Microsoft.Maui.AI.Navigation;

namespace Microsoft.Maui.AI.Wayfinding;

/// <summary>Options for the MAUI Wayfinding AI middleware.</summary>
public sealed class MauiWayfindingOptions
{
    public const string VisionChatClientServiceKey = "MauiWayfindingVision";

    /// <summary>Maximum destinations returned by <c>search_app_ui</c>.</summary>
    public int MaximumSearchResults { get; set; } = 5;

    /// <summary>Runtime snapshot options used by <c>get_current_app_state</c>.</summary>
    public CurrentPageSnapshotOptions CurrentPage { get; } = new();

    /// <summary>
    /// Additional app-specific guidance appended to the built-in wayfinding policy.
    /// Persona and domain behavior should remain in the app's own system prompt.
    /// </summary>
    public string? AdditionalInstructions { get; set; }

    /// <summary>User-visible title used while a Shell flyout is presented.</summary>
    public string NavigationMenuTitle { get; set; } = "Navigation menu";

    /// <summary>Enable rendered-view capture and the <c>describe_current_visual</c> tool.</summary>
    public bool EnableVision { get; set; }

    /// <summary>Maximum semantic visual regions accepted by one vision tool call.</summary>
    public int MaximumVisualTargets { get; set; } = 4;

    /// <summary>Maximum total PNG payload accepted by one vision tool call.</summary>
    public long MaximumVisualPayloadBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>Instructions applied to rendered-view analysis.</summary>
    public string VisionInstructions { get; set; } =
        """
        Describe the image as a whole, then identify visible charts, titles,
        labels, relative values, largest and smallest items, and useful comparisons.
        Include exact numbers only when clearly legible. Do not infer data that is
        not visible, and say when text or values are uncertain.
        """;
}

/// <summary>User-facing current page and optional runtime UI state.</summary>
public sealed record CurrentAppState(
    string CurrentPage,
    string? PageUi);

/// <summary>A user-facing semantic destination search result.</summary>
public sealed record WayfindingSearchResult(
    string DestinationId,
    string Title,
    IReadOnlyList<string> Path,
    IReadOnlyList<string> RequiredInputs,
    IReadOnlyList<string> MatchingControls);

/// <summary>User-facing details for a semantic destination.</summary>
public sealed record WayfindingDestination(
    string DestinationId,
    string Title,
    IReadOnlyList<string> Path,
    IReadOnlyList<string> RequiredInputs,
    IReadOnlyList<WayfindingPageDescription> Pages);

/// <summary>Structured destination lookup result with recoverable failures.</summary>
public sealed record WayfindingDestinationResult(
    bool Found,
    string Message,
    WayfindingDestination? Destination,
    IReadOnlyList<string> ValidDestinationIds);

/// <summary>User-facing semantic UI for one page along a destination path.</summary>
public sealed record WayfindingPageDescription(
    string Title,
    string UiDescription);

/// <summary>User-facing outcome of a navigation request.</summary>
public sealed record WayfindingNavigationResult(
    bool Success,
    string Message,
    IReadOnlyList<string> MissingInputs);

/// <summary>AI-callable semantic MAUI wayfinding operations.</summary>
public sealed class MauiWayfindingTools(
    ApplicationMapService applicationMap,
    ICurrentPageContextProvider currentPage,
    MauiWayfindingOptions options)
{
    private static readonly HashSet<string> PublicControlTypes =
    [
        "ActivityIndicator",
        "Button",
        "CarouselView",
        "CheckBox",
        "CollectionView",
        "DatePicker",
        "Editor",
        "Entry",
        "GraphicsView",
        "Image",
        "ImageButton",
        "Label",
        "List",
        "ListView",
        "Picker",
        "ProgressBar",
        "RadioButton",
        "SearchBar",
        "Slider",
        "Stepper",
        "Switch",
        "TimePicker",
        "WebView",
    ];

    [ExportAIFunction("search_app_ui")]
    [Description(
        "Search navigable app destinations for a page, feature, control, or task. " +
        "This is read-only and never changes the current screen.")]
    public IReadOnlyList<WayfindingSearchResult> SearchAppUi(
        [Description("Natural-language feature, task, control, or page to find.")]
        string query)
    {
        var destinations = applicationMap.GetDestinations();
        var lookup = CreateDestinationLookup(destinations);
        return applicationMap.Search(
                query,
                destinations,
                options.MaximumSearchResults)
            .Select(match =>
            {
                var item = lookup.First(candidate =>
                    string.Equals(
                        candidate.Destination.DestinationId,
                        match.Destination.DestinationId,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        candidate.Destination.Route,
                        match.Destination.Route,
                        StringComparison.OrdinalIgnoreCase));
                return new WayfindingSearchResult(
                    item.SafeId,
                    GetTitle(item.Destination),
                    GetSafePath(item.Destination),
                    item.Destination.RequiredParameters
                        .Select(parameter => parameter.QueryName)
                        .ToArray(),
                    match.RelevantLines
                        .Select(SanitizeUiLine)
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .ToArray());
            })
            .ToArray();
    }

    [ExportAIFunction("get_app_destination")]
    [Description(
        "Return a destination's user-visible title, required inputs, page path, and indexed UI. " +
        "This is read-only and does not inspect or change current app state.")]
    public WayfindingDestinationResult GetAppDestination(
        [Description("Exact destinationId returned by search_app_ui.")]
        string destinationId)
    {
        var lookup = CreateDestinationLookup();
        var item = lookup.FirstOrDefault(candidate =>
            string.Equals(
                candidate.SafeId,
                destinationId,
                StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return new WayfindingDestinationResult(
                false,
                $"No destination matched '{destinationId}'.",
                null,
                lookup.Select(candidate => candidate.SafeId).ToArray());
        }

        var pathPages = item.Destination.PagePath
            .Select(pageName => applicationMap.GetIndexedPage(pageName))
            .Where(page => page is not null)
            .Select(page => new WayfindingPageDescription(
                GetTitle(page!),
                SanitizeUi(page!.Markdown)))
            .ToArray();
        if (pathPages.Length != item.Destination.PagePath.Count)
        {
            return new WayfindingDestinationResult(
                false,
                "The destination path could not be fully resolved.",
                null,
                lookup.Select(candidate => candidate.SafeId).ToArray());
        }

        return new WayfindingDestinationResult(
            true,
            $"Found {GetTitle(item.Destination)}.",
            new WayfindingDestination(
                item.SafeId,
                GetTitle(item.Destination),
                GetSafePath(item.Destination),
                item.Destination.RequiredParameters
                    .Select(parameter => parameter.QueryName)
                    .ToArray(),
                pathPages),
            lookup.Select(candidate => candidate.SafeId).ToArray());
    }

    [ExportAIFunction("get_current_app_state")]
    [Description(
        "Return the current user-visible screen title, optionally with its visible UI. " +
        "Use includePageUi=true for where/how/back/current-control questions. " +
        "Semantically described controls include AutomationIds for targeted automation and vision. " +
        "Internal routes and type names are not returned.")]
    public async Task<CurrentAppState> GetCurrentAppStateAsync(
        [Description(
            "Set true when exact current controls or from-here directions are needed; " +
            "false returns only the current screen title.")]
        bool includePageUi = false)
    {
        var snapshot = await currentPage.CaptureAsync(options.CurrentPage);
        return new CurrentAppState(
            GetCurrentTitle(snapshot),
            includePageUi && snapshot is not null
                ? SanitizeUi(snapshot.Markdown)
                : null);
    }

    [ExportAIFunction("navigate_to_app_destination")]
    [Description(
        "Navigate only when the user explicitly asks to open, show, go to, or be taken to a destination. " +
        "Never call this for where/how/walkthrough questions.")]
    public async Task<WayfindingNavigationResult> NavigateToAppDestinationAsync(
        [Description("Exact destinationId returned by search_app_ui.")]
        string destinationId,
        [Description(
            "Required destination inputs returned by get_app_destination, " +
            "for example { \"sku\": \"seed-basil\" }.")]
        Dictionary<string, string>? parameters = null)
    {
        var item = CreateDestinationLookup().FirstOrDefault(candidate =>
            string.Equals(
                candidate.SafeId,
                destinationId,
                StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return new WayfindingNavigationResult(
                false,
                $"No destination matched '{destinationId}'. Valid destination IDs: " +
                string.Join(", ", CreateDestinationLookup().Select(candidate => candidate.SafeId)),
                []);
        }

        var resolution = await applicationMap.NavigateAsync(
            item.Destination.DestinationId,
            parameters);
        return resolution.Status switch
        {
            DestinationResolutionStatus.Success => new(
                true,
                $"Opened {GetTitle(item.Destination)}.",
                []),
            DestinationResolutionStatus.MissingParameters => new(
                false,
                $"More information is needed before opening {GetTitle(item.Destination)}.",
                resolution.MissingParameters),
            DestinationResolutionStatus.Ambiguous => new(
                false,
                "The requested destination is ambiguous.",
                []),
            _ => new(
                false,
                "The requested destination was not found.",
                []),
        };
    }

    private IReadOnlyList<DestinationLookup> CreateDestinationLookup(
        IReadOnlyList<ApplicationDestination>? destinations = null)
    {
        var candidates = (destinations ?? applicationMap.GetDestinations())
            .Select(destination =>
            {
                var baseId = Slugify(GetTitle(destination));
                if (string.IsNullOrWhiteSpace(baseId))
                    baseId = Slugify(HumanizeTypeName(destination.PageName));
                if (string.IsNullOrWhiteSpace(baseId))
                    baseId = "destination";
                return (BaseId: baseId, Destination: destination);
            })
            .ToArray();
        var duplicateIds = candidates
            .GroupBy(candidate => candidate.BaseId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.Select(candidate => new DestinationLookup(
                duplicateIds.Contains(candidate.BaseId)
                    ? $"{candidate.BaseId}-{CreateStableSuffix(candidate.Destination)}"
                    : candidate.BaseId,
                candidate.Destination))
            .ToArray();
    }

    private IReadOnlyList<string> GetSafePath(
        ApplicationDestination destination)
        => destination.PagePath
            .Select(pageName =>
                applicationMap.GetIndexedPage(pageName))
            .Where(page => page is not null)
            .Select(page => GetTitle(page!))
            .ToArray();

    private string GetCurrentTitle(CurrentPageSnapshot? snapshot)
    {
        if (snapshot is null)
            return "Current screen";
        if (snapshot.PageName.EndsWith("Shell", StringComparison.OrdinalIgnoreCase))
            return options.NavigationMenuTitle;

        var indexedPage = applicationMap.GetIndexedPage(snapshot.PageName);
        return indexedPage is not null
            ? GetTitle(indexedPage)
            : snapshot.PageTitle ?? "Current screen";
    }

    private static string GetTitle(ApplicationDestination destination)
        => GetTitle(new IndexedPage(
            destination.PageName,
            destination.FilePath,
            destination.Markdown));

    private static string GetTitle(IndexedPage page)
    {
        foreach (var line in page.Markdown.Split('\n'))
        {
            if (!Regex.IsMatch(
                    line.TrimStart(),
                    "^- Heading \\(level [1-9]\\):")
                || !TryGetQuotedText(line, out var title)
                || title.Contains('{'))
            {
                continue;
            }

            return title;
        }

        return HumanizeTypeName(page.Name);
    }

    private static string SanitizeUi(string markdown)
        => string.Join(
            '\n',
            markdown.Split('\n')
                .Select(SanitizeUiLine)
                .Where(line => !string.IsNullOrWhiteSpace(line)));

    private static string SanitizeUiLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("File:", StringComparison.Ordinal)
            || trimmed.StartsWith("# ", StringComparison.Ordinal)
            || Regex.IsMatch(trimmed, "^- Current page: [A-Za-z_][A-Za-z0-9_.]*$")
            )
        {
            return "";
        }

        var sanitized = line;
        var typeMatch = Regex.Match(
            trimmed,
            "^- (?<type>\\[[A-Za-z_][A-Za-z0-9_.]*\\]|[A-Za-z_][A-Za-z0-9_.]*):");
        if (typeMatch.Success
            && !PublicControlTypes.Contains(
                typeMatch.Groups["type"].Value.Trim('[', ']')))
        {
            var isBracketedType = typeMatch.Groups["type"].Value.StartsWith(
                "[",
                StringComparison.Ordinal);
            var replacement = isBracketedType
                && !trimmed.Contains("[automationId:", StringComparison.Ordinal)
                    ? "Group"
                    : "Control";
            sanitized = Regex.Replace(
                sanitized,
                "^(\\s*)- (?:\\[[A-Za-z_][A-Za-z0-9_.]*\\]|[A-Za-z_][A-Za-z0-9_.]*):",
                $"$1- {replacement}:");
        }

        sanitized = Regex.Replace(
            sanitized,
            "\\{[^}]+\\}",
            "[dynamic text]");
        sanitized = Regex.Replace(
            sanitized,
            "\\s+→\\s+[A-Za-z_][A-Za-z0-9_.]*",
            "");
        sanitized = Regex.Replace(
            sanitized,
            "\\[(visible|hidden) when [^\\]]*\\]",
            "[shown in some states]",
            RegexOptions.IgnoreCase);
        sanitized = sanitized.Replace(
            "CollectionView:",
            "List:",
            StringComparison.Ordinal);
        return sanitized;
    }

    private static bool TryGetQuotedText(
        string line,
        out string text)
    {
        var start = line.IndexOf('"');
        var end = line.LastIndexOf('"');
        if (start < 0 || end <= start)
        {
            text = "";
            return false;
        }

        text = line[(start + 1)..end];
        return true;
    }

    private static string HumanizeTypeName(string value)
    {
        var trimmed = value.EndsWith("Page", StringComparison.Ordinal)
            ? value[..^4]
            : value.EndsWith("View", StringComparison.Ordinal)
                ? value[..^4]
                : value;
        return Regex.Replace(
            trimmed,
            "(?<=[a-z0-9])(?=[A-Z])",
            " ");
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder();
        var previousDash = false;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousDash = false;
            }
            else if (!previousDash && builder.Length > 0)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string CreateStableSuffix(
        ApplicationDestination destination)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{destination.DestinationId}|{destination.Route}"));
        return Convert.ToHexString(bytes.AsSpan(0, 3))
            .ToLowerInvariant();
    }

    private sealed record DestinationLookup(
        string SafeId,
        ApplicationDestination Destination);
}

/// <summary>DI registration for MAUI Wayfinding.</summary>
public static class MauiWayfindingServiceCollectionExtensions
{
    public static IServiceCollection AddMauiWayfinding(
        this IServiceCollection services,
        IndexedPageCatalog catalog,
        Action<MauiWayfindingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(catalog);

        var options = new MauiWayfindingOptions();
        configure?.Invoke(options);
        if (options.MaximumSearchResults <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                "MaximumSearchResults must be positive.");
        }
        if (options.MaximumVisualTargets <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                "MaximumVisualTargets must be positive.");
        }
        if (options.MaximumVisualPayloadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                "MaximumVisualPayloadBytes must be positive.");
        }

        services.AddSingleton(catalog);
        services.AddSingleton(options);
        services.TryAddSingleton<ICurrentPageContextProvider, RuntimePageContextProvider>();
        services.TryAddSingleton<ShellNavigationService>();
        services.TryAddSingleton<ApplicationMapService>();
        services.TryAddSingleton<MauiWayfindingTools>();
        if (options.EnableVision)
        {
            services.TryAddSingleton<ICurrentViewCaptureService, CurrentViewCaptureService>();
            services.TryAddSingleton<MauiVisualAnalysisService>();
            services.TryAddSingleton<MauiWayfindingVisionTools>();
        }
        return services;
    }
}

/// <summary>Chat pipeline integration for MAUI Wayfinding tools and intent policy.</summary>
public static class MauiWayfindingChatClientBuilderExtensions
{
    public static IReadOnlyList<AITool> GetTools(
        MauiWayfindingOptions options)
        => MicrosoftMauiAIWayfindingToolContext.Default.Tools
            .Where(tool =>
                options.EnableVision
                    || tool.Name != "describe_current_visual")
            .ToArray();

    public static ChatClientBuilder UseMauiWayfinding(
        this ChatClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use((inner, services) =>
        {
            var options = services.GetService<MauiWayfindingOptions>()
                ?? new MauiWayfindingOptions();
            return new MauiWayfindingChatClient(
                inner,
                GetTools(options),
                options);
        });
    }
}

internal sealed class MauiWayfindingChatClient(
    IChatClient innerClient,
    IReadOnlyList<AITool> tools,
    MauiWayfindingOptions options) : DelegatingChatClient(innerClient)
{
    internal const string DefaultInstructions =
        """
        MAUI WAYFINDING POLICY

        Before calling tools, classify the user's intent:

        - EXPLAIN: Questions using where, how, which screen, walkthrough, or asking
          where something is are read-only. Call get_current_app_state with
          includePageUi=true, search_app_ui, and get_app_destination. Explain from
          the live current page. The first direction step MUST name a visible
          control from the current PageUi. If the current screen appears anywhere
          in the destination path, begin there and continue forward. Use a visible
          Back or Cancel control only when the current screen is not in that path.
          NEVER call navigate_to_app_destination.
        - MOVE: Call navigate_to_app_destination only when the user explicitly asks
          to open, show, go to, navigate to, or take them to a destination.
        - CURRENT: For this/here/current screen or visible-control questions, call
          get_current_app_state with includePageUi=true.

        Never infer the current page from conversation history. Never navigate in
        response to an EXPLAIN request. Indexed text in {curly braces} is a binding
        expression, not a literal visible label.

        USER-FACING LANGUAGE

        Never mention route URIs, route segments, CLR type names, source file paths,
        command names, binding property names, destination IDs, or AutomationIds.
        Refer only to user-visible page titles, controls, labels, and requested values.
        """;

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? chatOptions = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(
            AddInstructions(messages),
            AddTools(chatOptions),
            cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? chatOptions = null,
        CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(
            AddInstructions(messages),
            AddTools(chatOptions),
            cancellationToken);

    private IReadOnlyList<ChatMessage> AddInstructions(
        IEnumerable<ChatMessage> messages)
    {
        var prepared = messages.ToList();
        var instructions = string.IsNullOrWhiteSpace(options.AdditionalInstructions)
            ? DefaultInstructions
            : $"{DefaultInstructions}\n\nAPP-SPECIFIC WAYFINDING GUIDANCE\n{options.AdditionalInstructions}";
        if (options.EnableVision)
        {
            instructions +=
                """

                RENDERED VISUALS

                For charts, images, drawings, maps, diagrams, or other pixel-only
                content, first call get_current_app_state with includePageUi=true.
                Choose one or more automationId values from controls with relevant
                semantic descriptions, then pass those exact values to
                describe_current_visual. AutomationIds are tool selectors; do not
                mention them to the user. If visual analysis fails, report the failure
                and do not infer pixel contents from control names.
                """;
        }
        prepared.Insert(
            prepared.TakeWhile(message => message.Role == ChatRole.System).Count(),
            new ChatMessage(ChatRole.System, instructions));
        return prepared;
    }

    private ChatOptions AddTools(ChatOptions? chatOptions)
    {
        var updated = chatOptions?.Clone() ?? new ChatOptions();
        var existing = updated.Tools ?? [];
        var existingNames = existing
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        updated.Tools =
        [
            .. existing,
            .. tools.Where(tool => existingNames.Add(tool.Name)),
        ];
        return updated;
    }
}
