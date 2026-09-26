using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public static class AIPromptBuilder
{
    public static string BuildPrompt(AIAdviceRequestDto context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var prompt = new StringBuilder();
        prompt.AppendLine("## Current Shot");
        prompt.AppendLine($"- Brew method: {context.CurrentShot.BrewMethod.DisplayName()}");
        if (!string.IsNullOrWhiteSpace(context.CurrentShot.DrinkType))
            prompt.AppendLine($"- Drink type: {context.CurrentShot.DrinkType}");
        prompt.AppendLine($"- Dose: {context.CurrentShot.DoseIn}g in");
        if (context.CurrentShot.ActualOutput.HasValue)
            prompt.AppendLine($"- Yield: {context.CurrentShot.ActualOutput}g out");
        if (context.CurrentShot.ActualTime.HasValue)
            prompt.AppendLine($"- Time: {context.CurrentShot.ActualTime}s");
        if (context.CurrentShot.GrindMicrons.HasValue)
            prompt.AppendLine($"- Grind: {context.CurrentShot.GrindMicrons}µm");
        if (context.CurrentShot.Rating.HasValue)
            prompt.AppendLine($"- Rating: {context.CurrentShot.Rating}/4");
        if (!string.IsNullOrWhiteSpace(context.CurrentShot.TastingNotes))
            prompt.AppendLine($"- Tasting notes: {context.CurrentShot.TastingNotes}");

        prompt.AppendLine();
        prompt.AppendLine("## Bean Information");
        prompt.AppendLine($"- Name: {context.BeanInfo.Name}");
        if (!string.IsNullOrWhiteSpace(context.BeanInfo.Roaster))
            prompt.AppendLine($"- Roaster: {context.BeanInfo.Roaster}");
        if (!string.IsNullOrWhiteSpace(context.BeanInfo.Origin))
            prompt.AppendLine($"- Origin: {context.BeanInfo.Origin}");
        prompt.AppendLine($"- Days since roast: {context.BeanInfo.DaysFromRoast}");
        if (!string.IsNullOrWhiteSpace(context.BeanInfo.Notes))
            prompt.AppendLine($"- Flavor notes: {context.BeanInfo.Notes}");

        if (context.Equipment is not null)
        {
            prompt.AppendLine();
            prompt.AppendLine("## Equipment");
            if (!string.IsNullOrWhiteSpace(context.Equipment.MachineName))
                prompt.AppendLine($"- Machine: {context.Equipment.MachineName}");
            if (!string.IsNullOrWhiteSpace(context.Equipment.GrinderName))
                prompt.AppendLine($"- Grinder: {context.Equipment.GrinderName}");
        }

        if (context.MadeFor is not null)
        {
            prompt.AppendLine();
            prompt.AppendLine($"## Made For: {context.MadeFor.Name}");
            if (!string.IsNullOrWhiteSpace(context.MadeFor.Context))
            {
                prompt.AppendLine("Persona context (their stated coffee preferences — weight advice accordingly):");
                prompt.AppendLine(context.MadeFor.Context);
            }
            else
            {
                prompt.AppendLine("(No persona preferences recorded yet.)");
            }
        }

        if (context.HistoricalShots.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("## Previous Shots (same beans, sorted by rating)");

            var bestShots = context.HistoricalShots
                .Where(shot => shot.Rating is >= 3)
                .Take(5);
            var wroteBestHeading = false;
            foreach (var shot in bestShots)
            {
                if (!wroteBestHeading)
                {
                    prompt.AppendLine("Best rated shots:");
                    wroteBestHeading = true;
                }
                prompt.AppendLine($"- {FormatShot(shot, includeGrind: true)}");
            }

            var recentShots = context.HistoricalShots
                .OrderByDescending(shot => shot.Timestamp)
                .Take(3);
            var wroteRecentHeading = false;
            foreach (var shot in recentShots)
            {
                if (!wroteRecentHeading)
                {
                    prompt.AppendLine("Most recent shots:");
                    wroteRecentHeading = true;
                }
                prompt.AppendLine($"- {FormatShot(shot, includeGrind: false)}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine(
            $"Based on this {context.CurrentShot.BrewMethod.DisplayName()} brew, the drink type, and my history, " +
            "what adjustments would you suggest to improve my next one? Keep advice grounded in the brewing " +
            "method above — do not import assumptions from other methods.");
        return prompt.ToString();
    }

    public static string BuildAdviceSystemPrompt(BrewMethod method)
    {
        var profile = MethodAdvice(method);
        return
            $"You are an expert barista assistant helping improve {profile.Subject} brews.\n" +
            "Analyse the brew data and provide 1–3 specific parameter adjustments.\n" +
            $"{profile.Targets}\n" +
            $"Adjust using these levers: {profile.Levers}.\n" +
            "Be practical and specific with amounts (e.g. '0.5g', '2 clicks finer', '15s longer steep', '+1°C').\n" +
            "Provide brief reasoning in one sentence.\n" +
            $"This is a {method.DisplayName()} brew — do NOT apply assumptions from other brewing methods. " +
            "Espresso ratios and timings, for example, do not apply to pour over, French press, cold brew, etc.";
    }

    public static string BuildPassivePrompt(AIAdviceRequestDto context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var prompt = new StringBuilder();
        prompt.Append($"Shot: {context.CurrentShot.DoseIn}g in");
        if (context.CurrentShot.ActualOutput.HasValue)
            prompt.Append($", {context.CurrentShot.ActualOutput}g out");
        if (context.CurrentShot.ActualTime.HasValue)
            prompt.Append($", {context.CurrentShot.ActualTime}s");
        if (context.CurrentShot.GrindMicrons.HasValue)
            prompt.Append($", grind {context.CurrentShot.GrindMicrons}µm");

        var best = context.HistoricalShots.FirstOrDefault(shot => shot.Rating is >= 3);
        if (best is not null)
        {
            prompt.AppendLine();
            prompt.Append($"Best shot was: {best.DoseIn}g in");
            if (best.ActualOutput.HasValue)
                prompt.Append($", {best.ActualOutput}g out");
            if (best.ActualTime.HasValue)
                prompt.Append($", {best.ActualTime}s");
        }

        prompt.AppendLine();
        prompt.AppendLine("Quick tip?");
        return prompt.ToString();
    }

    public static string BuildPassiveAdviceSystemPrompt(BrewMethod method)
    {
        var profile = MethodAdvice(method);
        return
            $"You are a brief {profile.Subject} advisor. Give ONE short sentence of advice (max 15 words). " +
            $"This is a {method.DisplayName()} brew — use only levers appropriate to that method ({profile.Levers}).";
    }

    static string FormatShot(ShotContextDto shot, bool includeGrind)
    {
        var details = new List<string> { $"{shot.DoseIn}g in" };
        if (shot.ActualOutput.HasValue)
            details.Add($"{shot.ActualOutput}g out");
        if (shot.ActualTime.HasValue)
            details.Add($"{shot.ActualTime}s");
        if (includeGrind && shot.GrindMicrons.HasValue)
            details.Add($"grind {shot.GrindMicrons}µm");
        if (shot.Rating.HasValue)
            details.Add($"rated {shot.Rating}/4");
        return string.Join(", ", details);
    }

    static (string Subject, string Targets, string Levers) MethodAdvice(BrewMethod method) =>
        method switch
        {
            BrewMethod.Espresso => (
                "espresso",
                "Consider extraction ratio (target 1:2 to 1:2.5), shot time (25–35s including preinfusion), grind size, dose, and basket fit.",
                "grind size, dose, yield (output), shot time, preinfusion"),
            BrewMethod.PourOver => (
                "pour-over coffee",
                "Consider brew ratio (1:15 to 1:17), total drawdown time (2:30–4:00 for a single cup), bloom (30–45s with ~2x dose water), pour structure, and water temperature (92–96°C).",
                "grind size, dose, water ratio, bloom time, pour rate / pulse count, water temperature"),
            BrewMethod.V60 => (
                "V60 pour-over coffee",
                "Consider brew ratio (1:15 to 1:17), total drawdown 2:30–3:30, bloom (30–45s, ~2x dose), pour pulses, and water temperature (92–96°C).",
                "grind size, dose, water ratio, bloom time, number/size of pours, water temperature"),
            BrewMethod.Moka => (
                "Moka pot",
                "Consider dose-to-water ratio (typically 1:7 to 1:10), heat level (low–medium), and total time on heat (4–6 min). Pull off heat as soon as you hear sputtering to avoid scorching.",
                "grind size (espresso-fine to medium-fine), dose, water amount, heat level, time on heat"),
            BrewMethod.Drip => (
                "batch drip coffee",
                "Consider brew ratio (1:16 to 1:18), total brew time (4–6 min for a typical batch), and even bed wetting.",
                "grind size (medium), dose, water volume, brew time, water temperature"),
            BrewMethod.Aeropress => (
                "AeroPress",
                "Consider brew ratio (1:12 to 1:16 standard, or concentrate then dilute), steep time (1–2 min), press time (~30s), and orientation (inverted vs standard).",
                "grind size (medium-fine), dose, water ratio, steep time, water temperature, press speed, inverted vs standard"),
            BrewMethod.FrenchPress => (
                "French press",
                "Consider brew ratio (1:12 to 1:17), steep time (4–5 min), grind coarseness, and crust handling (break + skim or leave). Decant promptly after plunging.",
                "grind size (coarse), dose, water ratio, steep time, water temperature, crust handling"),
            BrewMethod.Turkish => (
                "Turkish (ibrik / cezve)",
                "Consider 1:10 ratio, very fine (powder) grind, low heat for ~3–4 min, foam-raising technique (heat → lift → reheat). Do not stir after foam forms.",
                "grind fineness (powder), dose, water amount, heat level, total time, foam technique, sweetness level"),
            BrewMethod.Siphon => (
                "siphon (vacuum) coffee",
                "Consider brew ratio (1:14 to 1:17), total brew time 2–3 min after full draw-up, medium grind, and stir technique on draw-up and draw-down.",
                "grind size (medium), dose, water ratio, draw-up time, brew time, stir pattern, heat level"),
            BrewMethod.Cupping => (
                "SCA-style cupping",
                "SCA standard is 8.25g per 150ml water (~1:18), 4-minute steep, then break the crust and skim. Use 93°C water.",
                "grind size (medium-coarse, consistent), dose-to-water ratio, steep time, crust break timing, water temperature"),
            BrewMethod.ColdBrew => (
                "cold brew",
                "Consider concentrate ratio (1:5 to 1:8 for ready-to-drink, or stronger to dilute later), steep time (12–24 h at room or fridge temp), and filtration. Coarse grind only.",
                "grind size (coarse), dose, water ratio, steep time, steep temperature (room vs fridge), dilution ratio"),
            BrewMethod.ColdDrip => (
                "cold drip / Kyoto",
                "Consider brew ratio (1:8 to 1:10), total drip time (4–12 h), drip rate (~1 drop/sec), and bed even-wetting.",
                "grind size (medium-coarse to coarse), dose, water ratio, drip rate, total drip time"),
            BrewMethod.SteepAndRelease => (
                "steep-and-release (Clever / Hario Switch)",
                "Consider brew ratio (1:15 to 1:17), steep time (2–3 min before release), bloom (30s), and water temperature (92–96°C).",
                "grind size (medium), dose, water ratio, bloom time, steep time, release timing, water temperature"),
            _ => (
                method.DisplayName(),
                "Use brewing parameters appropriate for this method (ratio, time, grind, temperature).",
                "grind size, dose, water ratio, brew time, water temperature")
        };
}
