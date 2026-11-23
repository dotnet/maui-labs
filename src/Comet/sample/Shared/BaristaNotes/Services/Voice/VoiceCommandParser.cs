#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services.Voice;

public sealed class VoiceCommandParser
{
    static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    static readonly IReadOnlyDictionary<string, string> Vocabulary =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["grand"] = "grind",
            ["grin"] = "grind",
            ["ground setting"] = "grind setting",
            ["doze"] = "dose",
            ["those"] = "dose",
            ["yelled"] = "yield",
            ["yeild"] = "yield",
            ["short"] = "shot",
            ["shut"] = "shot",
            ["extra action"] = "extraction",
            ["expresso"] = "espresso",
            ["store"] = "star",
            ["stores"] = "stars",
            ["stare"] = "star",
            ["stares"] = "stars",
            ["stumped town"] = "Stumptown",
            ["intelligencia"] = "Intelligentsia",
            ["your gosh if"] = "Yirgacheffe",
        };

    static readonly IReadOnlyDictionary<string, int> SmallNumbers =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3,
            ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
            ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11,
            ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
            ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17,
            ["eighteen"] = 18, ["nineteen"] = 19,
        };

    static readonly IReadOnlyDictionary<string, int> Tens =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40,
            ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70,
            ["eighty"] = 80, ["ninety"] = 90,
        };

    const string NumberPattern = @"(?<n>\d+(?:\.\d+)?)";
    const string SignedNumberPattern = @"(?<n>-?\d+(?:\.\d+)?)";

    public ParsedVoiceCommand Parse(string transcript)
    {
        var normalized = Normalize(transcript);
        if (string.IsNullOrWhiteSpace(normalized))
            return Unknown(normalized, "I didn't hear a command. Hold the microphone and try again.");

        if (Regex.IsMatch(normalized, @"\b(cancel|never mind|nevermind|stop)\b", RegexOptions.IgnoreCase))
            return new ParsedVoiceCommand
            {
                Intent = CommandIntent.Cancel,
                NormalizedTranscript = normalized,
            };

        if (Regex.IsMatch(normalized, @"\b(help|what can (?:i|you) (?:say|do))\b", RegexOptions.IgnoreCase))
            return new ParsedVoiceCommand
            {
                Intent = CommandIntent.Help,
                NormalizedTranscript = normalized,
            };

        var navigation = ParseNavigation(normalized);
        if (navigation is not null)
            return new ParsedVoiceCommand
            {
                Intent = CommandIntent.Navigate,
                NormalizedTranscript = normalized,
                Navigation = navigation,
            };

        if (Regex.IsMatch(normalized, @"\b(add|create)\s+(?:a\s+)?(?:new\s+)?bean\b", RegexOptions.IgnoreCase))
            return ParseAddBean(normalized);

        if (Regex.IsMatch(normalized, @"\b(add|create)\s+(?:a\s+)?(?:new\s+)?bag\b", RegexOptions.IgnoreCase))
            return ParseAddBag(normalized);

        if (Regex.IsMatch(normalized, @"\b(add|create)\s+(?:a\s+)?(?:new\s+)?(?:machine|grinder|tamper|puck screen|equipment)\b", RegexOptions.IgnoreCase))
            return ParseAddEquipment(normalized);

        if (Regex.IsMatch(normalized, @"\b(add|create)\s+(?:a\s+)?(?:new\s+)?profile\b", RegexOptions.IgnoreCase))
            return ParseAddProfile(normalized);

        if (Regex.IsMatch(normalized, @"\b(rate|rating)\b.*\b(last|latest|recent)\s+shot\b|\b(last|latest|recent)\s+shot\b.*\b(rate|rating)\b", RegexOptions.IgnoreCase))
            return ParseRateLastShot(normalized);

        if (Regex.IsMatch(normalized, @"\b(add|append|set)\s+tasting notes?\b", RegexOptions.IgnoreCase))
            return ParseTastingNotes(normalized);

        var query = ParseQuery(normalized);
        if (query is not null)
            return query;

        var fields = ParseFields(normalized, out var fieldError);
        var isLogCommand = Regex.IsMatch(
            normalized,
            @"\b(log|new|record|pull|create|save)\b.*\b(shot|drink|espresso)\b|\b(shot|drink|espresso)\b.*\b(log|record|pull|create|save)\b",
            RegexOptions.IgnoreCase);
        var isFieldUpdate = Regex.IsMatch(
            normalized,
            @"\b(set|change|update)\b.*\b(dose|yield|output|time|grind|rating|notes?)\b",
            RegexOptions.IgnoreCase);

        if (isLogCommand || isFieldUpdate)
        {
            var missing = isLogCommand
                ? MissingRequiredFields(fields)
                : Array.Empty<string>();
            return new ParsedVoiceCommand
            {
                Intent = CommandIntent.LogShot,
                NormalizedTranscript = normalized,
                FieldUpdates = fields,
                CommitNewDrink = isLogCommand && missing.Length == 0,
                ErrorMessage = fieldError ?? (missing.Length == 0
                    ? null
                    : $"Please provide {JoinNaturalLanguage(missing)}."),
            };
        }

        return Unknown(
            normalized,
            "I couldn't understand that command. Try logging a shot, updating a field, or opening a page.");
    }

    public string Normalize(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return "";

        var result = transcript.Trim();
        foreach (var pair in Vocabulary.OrderByDescending(pair => pair.Key.Length))
        {
            result = Regex.Replace(
                result,
                $@"\b{Regex.Escape(pair.Key)}\b",
                pair.Value,
                RegexOptions.IgnoreCase);
        }

        result = Regex.Replace(
            result,
            @"\b([2-9])0\s+([1-9])\b",
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}");
        result = ReplaceNumberWords(result);
        return Regex.Replace(result, @"\s+", " ").Trim();
    }

    static ParsedVoiceCommand ParseAddBean(string transcript)
    {
        var match = Regex.Match(
            transcript,
            @"\b(?:add|create)\s+(?:a\s+)?(?:new\s+)?bean\s+(?:(?:called|named)\s+)?(?<name>.+?)(?:\s+from\s+(?<roaster>.+?))?(?:\s+(?:origin|from origin)\s+(?<origin>.+))?$",
            RegexOptions.IgnoreCase);
        var name = match.Groups["name"].Value.Trim();
        return new ParsedVoiceCommand
        {
            Intent = CommandIntent.AddBean,
            NormalizedTranscript = transcript,
            Name = name,
            Roaster = NullIfEmpty(match.Groups["roaster"].Value),
            Origin = NullIfEmpty(match.Groups["origin"].Value),
            ErrorMessage = name.Length == 0 ? "Please provide a bean name." : null,
        };
    }

    static ParsedVoiceCommand ParseAddBag(string transcript)
    {
        var bagMatch = Regex.Match(
            transcript,
            @"\bbag(?:\s+of)?(?<tail>.*)$",
            RegexOptions.IgnoreCase);
        var tail = bagMatch.Groups["tail"].Value.Trim();
        var roastMarker = Regex.Match(tail, @"\broast(?:ed)?\b", RegexOptions.IgnoreCase);
        var beanName = NullIfEmpty(
            roastMarker.Success ? tail[..roastMarker.Index] : tail);
        return new ParsedVoiceCommand
        {
            Intent = CommandIntent.AddBag,
            NormalizedTranscript = transcript,
            BeanName = beanName,
            RoastDate = ParseRoastDate(transcript),
        };
    }

    static ParsedVoiceCommand ParseAddEquipment(string transcript)
    {
        var typeMatch = Regex.Match(
            transcript,
            @"\b(machine|grinder|tamper|puck screen|equipment)\b",
            RegexOptions.IgnoreCase);
        var typeText = typeMatch.Value.ToLowerInvariant();
        var nameMatch = Regex.Match(
            transcript,
            @"\b(?:called|named)\s+(?<name>.+)$",
            RegexOptions.IgnoreCase);
        var name = NullIfEmpty(nameMatch.Groups["name"].Value);
        if (name is null && typeMatch.Success)
            name = NullIfEmpty(transcript[(typeMatch.Index + typeMatch.Length)..]);

        return new ParsedVoiceCommand
        {
            Intent = CommandIntent.AddEquipment,
            NormalizedTranscript = transcript,
            Name = name,
            EquipmentType = typeText switch
            {
                "machine" => EquipmentType.Machine,
                "grinder" => EquipmentType.Grinder,
                "tamper" => EquipmentType.Tamper,
                "puck screen" => EquipmentType.PuckScreen,
                _ => EquipmentType.Other,
            },
            ErrorMessage = name is null ? "Please provide an equipment name." : null,
        };
    }

    static ParsedVoiceCommand ParseAddProfile(string transcript)
    {
        var match = Regex.Match(
            transcript,
            @"\bprofile(?:\s+for|\s+named|\s+called)?\s+(?<name>.+)$",
            RegexOptions.IgnoreCase);
        var name = NullIfEmpty(match.Groups["name"].Value);
        return new ParsedVoiceCommand
        {
            Intent = CommandIntent.AddProfile,
            NormalizedTranscript = transcript,
            Name = name,
            ErrorMessage = name is null ? "Please provide a name for the profile." : null,
        };
    }

    static ParsedVoiceCommand ParseRateLastShot(string transcript)
    {
        var rating = ParseRating(transcript);
        return new ParsedVoiceCommand
        {
            Intent = CommandIntent.RateShot,
            NormalizedTranscript = transcript,
            FieldUpdates = new VoiceFieldUpdates(Rating: rating),
            ErrorMessage = rating.HasValue ? null : "Please provide a rating from 0 to 4.",
        };
    }

    static ParsedVoiceCommand ParseTastingNotes(string transcript)
    {
        var notes = Regex.Replace(
            transcript,
            @"^.*?\b(?:add|append|set)\s+tasting notes?(?:\s+to\s+(?:my\s+)?(?:last|latest|recent)\s+shot)?\s*",
            "",
            RegexOptions.IgnoreCase).Trim();
        notes = Regex.Replace(
            notes,
            @"\s+to\s+(?:my\s+)?(?:last|latest|recent)\s+shot$",
            "",
            RegexOptions.IgnoreCase).Trim();
        return new ParsedVoiceCommand
        {
            Intent = CommandIntent.AddTastingNotes,
            NormalizedTranscript = transcript,
            FieldUpdates = new VoiceFieldUpdates(TastingNotes: NullIfEmpty(notes)),
            ErrorMessage = notes.Length == 0 ? "Please provide tasting notes to add." : null,
        };
    }

    static ParsedVoiceCommand? ParseQuery(string transcript)
    {
        if (Regex.IsMatch(
                transcript,
                @"\b(?:last|latest|most\s+recent)\s+shot\b",
                RegexOptions.IgnoreCase))
        {
            return new ParsedVoiceCommand
            {
                Intent = CommandIntent.Query,
                NormalizedTranscript = transcript,
                QueryKind = VoiceQueryKind.LastShot,
                QueryLimit = 1,
            };
        }

        if (Regex.IsMatch(transcript, @"\b(?:how\s+many|count)\b.*\bshots?\b", RegexOptions.IgnoreCase))
            return ParseShotQuery(transcript, VoiceQueryKind.ShotCount, countOnly: true);

        if (Regex.IsMatch(
                transcript,
                @"\b(?:find|list|which|what|get|search)\b.*\bshots?\b",
                RegexOptions.IgnoreCase))
        {
            return ParseShotQuery(transcript, VoiceQueryKind.Shots, countOnly: false);
        }

        foreach (var (kind, nounPattern) in new[]
        {
            (VoiceQueryKind.Beans, @"beans?"),
            (VoiceQueryKind.Bags, @"bags?"),
            (VoiceQueryKind.Equipment, @"(?:equipment|gear|machines?|grinders?|tampers?|puck\s+screens?)"),
            (VoiceQueryKind.Profiles, @"(?:profiles?|people|users?)"),
        })
        {
            var countOnly = Regex.IsMatch(
                transcript,
                $@"\b(?:how\s+many|count)\b.*\b{nounPattern}\b",
                RegexOptions.IgnoreCase);
            var listQuery = Regex.IsMatch(
                transcript,
                $@"\b(?:find|list|which|what|get|search)\b.*\b{nounPattern}\b",
                RegexOptions.IgnoreCase);
            if (countOnly || listQuery)
                return ParseEntityQuery(transcript, kind, countOnly);
        }

        return null;
    }

    static ParsedVoiceCommand ParseShotQuery(
        string transcript,
        VoiceQueryKind queryKind,
        bool countOnly)
    {
        var remaining = transcript;
        var madeBy = ExtractQueryFilter(
            ref remaining,
            @"\bmade\s+by\s+(?<value>.+?)(?=\s+(?:made\s+for|with|using|rated|rating|minimum|at\s+least|today|yesterday|this\s+week|last\s+week|this\s+month|last\s+month|all\s+time|did|do|have|has|were|was|are)\b|[?.!,]|$)");
        var madeFor = ExtractQueryFilter(
            ref remaining,
            @"\bmade\s+for\s+(?<value>.+?)(?=\s+(?:made\s+by|with|using|rated|rating|minimum|at\s+least|today|yesterday|this\s+week|last\s+week|this\s+month|last\s+month|all\s+time|did|do|have|has|were|was|are)\b|[?.!,]|$)");

        int? minimumRating = null;
        string? filterError = null;
        var ratingMatch = Regex.Match(
            remaining,
            @"\b(?:with\s+(?:a\s+)?minimum\s+rating(?:\s+of)?|minimum\s+rating(?:\s+of)?|rated(?:\s+at\s+least)?|rating(?:\s+of)?|at\s+least)\s+(?<value>\d+(?:\.\d+)?)\s*(?:\+|stars?|or\s+(?:higher|better))?",
            RegexOptions.IgnoreCase);
        if (ratingMatch.Success)
        {
            remaining = remaining.Remove(ratingMatch.Index, ratingMatch.Length);
            if (decimal.TryParse(
                    ratingMatch.Groups["value"].Value,
                    NumberStyles.Number,
                    EnUs,
                    out var rating) &&
                rating == decimal.Truncate(rating) &&
                rating is >= 0 and <= 4)
            {
                minimumRating = (int)rating;
            }
            else
            {
                filterError = "Minimum rating must be a whole number between 0 and 4.";
            }
        }

        var beanName = ExtractQueryFilter(
            ref remaining,
            @"\b(?:with|using)\s+(?:the\s+)?(?<value>.+?)(?=\s+(?:made\s+by|made\s+for|rated|rating|minimum|at\s+least|today|yesterday|this\s+week|last\s+week|this\s+month|last\s+month|all\s+time|did|do|have|has|were|was|are)\b|[?.!,]|$)");
        if (beanName is not null)
            beanName = Regex.Replace(beanName, @"\s+beans?$", "", RegexOptions.IgnoreCase).Trim();

        remaining = Regex.Replace(
            remaining,
            @"\b(?:today|yesterday|this\s+week|last\s+week|this\s+month|last\s+month|all\s+time)\b",
            "",
            RegexOptions.IgnoreCase);
        remaining = Regex.Replace(
            remaining,
            @"\b(?:how\s+many|count|find|list|which|what|get|search|show|matching|recent|latest|shots?|have|has|i|we|you|pulled|pull|did|do|are|there|were|was|please|tell\s+me|me)\b",
            "",
            RegexOptions.IgnoreCase);
        remaining = Regex.Replace(
            remaining,
            @"\blimit\s+\d+\b",
            "",
            RegexOptions.IgnoreCase);
        remaining = Regex.Replace(remaining, @"[\s?.!,]+", " ").Trim();

        if (filterError is null && remaining.Length > 0)
            filterError = $"I couldn't apply the shot filter “{remaining}”.";

        return new ParsedVoiceCommand
        {
            Intent = CommandIntent.Query,
            NormalizedTranscript = transcript,
            QueryKind = queryKind,
            QueryPeriod = ParsePeriod(transcript),
            QueryBeanName = NullIfEmpty(beanName ?? ""),
            QueryMadeBy = NullIfEmpty(madeBy ?? ""),
            QueryMadeFor = NullIfEmpty(madeFor ?? ""),
            QueryMinimumRating = minimumRating,
            QueryCountOnly = countOnly,
            QueryLimit = ParseLimit(transcript),
            ErrorMessage = filterError,
        };
    }

    static ParsedVoiceCommand ParseEntityQuery(
        string transcript,
        VoiceQueryKind queryKind,
        bool countOnly)
    {
        var remaining = transcript;
        var name = ExtractQueryFilter(
            ref remaining,
            @"\b(?:named|called)\s+(?<value>.+?)(?=\s+(?:by|from|of|with|including|include|limit)\b|[?.!,]|$)");
        string? roaster = null;
        string? origin = null;
        EquipmentType? equipmentType = null;
        var explicitlyActive = Regex.IsMatch(
            transcript,
            @"\b(?:active|open|current)\b",
            RegexOptions.IgnoreCase);
        var includesCompleted = Regex.IsMatch(
            transcript,
            @"\b(?:all|completed|finished|inactive)\b",
            RegexOptions.IgnoreCase);
        var activeOnly = queryKind == VoiceQueryKind.Bags
            && (countOnly ? explicitlyActive : !includesCompleted);

        switch (queryKind)
        {
            case VoiceQueryKind.Beans:
                roaster = ExtractQueryFilter(
                    ref remaining,
                    @"\bby\s+(?<value>.+?)(?=\s+(?:from|named|called|limit)\b|[?.!,]|$)");
                origin = ExtractQueryFilter(
                    ref remaining,
                    @"\bfrom\s+(?<value>.+?)(?=\s+(?:by|named|called|limit)\b|[?.!,]|$)");
                break;
            case VoiceQueryKind.Bags:
                name ??= ExtractQueryFilter(
                    ref remaining,
                    @"\b(?:of|with)\s+(?<value>.+?)(?=\s+(?:from|by|including|include|limit)\b|[?.!,]|$)");
                roaster = ExtractQueryFilter(
                    ref remaining,
                    @"\b(?:from|by)\s+(?<value>.+?)(?=\s+(?:of|with|including|include|limit)\b|[?.!,]|$)");
                break;
            case VoiceQueryKind.Equipment:
                var typeMatch = Regex.Match(
                    transcript,
                    @"\b(machine|grinder|tamper|puck\s+screen)s?\b",
                    RegexOptions.IgnoreCase);
                equipmentType = typeMatch.Groups[1].Value.ToLowerInvariant() switch
                {
                    "machine" => EquipmentType.Machine,
                    "grinder" => EquipmentType.Grinder,
                    "tamper" => EquipmentType.Tamper,
                    "puck screen" => EquipmentType.PuckScreen,
                    _ => null,
                };
                break;
            case VoiceQueryKind.Profiles:
                name ??= ExtractQueryFilter(
                    ref remaining,
                    @"\bfor\s+(?<value>.+?)(?=\s+(?:named|called|limit)\b|[?.!,]|$)");
                break;
        }

        return new ParsedVoiceCommand
        {
            Intent = CommandIntent.Query,
            NormalizedTranscript = transcript,
            QueryKind = queryKind,
            QueryName = NullIfEmpty(name ?? ""),
            QueryRoaster = NullIfEmpty(roaster ?? ""),
            QueryOrigin = NullIfEmpty(origin ?? ""),
            QueryEquipmentType = equipmentType,
            QueryActiveOnly = activeOnly,
            QueryCountOnly = countOnly,
            QueryLimit = ParseLimit(transcript),
        };
    }

    static int ParseLimit(string transcript)
    {
        var match = Regex.Match(
            transcript,
            @"\blimit\s+(?<value>\d+)\b",
            RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["value"].Value, out var limit)
            ? Math.Clamp(limit, 1, 10)
            : 5;
    }

    static string? ExtractQueryFilter(ref string transcript, string pattern)
    {
        var match = Regex.Match(transcript, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        transcript = transcript.Remove(match.Index, match.Length);
        return NullIfEmpty(match.Groups["value"].Value);
    }

    static VoiceFieldUpdates ParseFields(string transcript, out string? error)
    {
        var dose = MatchDecimal(
            transcript,
            $@"\bdose(?:\s+(?:of|to|is))?\s*{NumberPattern}",
            $@"\b{NumberPattern}\s*(?:grams?|g)?\s+in\b");
        var yield = MatchDecimal(
            transcript,
            $@"\b(?:yield|output)(?:\s+(?:of|to|is))?\s*{NumberPattern}",
            $@"\b{NumberPattern}\s*(?:grams?|g)?\s+out\b");
        var time = MatchDecimal(
            transcript,
            $@"\b(?:time|duration)(?:\s+(?:of|to|is))?\s*{NumberPattern}",
            $@"\b{NumberPattern}\s*(?:seconds?|secs?|s)\b");
        var grindValue = MatchDecimal(
            transcript,
            $@"\bgrind(?:\s+(?:setting|size))?(?:\s+(?:of|to|is))?\s*{SignedNumberPattern}",
            $@"\b{SignedNumberPattern}\s*(?:microns?|µm|um)\b");
        int? grind = null;
        error = null;
        if (grindValue.HasValue)
        {
            if (grindValue.Value == decimal.Truncate(grindValue.Value) &&
                grindValue.Value is >= int.MinValue and <= int.MaxValue)
                grind = (int)grindValue.Value;
            else
                error = "Grind microns must be a whole number within the Int32 range.";
        }
        else if (Regex.IsMatch(
                     transcript,
                     @"\bgrind(?:\s+(?:setting|size))?(?:\s+(?:of|to|is))?\s*-?\d",
                     RegexOptions.IgnoreCase))
        {
            error = "Grind microns must be a whole number within the Int32 range.";
        }
        var notesMatch = Regex.Match(
            transcript,
            @"\b(?:tasting\s+)?notes?(?:\s+(?:are|of|to))?\s+(?<notes>.+)$",
            RegexOptions.IgnoreCase);

        return new VoiceFieldUpdates(
            dose,
            yield,
            time,
            grind,
            ParseRating(transcript),
            NullIfEmpty(notesMatch.Groups["notes"].Value));
    }

    static VoiceNavigationRequest? ParseNavigation(string transcript)
    {
        if (!Regex.IsMatch(
                transcript,
                @"\b(go to|open|navigate to|take me to|show (?:me|my))\b",
                RegexOptions.IgnoreCase))
            return null;

        var lower = transcript.ToLowerInvariant();

        // Try entity-detail patterns first: "show me bean 42", "open profile 3"
        var bagDetail = Regex.Match(lower, @"\bbags?\s+(?:#?\s*)?(\d+)\b");
        if (bagDetail.Success && int.TryParse(bagDetail.Groups[1].Value, out var bagId))
            return new VoiceNavigationRequest("bags", EntityId: bagId);

        var beanDetail = Regex.Match(lower, @"\bbeans?\s+(?:#?\s*)?(\d+)\b");
        if (beanDetail.Success && int.TryParse(beanDetail.Groups[1].Value, out var beanId))
            return new VoiceNavigationRequest("beans", EntityId: beanId);

        var equipDetail = Regex.Match(lower, @"\b(?:equipment|gear|machines?|grinders?)\s+(?:#?\s*)?(\d+)\b");
        if (equipDetail.Success && int.TryParse(equipDetail.Groups[1].Value, out var equipId))
            return new VoiceNavigationRequest("equipment", EntityId: equipId);

        var profileDetail = Regex.Match(lower, @"\bprofiles?\s+(?:#?\s*)?(\d+)\b");
        if (profileDetail.Success && int.TryParse(profileDetail.Groups[1].Value, out var profileId))
            return new VoiceNavigationRequest("profiles", EntityId: profileId);

        // Name-based detail: "show me the Ethiopia bean" → destination=beans with EntityName for service lookup
        var beanNameMatch = Regex.Match(
            lower,
            @"(?:show\s+me|open)\s+(?:the\s+)?(.+?)\s+beans?\b");
        if (beanNameMatch.Success)
        {
            var name = beanNameMatch.Groups[1].Value.Trim();
            if (name.Length > 0 && !Regex.IsMatch(name, @"^\d+$"))
                return new VoiceNavigationRequest("beans", EntityName: name);
        }

        var profileNameMatch = Regex.Match(
            lower,
            @"(?:show\s+me|open)\s+(?:the\s+)?(.+?)(?:'s)?\s+profiles?\b");
        if (profileNameMatch.Success)
        {
            var name = profileNameMatch.Groups[1].Value.Trim();
            if (name.Length > 0 && !Regex.IsMatch(name, @"^\d+$"))
                return new VoiceNavigationRequest("profiles", EntityName: name);
        }

        // Management-level navigation (no entity)
        var destination = lower switch
        {
            var text when Regex.IsMatch(text, @"\b(new (?:shot|drink)|log (?:a )?(?:shot|drink)|shots page)\b") => "new-drink",
            var text when Regex.IsMatch(
                text,
                @"\b(activity|history|past shots|shot history|shots? from (?:last|this|today|yesterday))\b") => "activity",
            var text when Regex.IsMatch(text, @"\bbeans?\b") => "beans",
            var text when Regex.IsMatch(text, @"\bequipment|gear|machines?|grinders?\b") => "equipment",
            var text when Regex.IsMatch(text, @"\bprofiles?|people|baristas?\b") => "profiles",
            var text when Regex.IsMatch(text, @"\bsettings|preferences|options\b") => "settings",
            _ => null,
        };

        return destination is null
            ? null
            : new VoiceNavigationRequest(destination, Period: ParsePeriod(transcript));
    }

    static decimal? MatchDecimal(string text, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success &&
                decimal.TryParse(match.Groups["n"].Value, NumberStyles.Number, EnUs, out var value))
                return value;
        }
        return null;
    }

    static int? MatchInt(string text, params string[] patterns)
    {
        var value = MatchDecimal(text, patterns);
        return value.HasValue &&
            value.Value == decimal.Truncate(value.Value) &&
            value.Value is >= int.MinValue and <= int.MaxValue
            ? (int)value.Value
            : null;
    }

    static int? ParseRating(string transcript)
    {
        var numeric = MatchInt(
            transcript,
            $@"\brat(?:e|ing)(?:\s+(?:of|to|is))?\s*{NumberPattern}",
            $@"\b{NumberPattern}\s*(?:out of 4|stars?)\b");
        if (numeric.HasValue)
            return numeric is >= 0 and <= 4 ? numeric : null;

        if (Regex.IsMatch(transcript, @"\b(excellent|amazing|perfect)\b", RegexOptions.IgnoreCase))
            return 4;
        if (Regex.IsMatch(transcript, @"\b(pretty good|good)\b", RegexOptions.IgnoreCase))
            return 3;
        if (Regex.IsMatch(transcript, @"\b(not great|meh|okay|average)\b", RegexOptions.IgnoreCase))
            return 2;
        if (Regex.IsMatch(transcript, @"\b(bad)\b", RegexOptions.IgnoreCase))
            return 1;
        if (Regex.IsMatch(transcript, @"\b(terrible|awful)\b", RegexOptions.IgnoreCase))
            return 0;
        return null;
    }

    static string[] MissingRequiredFields(VoiceFieldUpdates updates)
    {
        var fields = new List<string>(3);
        if (!updates.DoseGrams.HasValue) fields.Add("dose");
        if (!updates.YieldGrams.HasValue) fields.Add("output");
        if (!updates.TimeSeconds.HasValue) fields.Add("time");
        return fields.ToArray();
    }

    static string ParsePeriod(string transcript)
    {
        if (Regex.IsMatch(transcript, @"\byesterday\b", RegexOptions.IgnoreCase))
            return "yesterday";
        if (Regex.IsMatch(transcript, @"\blast\s+week\b", RegexOptions.IgnoreCase))
            return "last week";
        if (Regex.IsMatch(transcript, @"\bthis\s+week\b", RegexOptions.IgnoreCase))
            return "this week";
        if (Regex.IsMatch(transcript, @"\blast\s+month\b", RegexOptions.IgnoreCase))
            return "last month";
        if (Regex.IsMatch(transcript, @"\bthis\s+month\b", RegexOptions.IgnoreCase))
            return "this month";
        if (Regex.IsMatch(transcript, @"\btoday\b", RegexOptions.IgnoreCase))
            return "today";
        return "all time";
    }

    static DateTime ParseRoastDate(string transcript)
    {
        if (Regex.IsMatch(transcript, @"\byesterday\b", RegexOptions.IgnoreCase))
            return DateTime.Today.AddDays(-1);
        if (Regex.IsMatch(transcript, @"\btoday\b", RegexOptions.IgnoreCase))
            return DateTime.Today;
        var daysAgo = Regex.Match(
            transcript,
            @"\b(?<days>\d+)\s+days?\s+ago\b",
            RegexOptions.IgnoreCase);
        if (daysAgo.Success && int.TryParse(daysAgo.Groups["days"].Value, out var days))
            return DateTime.Today.AddDays(-days);

        var dateText = Regex.Match(
            transcript,
            @"\broast(?:ed)?\s+(?<date>.+)$",
            RegexOptions.IgnoreCase).Groups["date"].Value;
        return DateTime.TryParse(dateText, EnUs, DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date.Date
            : DateTime.Today;
    }

    static string ReplaceNumberWords(string text)
    {
        var pattern = @"\b(?:(?:twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)(?:[- ](?:one|two|three|four|five|six|seven|eight|nine))?|zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen)\b";
        return Regex.Replace(text, pattern, match =>
        {
            var words = match.Value.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1)
            {
                if (SmallNumbers.TryGetValue(words[0], out var small)) return small.ToString(EnUs);
                if (Tens.TryGetValue(words[0], out var tens)) return tens.ToString(EnUs);
            }
            if (words.Length == 2 &&
                Tens.TryGetValue(words[0], out var ten) &&
                SmallNumbers.TryGetValue(words[1], out var one))
                return (ten + one).ToString(EnUs);
            return match.Value;
        }, RegexOptions.IgnoreCase);
    }

    static ParsedVoiceCommand Unknown(string transcript, string error) => new()
    {
        Intent = CommandIntent.Unknown,
        NormalizedTranscript = transcript,
        ErrorMessage = error,
    };

    static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('.', ',');

    static string JoinNaturalLanguage(IReadOnlyList<string> values) =>
        values.Count switch
        {
            0 => "",
            1 => values[0],
            2 => $"{values[0]} and {values[1]}",
            _ => $"{string.Join(", ", values.Take(values.Count - 1))}, and {values[^1]}",
        };
}
