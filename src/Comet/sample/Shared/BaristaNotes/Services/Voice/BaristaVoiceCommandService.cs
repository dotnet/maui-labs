#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services.Voice;

public sealed class BaristaVoiceCommandService : IBaristaVoiceCommandService
{
    readonly VoiceCommandParser _parser;
    readonly IBaristaVoiceCallbacks _callbacks;
    readonly IShotService _shots;
    readonly IBeanService _beans;
    readonly IBagService _bags;
    readonly IEquipmentService _equipment;
    readonly IUserProfileService _profiles;
    readonly Func<DateTime> _utcNow;

    public BaristaVoiceCommandService(
        VoiceCommandParser parser,
        IBaristaVoiceCallbacks callbacks,
        IShotService shots,
        IBeanService beans,
        IBagService bags,
        IEquipmentService equipment,
        IUserProfileService profiles,
        Func<DateTime>? utcNow = null)
    {
        _parser = parser;
        _callbacks = callbacks;
        _shots = shots;
        _beans = beans;
        _bags = bags;
        _equipment = equipment;
        _profiles = profiles;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public ParsedVoiceCommand Interpret(string transcript) => _parser.Parse(transcript);

    public async Task<VoiceToolResultDto> ProcessAsync(
        string transcript,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var command = Interpret(transcript);
            if (!string.IsNullOrEmpty(command.ErrorMessage))
                return Failure(command.ErrorMessage);

            cancellationToken.ThrowIfCancellationRequested();
            return command.Intent switch
            {
                CommandIntent.Cancel => Failure("Cancelled"),
                CommandIntent.Help => Success(
                    "Try “Log shot 18 in, 36 out, 28 seconds”, “Rate my last shot 3 stars”, or “Open activity”."),
                CommandIntent.Navigate => await NavigateAsync(command, cancellationToken),
                CommandIntent.LogShot => await ApplyOrCommitShotAsync(command, cancellationToken),
                CommandIntent.RateShot => await RateLastShotAsync(command),
                CommandIntent.AddTastingNotes => await AddTastingNotesAsync(command),
                CommandIntent.AddBean => await AddBeanAsync(command),
                CommandIntent.AddBag => await AddBagAsync(command),
                CommandIntent.AddEquipment => await AddEquipmentAsync(command),
                CommandIntent.AddProfile => await AddProfileAsync(command),
                CommandIntent.Query => await QueryAsync(command, cancellationToken),
                _ => Failure(
                    command.ErrorMessage ??
                    "I couldn't understand that command. Try logging a shot or opening a page."),
            };
        }
        catch (OperationCanceledException)
        {
            return Failure("Cancelled");
        }
        catch (Exception ex)
        {
            return Failure($"Sorry, I couldn't process that command. {ex.Message}");
        }
    }

    async Task<VoiceToolResultDto> NavigateAsync(ParsedVoiceCommand command, CancellationToken cancellationToken)
    {
        if (command.Navigation is null)
            return Failure("I couldn't find that page.");

        var nav = command.Navigation;

        // Resolve EntityName to EntityId via service lookup when needed
        if (!nav.EntityId.HasValue && !string.IsNullOrWhiteSpace(nav.EntityName))
            nav = await ResolveEntityNameAsync(nav, cancellationToken);

        if (nav.Destination == "bags" && nav.EntityId.HasValue)
        {
            var bag = await _bags.GetBagByIdAsync(nav.EntityId.Value);
            if (bag is null)
                return Failure($"I couldn't find a bag with ID {nav.EntityId.Value}.");

            var bean = await _beans.GetBeanByIdAsync(bag.BeanId);
            nav = nav with
            {
                ParentEntityId = bag.BeanId,
                ParentEntityName = bean?.Name ?? string.Empty,
            };
        }

        var navigation = await _callbacks.NavigateAsync(nav, cancellationToken);
        if (navigation.Outcome == VoiceNavigationOutcome.KeptEditing)
        {
            return Failure(
                navigation.Message ??
                "Navigation cancelled. Your unsaved changes are still open.");
        }
        if (navigation.Outcome == VoiceNavigationOutcome.Cancelled)
            return Failure(navigation.Message ?? "Navigation was cancelled.");
        if (navigation.Outcome == VoiceNavigationOutcome.DestinationUnavailable)
            return Failure(navigation.Message ?? "I couldn't open that destination.");

        var detail = nav.EntityId.HasValue ? $" (#{nav.EntityId.Value})" : "";
        return Success(
            navigation.Message ??
            $"I've opened {DisplayDestination(nav.Destination)}{detail}.");
    }

    async Task<VoiceNavigationRequest> ResolveEntityNameAsync(
        VoiceNavigationRequest nav,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (nav.Destination)
            {
                case "beans" when _beans is not null:
                    // Try exact/fuzzy first
                    var exactBean = await _beans.FuzzyFindByNameRoasterAsync(nav.EntityName!, null);
                    if (exactBean is not null)
                        return nav with { EntityId = exactBean.Id, EntityName = null };
                    // Fall back to contains match
                    var allBeans = await _beans.GetAllActiveBeansAsync();
                    var beanMatch = allBeans.FirstOrDefault(b =>
                        b.Name.Contains(nav.EntityName!, StringComparison.OrdinalIgnoreCase));
                    if (beanMatch is not null)
                        return nav with { EntityId = beanMatch.Id, EntityName = null };
                    break;
                case "equipment" when _equipment is not null:
                    var allEquip = await _equipment.GetAllActiveEquipmentAsync();
                    var equipMatch = allEquip.FirstOrDefault(e =>
                        e.Name.Contains(nav.EntityName!, StringComparison.OrdinalIgnoreCase));
                    if (equipMatch is not null)
                        return nav with { EntityId = equipMatch.Id, EntityName = null };
                    break;
                case "profiles" when _profiles is not null:
                    var allProfiles = await _profiles.GetAllProfilesAsync();
                    var profileMatch = allProfiles.FirstOrDefault(p =>
                        p.Name.Contains(nav.EntityName!, StringComparison.OrdinalIgnoreCase));
                    if (profileMatch is not null)
                        return nav with { EntityId = profileMatch.Id, EntityName = null };
                    break;
            }
        }
        catch
        {
            // Service lookup failed — fall through to management navigation
        }
        return nav;
    }

    async Task<VoiceToolResultDto> ApplyOrCommitShotAsync(
        ParsedVoiceCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.FieldUpdates.HasChanges)
            return Failure("Please provide a dose, output, or time.");

        _callbacks.ApplyNewDrinkFields(command.FieldUpdates);
        if (!command.CommitNewDrink)
            return Success(FieldUpdateMessage(command.FieldUpdates));

        return await _callbacks.CommitNewDrinkAsync(command.FieldUpdates, cancellationToken);
    }

    async Task<VoiceToolResultDto> RateLastShotAsync(ParsedVoiceCommand command)
    {
        var rating = command.FieldUpdates.Rating;
        if (!rating.HasValue || rating is < 0 or > 4)
            return Failure("Rating must be between 0 and 4.");

        var last = await _shots.GetMostRecentShotAsync();
        if (last is null)
            return Failure("No shots found to rate. Log a shot first.");

        await _shots.UpdateShotAsync(last.Id, new UpdateShotDto
        {
            Rating = rating,
            DrinkType = last.DrinkType,
        });
        return Success($"Rated your last shot {rating}/4.");
    }

    async Task<VoiceToolResultDto> AddTastingNotesAsync(ParsedVoiceCommand command)
    {
        var notes = command.FieldUpdates.TastingNotes;
        if (string.IsNullOrWhiteSpace(notes))
            return Failure("Please provide tasting notes to add.");

        var last = await _shots.GetMostRecentShotAsync();
        if (last is null)
            return Failure("No shots found to add notes to. Log a shot first.");

        var combined = string.IsNullOrWhiteSpace(last.TastingNotes)
            ? notes
            : $"{last.TastingNotes}; {notes}";
        await _shots.UpdateShotAsync(last.Id, new UpdateShotDto
        {
            Rating = last.Rating,
            DrinkType = last.DrinkType,
            TastingNotes = combined,
        });
        return Success($"Added tasting notes to your last shot: “{notes}”.");
    }

    async Task<VoiceToolResultDto> AddBeanAsync(ParsedVoiceCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Failure("Please provide a bean name.");

        var result = await _beans.CreateBeanAsync(new CreateBeanDto
        {
            Name = command.Name,
            Roaster = command.Roaster,
            Origin = command.Origin,
        });
        return result.Success
            ? Success(
                string.IsNullOrWhiteSpace(command.Roaster)
                    ? $"Added bean: {command.Name}."
                    : $"Added bean: {command.Name} from {command.Roaster}.")
            : Failure(result.ErrorMessage ?? "Failed to create bean.");
    }

    async Task<VoiceToolResultDto> AddBagAsync(ParsedVoiceCommand command)
    {
        var beans = await _beans.GetAllActiveBeansAsync();
        var bean = beans.FirstOrDefault(item =>
            item.Name.Equals(command.BeanName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(command.BeanName))
        {
            var activeBag = (await _bags.GetActiveBagsForShotLoggingAsync()).FirstOrDefault();
            bean = activeBag is null
                ? null
                : beans.FirstOrDefault(item => item.Id == activeBag.BeanId);
        }
        if (bean is null)
            return Failure(
                string.IsNullOrWhiteSpace(command.BeanName)
                    ? "No recently used bean was found. Please provide a bean name."
                    : $"Bean “{command.BeanName}” was not found. Please add it first.");

        var roastDate = command.RoastDate ?? DateTime.Today;
        var result = await _bags.CreateNewBagForBeanAsync(bean.Id, roastDate);
        return result.Success
            ? Success($"Added bag of {bean.Name} roasted {roastDate:MMM d}.")
            : Failure(result.ErrorMessage ?? "Failed to create bag.");
    }

    async Task<VoiceToolResultDto> AddEquipmentAsync(ParsedVoiceCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Failure("Please provide an equipment name.");

        var item = await _equipment.CreateEquipmentAsync(new CreateEquipmentDto
        {
            Name = command.Name,
            Type = command.EquipmentType,
        });
        return Success($"Added {EquipmentName(command.EquipmentType)}: {item.Name}.");
    }

    async Task<VoiceToolResultDto> AddProfileAsync(ParsedVoiceCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Failure("Please provide a name for the profile.");
        var profile = await _profiles.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = command.Name,
        });
        return Success($"Added profile for {profile.Name}.");
    }

    Task<VoiceToolResultDto> QueryAsync(
        ParsedVoiceCommand command,
        CancellationToken cancellationToken) =>
        command.QueryKind switch
        {
            VoiceQueryKind.LastShot => QueryLastShotAsync(),
            VoiceQueryKind.Shots => QueryShotsAsync(command, cancellationToken),
            VoiceQueryKind.Beans => QueryBeansAsync(command),
            VoiceQueryKind.Bags => QueryBagsAsync(command, cancellationToken),
            VoiceQueryKind.Equipment => QueryEquipmentAsync(command),
            VoiceQueryKind.Profiles => QueryProfilesAsync(command),
            _ => QueryShotCountAsync(command, cancellationToken),
        };

    async Task<VoiceToolResultDto> QueryLastShotAsync()
    {
        var shot = await _shots.GetMostRecentShotAsync();
        if (shot is null)
            return Failure("No shots found. Log a shot first.");

        var bean = shot.Bean?.Name ?? shot.Bag?.BeanName ?? "unknown bean";
        var details = new List<string>
        {
            $"Last shot (ID: {shot.Id}, {shot.Timestamp.ToLocalTime():MMM d 'at' h:mm tt})",
            $"{shot.DoseIn:0.#}g in → {shot.ActualOutput ?? shot.ExpectedOutput:0.#}g out",
            $"{shot.ActualTime ?? shot.ExpectedTime:0.#} seconds",
            $"grind {(shot.GrindMicrons.HasValue ? $"{shot.GrindMicrons}µm" : "not recorded")}",
            shot.DrinkType,
            bean,
        };
        if (shot.Rating.HasValue)
            details.Add($"{shot.Rating}/4");
        if (shot.Machine is not null)
            details.Add($"machine {shot.Machine.Name}");
        if (shot.Grinder is not null)
            details.Add($"grinder {shot.Grinder.Name}");
        if (shot.MadeBy is not null)
            details.Add($"made by {shot.MadeBy.Name}");
        if (shot.MadeFor is not null)
            details.Add($"made for {shot.MadeFor.Name}");
        if (!string.IsNullOrWhiteSpace(shot.TastingNotes))
            details.Add($"notes {shot.TastingNotes}");
        return Success(string.Join("; ", details) + ".");
    }

    async Task<VoiceToolResultDto> QueryShotCountAsync(
        ParsedVoiceCommand command,
        CancellationToken cancellationToken)
    {
        var shots = ApplyShotFilters(
            await LoadAllShotsAsync(cancellationToken),
            command);
        var count = shots.Count();
        var filters = DescribeShotFilters(command);
        var filterSuffix = filters.Count == 0 ? "" : $" {string.Join(" ", filters)}";
        var suffix = command.QueryPeriod is null or "all time"
            ? ""
            : $" {command.QueryPeriod}";
        if (!string.IsNullOrWhiteSpace(command.QueryMadeBy))
            return Success(
                $"{command.QueryMadeBy} has made {count} shot{(count == 1 ? "" : "s")}{suffix}.");
        return Success(
            $"You've pulled {count} shot{(count == 1 ? "" : "s")}{filterSuffix}{suffix}.");
    }

    async Task<VoiceToolResultDto> QueryShotsAsync(
        ParsedVoiceCommand command,
        CancellationToken cancellationToken)
    {
        var matches = ApplyShotFilters(
                await LoadAllShotsAsync(cancellationToken),
                command)
            .OrderByDescending(shot => shot.Timestamp)
            .Take(command.QueryLimit)
            .ToList();
        if (matches.Count == 0)
            return Success("I couldn't find any shots matching those criteria.");

        var details = matches.Select(shot =>
        {
            var bean = shot.Bean?.Name ?? shot.Bag?.BeanName ?? "unknown bean";
            var rating = shot.Rating.HasValue ? $", {shot.Rating}/4" : "";
            return $"#{shot.Id} {shot.Timestamp.ToLocalTime():MMM d}: {shot.DoseIn:0.#}g→{shot.ActualOutput ?? shot.ExpectedOutput:0.#}g, {shot.ActualTime ?? shot.ExpectedTime:0.#}s, {bean}{rating}";
        });
        return Success($"Found {matches.Count} matching shot{(matches.Count == 1 ? "" : "s")}: {string.Join("; ", details)}.");
    }

    async Task<List<ShotRecordDto>> LoadAllShotsAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var shots = new List<ShotRecordDto>();
        var pageIndex = 0;
        var totalCount = int.MaxValue;

        while (shots.Count < totalCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await _shots.GetShotHistoryAsync(pageIndex, pageSize);
            if (pageIndex == 0)
                totalCount = Math.Max(0, page.TotalCount);
            if (page.Items.Count == 0)
                break;

            shots.AddRange(page.Items);
            pageIndex++;
        }

        return shots;
    }

    IEnumerable<ShotRecordDto> ApplyShotFilters(
        IEnumerable<ShotRecordDto> source,
        ParsedVoiceCommand command)
    {
        var now = DateTime.SpecifyKind(_utcNow(), DateTimeKind.Utc);
        var shots = source;
        var (start, end) = UtcPeriodBounds(command.QueryPeriod, now);
        if (start.HasValue)
            shots = shots.Where(shot => shot.Timestamp >= start.Value);
        if (end.HasValue)
            shots = shots.Where(shot => shot.Timestamp < end.Value);

        if (!string.IsNullOrWhiteSpace(command.QueryBeanName))
        {
            shots = shots.Where(shot =>
                shot.Bean?.Name.Contains(
                    command.QueryBeanName,
                    StringComparison.OrdinalIgnoreCase) == true ||
                shot.Bag?.BeanName.Contains(
                    command.QueryBeanName,
                    StringComparison.OrdinalIgnoreCase) == true);
        }
        if (!string.IsNullOrWhiteSpace(command.QueryMadeBy))
        {
            shots = shots.Where(shot =>
                shot.MadeBy?.Name.Contains(
                    command.QueryMadeBy,
                    StringComparison.OrdinalIgnoreCase) == true);
        }
        if (!string.IsNullOrWhiteSpace(command.QueryMadeFor))
        {
            shots = shots.Where(shot =>
                shot.MadeFor?.Name.Contains(
                    command.QueryMadeFor,
                    StringComparison.OrdinalIgnoreCase) == true);
        }
        if (command.QueryMinimumRating.HasValue)
        {
            shots = shots.Where(shot =>
                shot.Rating.HasValue &&
                shot.Rating.Value >= command.QueryMinimumRating.Value);
        }

        return shots;
    }

    static List<string> DescribeShotFilters(ParsedVoiceCommand command)
    {
        var filters = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(command.QueryBeanName))
            filters.Add($"with {command.QueryBeanName}");
        if (!string.IsNullOrWhiteSpace(command.QueryMadeBy))
            filters.Add($"made by {command.QueryMadeBy}");
        if (!string.IsNullOrWhiteSpace(command.QueryMadeFor))
            filters.Add($"made for {command.QueryMadeFor}");
        if (command.QueryMinimumRating.HasValue)
            filters.Add($"rated {command.QueryMinimumRating}+ stars");
        return filters;
    }

    async Task<VoiceToolResultDto> QueryBeansAsync(ParsedVoiceCommand command)
    {
        IEnumerable<BeanDto> beans = await _beans.GetAllActiveBeansAsync();
        if (!string.IsNullOrWhiteSpace(command.QueryName))
        {
            beans = beans.Where(bean =>
                bean.Name.Contains(command.QueryName, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(command.QueryRoaster))
        {
            beans = beans.Where(bean =>
                bean.Roaster?.Contains(command.QueryRoaster, StringComparison.OrdinalIgnoreCase) == true);
        }
        if (!string.IsNullOrWhiteSpace(command.QueryOrigin))
        {
            beans = beans.Where(bean =>
                bean.Origin?.Contains(command.QueryOrigin, StringComparison.OrdinalIgnoreCase) == true);
        }

        var matches = beans.OrderBy(bean => bean.Name).ToList();
        if (command.QueryCountOnly)
            return Success($"You have {matches.Count} active bean{(matches.Count == 1 ? "" : "s")}.");
        if (matches.Count == 0)
            return Success("I couldn't find any beans matching those criteria.");

        var details = matches.Take(command.QueryLimit).Select(bean =>
            string.IsNullOrWhiteSpace(bean.Roaster)
                ? $"#{bean.Id} {bean.Name}"
                : $"#{bean.Id} {bean.Name} by {bean.Roaster}");
        return Success($"Found {matches.Count} bean{(matches.Count == 1 ? "" : "s")}: {string.Join("; ", details)}.");
    }

    async Task<VoiceToolResultDto> QueryBagsAsync(
        ParsedVoiceCommand command,
        CancellationToken cancellationToken)
    {
        var beans = await _beans.GetAllActiveBeansAsync();
        var matches = new List<BagSummaryDto>();
        foreach (var bean in beans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(command.QueryRoaster)
                && bean.Roaster?.Contains(command.QueryRoaster, StringComparison.OrdinalIgnoreCase) != true)
            {
                continue;
            }

            var summaries = await _bags.GetBagSummariesForBeanAsync(
                bean.Id,
                includeCompleted: !command.QueryActiveOnly);
            matches.AddRange(summaries
                .Where(bag => !command.QueryActiveOnly || !bag.IsComplete)
                .Where(bag => string.IsNullOrWhiteSpace(command.QueryName)
                    || bag.BeanName.Contains(command.QueryName, StringComparison.OrdinalIgnoreCase)));
        }

        if (command.QueryCountOnly)
        {
            if (command.QueryActiveOnly)
                return Success($"You have {matches.Count} active bag{(matches.Count == 1 ? "" : "s")}.");

            var activeCount = matches.Count(bag => !bag.IsComplete);
            return Success(
                $"You have {matches.Count} bag{(matches.Count == 1 ? "" : "s")} total ({activeCount} active, {matches.Count - activeCount} finished).");
        }
        if (matches.Count == 0)
            return Success("I couldn't find any bags matching those criteria.");

        var details = matches
            .OrderByDescending(bag => bag.RoastDate)
            .Take(command.QueryLimit)
            .Select(bag =>
                $"#{bag.Id} {bag.BeanName}, roasted {bag.RoastDate:MMM d} ({bag.StatusBadge})");
        return Success($"Found {matches.Count} bag{(matches.Count == 1 ? "" : "s")}: {string.Join("; ", details)}.");
    }

    async Task<VoiceToolResultDto> QueryEquipmentAsync(ParsedVoiceCommand command)
    {
        IEnumerable<EquipmentDto> equipment = await _equipment.GetAllActiveEquipmentAsync();
        if (command.QueryEquipmentType.HasValue)
            equipment = equipment.Where(item => item.Type == command.QueryEquipmentType.Value);
        if (!string.IsNullOrWhiteSpace(command.QueryName))
        {
            equipment = equipment.Where(item =>
                item.Name.Contains(command.QueryName, StringComparison.OrdinalIgnoreCase));
        }

        var matches = equipment.OrderBy(item => item.Name).ToList();
        if (command.QueryCountOnly)
            return Success($"You have {matches.Count} active equipment item{(matches.Count == 1 ? "" : "s")}.");
        if (matches.Count == 0)
            return Success("I couldn't find any equipment matching those criteria.");

        var details = matches.Take(command.QueryLimit).Select(item =>
            $"#{item.Id} {item.Name} ({EquipmentName(item.Type)})");
        return Success($"Found {matches.Count} equipment item{(matches.Count == 1 ? "" : "s")}: {string.Join("; ", details)}.");
    }

    async Task<VoiceToolResultDto> QueryProfilesAsync(ParsedVoiceCommand command)
    {
        IEnumerable<UserProfileDto> profiles = await _profiles.GetAllProfilesAsync();
        if (!string.IsNullOrWhiteSpace(command.QueryName))
        {
            profiles = profiles.Where(profile =>
                profile.Name.Contains(command.QueryName, StringComparison.OrdinalIgnoreCase));
        }

        var matches = profiles.OrderBy(profile => profile.Name).ToList();
        if (command.QueryCountOnly)
            return Success($"You have {matches.Count} profile{(matches.Count == 1 ? "" : "s")}.");
        if (matches.Count == 0)
            return Success("I couldn't find any profiles matching those criteria.");

        var details = matches.Take(command.QueryLimit).Select(profile =>
            $"#{profile.Id} {profile.Name}");
        return Success($"Found {matches.Count} profile{(matches.Count == 1 ? "" : "s")}: {string.Join("; ", details)}.");
    }

    static (DateTime? Start, DateTime? End) UtcPeriodBounds(string? period, DateTime utcNow)
    {
        var today = utcNow.Date;
        var thisWeek = today.AddDays(-(int)today.DayOfWeek);
        var thisMonth = new DateTime(
            today.Year,
            today.Month,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);
        return period switch
        {
            "today" => (today, today.AddDays(1)),
            "yesterday" => (today.AddDays(-1), today),
            "this week" => (thisWeek, thisWeek.AddDays(7)),
            "last week" => (thisWeek.AddDays(-7), thisWeek),
            "this month" => (thisMonth, thisMonth.AddMonths(1)),
            "last month" => (thisMonth.AddMonths(-1), thisMonth),
            _ => (null, null),
        };
    }

    static VoiceToolResultDto Success(string message) => new(true, message, null, null);
    static VoiceToolResultDto Failure(string message) => new(false, message, null, null);

    static string DisplayDestination(string destination) => destination switch
    {
        "new-drink" => "New Drink",
        "activity" => "Activity",
        "beans" => "Coffee Beans",
        "bags" => "Coffee Bag",
        "equipment" => "Equipment",
        "profiles" => "User Profiles",
        "settings" => "Settings",
        _ => destination,
    };

    static string EquipmentName(EquipmentType type) => type switch
    {
        EquipmentType.Machine => "machine",
        EquipmentType.Grinder => "grinder",
        EquipmentType.Tamper => "tamper",
        EquipmentType.PuckScreen => "puck screen",
        _ => "equipment",
    };

    static string FieldUpdateMessage(VoiceFieldUpdates updates)
    {
        var fields = new List<string>(6);
        if (updates.DoseGrams.HasValue) fields.Add($"dose to {updates.DoseGrams:0.#}g");
        if (updates.YieldGrams.HasValue) fields.Add($"yield to {updates.YieldGrams:0.#}g");
        if (updates.TimeSeconds.HasValue) fields.Add($"time to {updates.TimeSeconds:0.#}s");
        if (updates.GrindMicrons.HasValue) fields.Add($"grind to {updates.GrindMicrons}µm");
        if (updates.Rating.HasValue) fields.Add($"rating to {updates.Rating}/4");
        if (updates.TastingNotes is not null) fields.Add("tasting notes");
        return $"Updated {string.Join(", ", fields)}.";
    }
}
