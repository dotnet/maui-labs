using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services.Recipes;

public abstract class HttpRoasterRecipeAdapterBase(HttpClient httpClient) : IRoasterRecipeAdapter
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    protected HttpClient HttpClient { get; } = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public abstract string Id { get; }
    public abstract string RoasterName { get; }

    public virtual bool CanHandle(Bean bean)
    {
        if (string.IsNullOrWhiteSpace(bean.Roaster))
            return false;

        var candidate = bean.Roaster.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        var expected = RoasterName.Replace(" ", string.Empty, StringComparison.Ordinal);
        return candidate.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<ScrapedRecipe>> FetchAsync(
        Bean bean,
        CancellationToken cancellationToken) =>
        await FetchCoreAsync(bean, cancellationToken);

    protected abstract Task<IReadOnlyList<ScrapedRecipe>> FetchCoreAsync(
        Bean bean,
        CancellationToken cancellationToken);

    protected async Task<string?> TryGetHtmlAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "BaristaNotesApp/1.0 (+https://github.com/davidortinau/BaristaNotes)");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

        using var response = await HttpClient.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(timeout.Token);
    }
}

public sealed class RoasterRecipeAdapterRegistry : IRoasterRecipeAdapterRegistry
{
    private readonly IReadOnlyList<IRoasterRecipeAdapter> _adapters;

    public RoasterRecipeAdapterRegistry(IEnumerable<IRoasterRecipeAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters.ToList();
    }

    public IReadOnlyList<IRoasterRecipeAdapter> All => _adapters;

    public IRoasterRecipeAdapter? FindAdapter(Bean bean) =>
        _adapters.FirstOrDefault(adapter => adapter.CanHandle(bean));

    public static RoasterRecipeAdapterRegistry CreateDefault(HttpClient httpClient) => new(
    [
        new OnyxCoffeeLabAdapter(httpClient),
        new CounterCultureAdapter(httpClient),
        new BlueBottleAdapter(httpClient),
        new IntelligentsiaAdapter(httpClient)
    ]);
}

public sealed class CounterCultureAdapter(HttpClient httpClient)
    : RecognizedRoasterAdapter(httpClient, "counterculture", "Counter Culture", "counter culture");

public sealed class BlueBottleAdapter(HttpClient httpClient)
    : RecognizedRoasterAdapter(httpClient, "bluebottle", "Blue Bottle", "blue bottle");

public sealed class IntelligentsiaAdapter(HttpClient httpClient)
    : RecognizedRoasterAdapter(httpClient, "intelligentsia", "Intelligentsia", "intelligentsia");

public abstract class RecognizedRoasterAdapter(
    HttpClient httpClient,
    string id,
    string roasterName,
    string match)
    : HttpRoasterRecipeAdapterBase(httpClient)
{
    public override string Id => id;
    public override string RoasterName => roasterName;

    public override bool CanHandle(Bean bean) =>
        bean.Roaster?.Trim().Contains(match, StringComparison.OrdinalIgnoreCase) == true;

    protected override Task<IReadOnlyList<ScrapedRecipe>> FetchCoreAsync(
        Bean bean,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ScrapedRecipe>>([]);
    }
}

public sealed class OnyxCoffeeLabAdapter(HttpClient httpClient)
    : HttpRoasterRecipeAdapterBase(httpClient)
{
    private const string BaseUrl = "https://onyxcoffeelab.com";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex GuideMarker = SafeRegex(
        @"wistia_id_brew_guide_(?<method>filter|espresso)_en");
    private static readonly Regex CoffeeLine = SafeRegex(
        @"Coffee\s*:\s*(?<dose>\d{1,3}(?:\.\d+)?)\s*g");
    private static readonly Regex WaterOrYieldLine = SafeRegex(
        @"(?:Water|Yield)\s*:\s*(?<amount>\d{1,4}(?:\.\d+)?)\s*g(?:[^<\r\n]{0,40}@\s*(?<temp>\d{2,3}(?:\.\d+)?)\s*°?\s*(?<unit>[CF]))?");
    private static readonly Regex GrindBlock = SafeRegex(
        @"<strong>\s*Grind\s*</strong>\s*(?:<br[^>]*>|\s|:)*\s*(?<hint>[^<\r\n]{1,60})");
    private static readonly Regex MicronGrind = SafeRegex(
        @"(?<microns>\d{2,4})\s*(?:µm|um|microns?)");
    private static readonly Regex Duration = SafeRegex(
        "data-duration\\s*=\\s*\"(?<seconds>\\d{1,4})\"");
    private static readonly Regex DoseYieldTime = SafeRegex(
        @"(?<dose>\d{1,3}(?:\.\d+)?)\s*(?:g|grams?)\s*[:\-→to/]+\s*(?<yield>\d{1,4}(?:\.\d+)?)\s*(?:g|grams?|ml)\s*(?:[^0-9]{0,40}(?<time>\d{1,3}(?:\.\d+)?)\s*(?:s|sec|seconds?))?");
    private static readonly Regex DoseWater = SafeRegex(
        @"(?<dose>\d{1,3}(?:\.\d+)?)\s*(?:g|grams?)\s*[^0-9]{1,10}(?<water>\d{2,4})\s*(?:g|grams?|ml)");
    private static readonly Regex TemperatureC = SafeRegex(
        @"(?<temp>\d{2,3}(?:\.\d+)?)\s*°?\s*C\b");
    private static readonly Regex TemperatureF = SafeRegex(
        @"(?<temp>\d{2,3}(?:\.\d+)?)\s*°?\s*F\b");
    private static readonly Regex Time = SafeRegex(
        @"(?<minutes>\d{1,2})\s*:\s*(?<seconds>\d{2})|(?<total>\d{1,3})\s*(?:s|sec|seconds?)\b");
    private static readonly Regex GrindHint = SafeRegex(
        @"grind[^<:]{0,20}[:\-]\s*(?<hint>[^<\r\n.]{3,40})");
    private static readonly Regex TagStripper = SafeRegex("<[^>]+>");

    private static readonly (BrewMethod Method, string HeadingPattern)[] MethodHeadingPatterns =
    [
        (BrewMethod.Espresso, "espresso"),
        (BrewMethod.V60, @"\bv60\b"),
        (BrewMethod.PourOver, @"pour\s*over|chemex"),
        (BrewMethod.Drip, @"\bdrip\b|\bbatch\b"),
        (BrewMethod.Aeropress, "aeropress"),
        (BrewMethod.FrenchPress, @"french\s*press"),
        (BrewMethod.Moka, "moka"),
        (BrewMethod.Turkish, "turkish|ibrik|cezve"),
        (BrewMethod.Siphon, "siphon|syphon"),
        (BrewMethod.Cupping, "cupping"),
        (BrewMethod.ColdDrip, @"cold\s*drip"),
        (BrewMethod.ColdBrew, @"cold\s*brew"),
        (BrewMethod.SteepAndRelease, @"steep.*release|clever|hario\s*switch")
    ];

    public override string Id => "onyx";
    public override string RoasterName => "Onyx Coffee Lab";

    public override bool CanHandle(Bean bean) =>
        bean.Roaster?.Trim().StartsWith("onyx", StringComparison.OrdinalIgnoreCase) == true;

    protected override async Task<IReadOnlyList<ScrapedRecipe>> FetchCoreAsync(
        Bean bean,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bean.Name))
            return [];

        var url = BuildProductUrl(bean.Name);
        var html = await TryGetHtmlAsync(url, cancellationToken);
        return html is null ? [] : ParseRecipes(html, url);
    }

    internal static string BuildProductUrl(string beanName)
    {
        var lower = beanName.Trim().ToLowerInvariant();
        var slug = new StringBuilder(lower.Length);
        var lastWasDash = false;
        foreach (var character in lower)
        {
            if (char.IsLetterOrDigit(character))
            {
                slug.Append(character);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                slug.Append('-');
                lastWasDash = true;
            }
        }

        return $"{BaseUrl}/products/{slug.ToString().Trim('-')}";
    }

    internal static IReadOnlyList<ScrapedRecipe> ParseRecipes(string html, string sourceUrl)
    {
        var guides = ParseGuideBlocks(html, sourceUrl);
        if (guides.Count != 0)
            return guides;

        var recipes = new List<ScrapedRecipe>();
        foreach (var (method, pattern) in MethodHeadingPatterns)
        {
            var body = ExtractSectionBody(html, pattern);
            var recipe = body is null ? null : ParseSection(body, method, sourceUrl);
            if (recipe is not null)
                recipes.Add(recipe);
        }

        return recipes;
    }

    internal static string? ExtractSectionBody(string html, string headingPattern)
    {
        var heading = new Regex(
            $@"<h([1-4])[^>]*>\s*(?:<[^>]+>\s*)*([^<]*?(?:{headingPattern})[^<]*?)(?:\s*<[^>]+>)*\s*</h\1\s*>",
            RegexOptions.IgnoreCase,
            RegexTimeout);
        var match = heading.Match(html);
        if (!match.Success)
            return null;

        var start = match.Index + match.Length;
        var endPattern = new Regex(
            $@"<h[1-{match.Groups[1].Value}][^>]*>",
            RegexOptions.IgnoreCase,
            RegexTimeout);
        var end = endPattern.Match(html, start);
        return html.Substring(start, (end.Success ? end.Index : html.Length) - start);
    }

    private static IReadOnlyList<ScrapedRecipe> ParseGuideBlocks(string html, string sourceUrl)
    {
        var recipes = new List<ScrapedRecipe>();
        foreach (Match marker in GuideMarker.Matches(html))
        {
            var method = marker.Groups["method"].Value.Equals(
                "espresso",
                StringComparison.OrdinalIgnoreCase)
                ? BrewMethod.Espresso
                : BrewMethod.PourOver;
            var block = ExtractGuideBody(html, marker.Index) ?? SurroundingWindow(html, marker.Index);
            var recipe = ParseGuideBlock(block, method, sourceUrl);
            if (recipe is not null && recipes.All(item => item.BrewMethod != method))
                recipes.Add(recipe);
        }

        return recipes;
    }

    private static ScrapedRecipe? ParseGuideBlock(
        string block,
        BrewMethod method,
        string sourceUrl)
    {
        var coffee = CoffeeLine.Match(block);
        var waterOrYield = WaterOrYieldLine.Match(block);
        var grindBlock = GrindBlock.Match(block);
        var microns = MicronGrind.Match(block);
        var duration = Duration.Match(block);

        var dose = ParseDecimal(coffee.Groups["dose"].Value);
        var output = ParseDecimal(waterOrYield.Groups["amount"].Value);
        var grind = grindBlock.Success
            ? grindBlock.Groups["hint"].Value.Trim()
            : microns.Success ? $"{microns.Groups["microns"].Value}µm" : null;
        var totalTime = ParseDecimal(duration.Groups["seconds"].Value);
        decimal? temperature = null;
        var rawTemperature = ParseDecimal(waterOrYield.Groups["temp"].Value);
        if (rawTemperature.HasValue)
        {
            temperature = waterOrYield.Groups["unit"].Value.Equals(
                "F",
                StringComparison.OrdinalIgnoreCase)
                ? FahrenheitToCelsius(rawTemperature.Value)
                : rawTemperature;
        }

        return CreateRecipe(
            method,
            sourceUrl,
            dose,
            output,
            grind,
            temperature,
            totalTime);
    }

    private static ScrapedRecipe? ParseSection(
        string html,
        BrewMethod method,
        string sourceUrl)
    {
        var text = WebUtility.HtmlDecode(TagStripper.Replace(html, " "));
        var doseYield = DoseYieldTime.Match(text);
        var doseWater = doseYield.Success ? Match.Empty : DoseWater.Match(text);
        var time = Time.Match(text);
        var celsius = TemperatureC.Match(text);
        var fahrenheit = celsius.Success ? Match.Empty : TemperatureF.Match(text);
        var grind = GrindHint.Match(text);

        var dose = ParseDecimal(
            doseYield.Success ? doseYield.Groups["dose"].Value : doseWater.Groups["dose"].Value);
        var output = ParseDecimal(
            doseYield.Success ? doseYield.Groups["yield"].Value : doseWater.Groups["water"].Value);
        decimal? totalTime = ParseDecimal(doseYield.Groups["time"].Value);
        if (!totalTime.HasValue && time.Success)
        {
            totalTime = time.Groups["minutes"].Success
                ? int.Parse(time.Groups["minutes"].Value, CultureInfo.InvariantCulture) * 60
                    + int.Parse(time.Groups["seconds"].Value, CultureInfo.InvariantCulture)
                : ParseDecimal(time.Groups["total"].Value);
        }

        var temperature = ParseDecimal(celsius.Groups["temp"].Value);
        if (!temperature.HasValue)
        {
            var rawFahrenheit = ParseDecimal(fahrenheit.Groups["temp"].Value);
            if (rawFahrenheit.HasValue)
                temperature = FahrenheitToCelsius(rawFahrenheit.Value);
        }

        return CreateRecipe(
            method,
            sourceUrl,
            dose,
            output,
            grind.Success ? grind.Groups["hint"].Value.Trim() : null,
            temperature,
            totalTime);
    }

    private static ScrapedRecipe? CreateRecipe(
        BrewMethod method,
        string sourceUrl,
        decimal? dose,
        decimal? output,
        string? grind,
        decimal? temperature,
        decimal? totalTime)
    {
        if (!dose.HasValue &&
            !output.HasValue &&
            !totalTime.HasValue &&
            string.IsNullOrWhiteSpace(grind))
        {
            return null;
        }

        return new ScrapedRecipe
        {
            BrewMethod = method,
            Title = $"{method.DisplayName()} recipe (Onyx)",
            SourceUrl = sourceUrl,
            DoseIn = dose,
            OutputAmount = output,
            GrindHint = grind,
            BrewTempC = temperature,
            TotalTimeSeconds = totalTime
        };
    }

    private static string? ExtractGuideBody(string html, int markerIndex)
    {
        const string token = "<div class=\"guide-body";
        var start = html.LastIndexOf(token, markerIndex, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        var depth = 0;
        var index = start;
        while (index < html.Length)
        {
            var nextOpen = html.IndexOf("<div", index, StringComparison.OrdinalIgnoreCase);
            var nextClose = html.IndexOf("</div>", index, StringComparison.OrdinalIgnoreCase);
            if (nextClose < 0)
                return null;

            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth++;
                index = nextOpen + 4;
            }
            else
            {
                depth--;
                index = nextClose + 6;
                if (depth <= 0)
                    return html.Substring(start, index - start);
            }
        }

        return null;
    }

    private static string SurroundingWindow(string html, int markerIndex)
    {
        var start = Math.Max(0, markerIndex - 4000);
        var end = Math.Min(html.Length, markerIndex + 4000);
        return html.Substring(start, end - start);
    }

    private static Regex SafeRegex(string pattern) => new(
        pattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static decimal FahrenheitToCelsius(decimal value) =>
        Math.Round((value - 32m) * 5m / 9m, 1);
}
