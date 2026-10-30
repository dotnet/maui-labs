using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;
using CometBaristaNotes.Services.Recipes;

namespace CometBaristaNotes.Services;

public sealed class InMemoryBeanService : IBeanService
{
    private readonly IBaristaDataStore _store;
    private readonly IDataChangeNotifier _notifier;
    private readonly IRatingService _ratingService;
    private readonly IRecipeSourcingService? _recipeSourcingService;
    private readonly IRecipeSourcingLogger _recipeSourcingLogger;

    public InMemoryBeanService(
        IBaristaDataStore store,
        IDataChangeNotifier notifier,
        IRatingService ratingService,
        IRecipeSourcingService? recipeSourcingService = null,
        IRecipeSourcingLogger? recipeSourcingLogger = null)
    {
        _store = store;
        _notifier = notifier;
        _ratingService = ratingService;
        _recipeSourcingService = recipeSourcingService;
        _recipeSourcingLogger =
            recipeSourcingLogger ?? ConsoleRecipeSourcingLogger.Instance;
    }

    public Task<List<BeanDto>> GetAllActiveBeansAsync()
        => Task.FromResult(_store.Beans
            .Where(b => b.IsActive && !b.IsDeleted)
            .OrderBy(b => b.Name)
            .Select(MapToDto)
            .ToList());

    public Task<BeanDto?> GetBeanByIdAsync(int id)
    {
        var bean = _store.Beans.FirstOrDefault(b => b.Id == id && !b.IsDeleted);
        return Task.FromResult(bean == null ? null : MapToDto(bean));
    }

    public async Task<BeanDto?> GetBeanWithRatingsAsync(int id)
    {
        var bean = _store.Beans.FirstOrDefault(b => b.Id == id && !b.IsDeleted);
        if (bean == null) return null;
        var ratings = await _ratingService.GetBeanRatingAsync(id);
        return MapToDto(bean) with { RatingAggregate = ratings };
    }

    public Task<OperationResult<BeanDto>> CreateBeanAsync(CreateBeanDto dto)
    {
        var error = ValidateCreateBean(dto);
        if (error != null)
            return Task.FromResult(OperationResult<BeanDto>.Fail(error));

        var bean = _store.ExecuteMutation(() =>
        {
            var created = new Bean
            {
                Id = _store.NextBeanId(),
                Name = dto.Name.Trim(),
                Roaster = dto.Roaster?.Trim(),
                Origin = dto.Origin?.Trim(),
                Notes = dto.Notes?.Trim(),
                RoasterUrl = dto.RoasterUrl?.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                SyncId = Guid.NewGuid(),
                LastModifiedAt = DateTime.UtcNow,
            };
            _store.Beans.Add(created);
            _store.SaveChanges();
            return created;
        });
        _notifier.NotifyDataChanged(DataChangeType.BeanCreated, bean);
        QueueRecipeSourcing(bean.Id);
        return Task.FromResult(
            OperationResult<BeanDto>.Ok(MapToDto(bean), $"{dto.Name} saved successfully"));
    }

    public Task<BeanDto> UpdateBeanAsync(int id, UpdateBeanDto dto)
    {
        var bean = _store.ExecuteMutation(() =>
        {
            var existing = _store.Beans.FirstOrDefault(b => b.Id == id && !b.IsDeleted)
                ?? throw new KeyNotFoundException($"Bean {id} not found");
            if (dto.Name != null) existing.Name = dto.Name.Trim();
            if (dto.Roaster != null) existing.Roaster = dto.Roaster.Trim();
            if (dto.Origin != null) existing.Origin = dto.Origin.Trim();
            if (dto.Notes != null) existing.Notes = dto.Notes.Trim();
            if (dto.RoasterUrl != null) existing.RoasterUrl = string.IsNullOrEmpty(dto.RoasterUrl) ? null : dto.RoasterUrl.Trim();
            if (dto.IsActive.HasValue) existing.IsActive = dto.IsActive.Value;
            existing.LastModifiedAt = DateTime.UtcNow;
            _store.SaveChanges();
            return existing;
        });
        _notifier.NotifyDataChanged(DataChangeType.BeanUpdated, bean);
        return Task.FromResult(MapToDto(bean));
    }

    public Task ArchiveBeanAsync(int id)
    {
        var bean = _store.ExecuteMutation(() =>
        {
            var existing = _store.Beans.FirstOrDefault(b => b.Id == id && !b.IsDeleted)
                ?? throw new KeyNotFoundException($"Bean {id} not found");
            existing.IsActive = false;
            existing.LastModifiedAt = DateTime.UtcNow;
            _store.SaveChanges();
            return existing;
        });
        _notifier.NotifyDataChanged(DataChangeType.BeanUpdated, bean);
        return Task.CompletedTask;
    }

    public Task DeleteBeanAsync(int id)
    {
        _store.ExecuteMutation(() =>
        {
            var bean = _store.Beans.FirstOrDefault(b => b.Id == id && !b.IsDeleted)
                ?? throw new KeyNotFoundException($"Bean {id} not found");
            bean.IsDeleted = true;
            bean.LastModifiedAt = DateTime.UtcNow;
            _store.SaveChanges();
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BeanDto>> GetRecentBeansAsync(int limit = 6, int withinDays = 90)
    {
        if (limit <= 0)
            return Task.FromResult<IReadOnlyList<BeanDto>>(Array.Empty<BeanDto>());

        var cutoff = DateTime.UtcNow.AddDays(-Math.Abs(withinDays));

        var result = _store.Beans
            .Where(b => b.IsActive && !b.IsDeleted)
            .Select(b =>
            {
                var bags = _store.Bags.Where(bag => bag.BeanId == b.Id && !bag.IsDeleted).ToList();
                DateTime? lastBag = bags.Select(bag => (DateTime?)bag.CreatedAt).DefaultIfEmpty(null).Max();
                DateTime? lastShot = bags
                    .SelectMany(bag => _store.Shots.Where(s => s.BagId == bag.Id && !s.IsDeleted))
                    .Select(s => (DateTime?)s.Timestamp)
                    .DefaultIfEmpty(null).Max();

                DateTime? lastActivity = lastBag.HasValue && lastShot.HasValue
                    ? (lastBag.Value > lastShot.Value ? lastBag : lastShot)
                    : lastBag ?? lastShot;

                return (Bean: b, LastActivity: lastActivity);
            })
            .Where(x => x.LastActivity.HasValue && x.LastActivity.Value >= cutoff)
            .OrderByDescending(x => x.LastActivity!.Value)
            .Take(limit)
            .Select(x => MapToDto(x.Bean))
            .ToList();

        return Task.FromResult<IReadOnlyList<BeanDto>>(result);
    }

    public Task<BeanDto?> FuzzyFindByNameRoasterAsync(string name, string? roaster)
    {
        var normalizedName = Normalize(name);
        if (string.IsNullOrEmpty(normalizedName))
            return Task.FromResult<BeanDto?>(null);

        var normalizedRoaster = Normalize(roaster);
        var hasRoaster = !string.IsNullOrEmpty(normalizedRoaster);
        var candidates = _store.Beans.Where(b => !b.IsDeleted).ToList();

        bool RoasterMatches(Bean b)
        {
            if (!hasRoaster) return true;
            return Normalize(b.Roaster) == normalizedRoaster;
        }

        // Exact normalized name match
        var exact = candidates.FirstOrDefault(b =>
            Normalize(b.Name) == normalizedName && RoasterMatches(b));
        if (exact != null)
            return Task.FromResult<BeanDto?>(MapToDto(exact));

        // Near match (Levenshtein ≤ 2) only when roaster provided
        if (hasRoaster)
        {
            Bean? best = null;
            var bestDist = int.MaxValue;
            foreach (var b in candidates)
            {
                if (!RoasterMatches(b)) continue;
                var bName = Normalize(b.Name);
                if (string.IsNullOrEmpty(bName)) continue;
                var d = Levenshtein(normalizedName, bName);
                if (d <= 2 && d < bestDist) { best = b; bestDist = d; }
            }
            if (best != null)
                return Task.FromResult<BeanDto?>(MapToDto(best));
        }

        return Task.FromResult<BeanDto?>(null);
    }

    public Task<IReadOnlyList<string>> GetDistinctRoastersAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = _store.Beans.Where(b => !b.IsDeleted)
            .Select(b => b.Roaster)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    public Task<IReadOnlyList<string>> GetDistinctOriginsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = _store.Beans.Where(b => !b.IsDeleted)
            .Select(b => b.Origin)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    public Task<RecipeSourcingResult> RefreshRecipesAsync(int beanId, CancellationToken ct = default) =>
        _recipeSourcingService?.SourceRecipesAsync(beanId, ct)
        ?? Task.FromResult(new RecipeSourcingResult
        {
            Status = RecipeSourcingStatus.Unavailable,
            ErrorCode = RecipeSourcingErrors.Unavailable,
            ErrorMessage = "Recipe sourcing is not configured."
        });

    private void QueueRecipeSourcing(int beanId)
    {
        if (_recipeSourcingService is null)
            return;

        _ = Task.Run(() => SourceRecipesBestEffortAsync(beanId));
    }

    private async Task SourceRecipesBestEffortAsync(int beanId)
    {
        try
        {
            var result = await _recipeSourcingService!.SourceRecipesAsync(beanId);
            if (result.Status is RecipeSourcingStatus.Cancelled
                or RecipeSourcingStatus.Unavailable
                or RecipeSourcingStatus.Failed)
            {
                _recipeSourcingLogger.LogFailure(
                    beanId,
                    result.Source ?? "post-bean-create",
                    result.Status,
                    result.ErrorMessage ?? "Post-create recipe sourcing failed.");
            }
        }
        catch (Exception exception)
        {
            _recipeSourcingLogger.LogFailure(
                beanId,
                "post-bean-create",
                RecipeSourcingStatus.Failed,
                "Unexpected post-create recipe sourcing failure.",
                exception);
        }
    }

    private static BeanDto MapToDto(Bean b) => new()
    {
        Id = b.Id, Name = b.Name, Roaster = b.Roaster, Origin = b.Origin,
        Notes = b.Notes, RoasterUrl = b.RoasterUrl, IsActive = b.IsActive, CreatedAt = b.CreatedAt,
    };

    private static string? ValidateCreateBean(CreateBeanDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required";
        if (dto.Name.Length > 100) return "Name must be 100 characters or less";
        if (dto.Roaster?.Length > 100) return "Roaster must be 100 characters or less";
        if (dto.Origin?.Length > 100) return "Origin must be 100 characters or less";
        if (dto.Notes?.Length > 500) return "Notes must be 500 characters or less";
        if (dto.RoasterUrl?.Length > 500) return "Roaster URL must be 500 characters or less";
        if (dto.RoastDate.HasValue && dto.RoastDate.Value > DateTime.UtcNow) return "Roast date cannot be in the future";
        return null;
    }

    private static string? Normalize(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToLowerInvariant();

    internal static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }
}
