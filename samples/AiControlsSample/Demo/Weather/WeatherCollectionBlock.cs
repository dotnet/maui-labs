using System.Text;
using Microsoft.Maui.AI.Chat;

namespace AiControlsSample;

/// <summary>
/// Aggregates every <c>GetCurrentWeather</c> call made in a single turn into one block, so asking for
/// several cities (e.g. "weather in Tokyo and Paris") renders as one grouped view (a carousel) instead
/// of a separate card per city.
/// </summary>
/// <remarks>
/// Demonstrates <b>many-to-one</b> block mapping. The <see cref="WeatherCollectionHandler"/> emits this
/// block on the first weather call and then stays active for the rest of the turn, folding each
/// additional call/result into <see cref="Items"/> rather than emitting a new block. Because it is a
/// plain <see cref="ContentBlock"/> (not a tool block), a panel with no custom template falls back to
/// the default view, which shows <see cref="ToString"/> — the plain-text summary below.
/// </remarks>
public sealed class WeatherCollectionBlock : ContentBlock
{
    private readonly List<WeatherItem> _items = new();

    /// <summary>The per-city weather items, in call order.</summary>
    public IReadOnlyList<WeatherItem> Items => _items;

    /// <summary>Returns the item for a call id, creating a pending one if it does not exist yet.</summary>
    internal WeatherItem GetOrAdd(string callId)
    {
        foreach (var item in _items)
        {
            if (item.CallId == callId)
                return item;
        }

        var created = new WeatherItem { CallId = callId };
        _items.Add(created);
        return created;
    }

    /// <summary>Finds the item for a call id, or <see langword="null"/> if the call was not seen.</summary>
    internal WeatherItem? Find(string callId)
    {
        foreach (var item in _items)
        {
            if (item.CallId == callId)
                return item;
        }
        return null;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append("Weather (").Append(_items.Count).Append(')');
        foreach (var item in _items)
        {
            sb.AppendLine();
            sb.Append("• ").Append(item.Location ?? item.City ?? "…");
            sb.Append(item.HasResult
                ? $" — {item.Temperature}°C, {item.Conditions}"
                : " — …");
        }
        return sb.ToString();
    }
}
