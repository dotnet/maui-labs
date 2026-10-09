using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

/// <summary>
/// In-memory shot service. Sufficient for create/list/edit/delete without SQLite.
/// </summary>
public sealed class InMemoryShotService : IShotService
{
    private readonly IBaristaDataStore _store;
    private readonly IDataChangeNotifier _notifier;
    private readonly IPreferencesService? _preferences;

    public InMemoryShotService(
        IBaristaDataStore store,
        IDataChangeNotifier notifier,
        IPreferencesService? preferences = null)
    {
        _store = store;
        _notifier = notifier;
        _preferences = preferences;
    }

    public Task<ShotRecordDto?> GetMostRecentShotAsync()
    {
        var shot = _store.Shots
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefault();
        return Task.FromResult(shot == null ? null : MapToDto(shot));
    }

    public Task<ShotRecordDto> CreateShotAsync(CreateShotDto dto)
    {
        ValidateCreateShot(dto);
        var shot = _store.ExecuteMutation(() =>
        {
            var bag = _store.Bags.FirstOrDefault(b => b.Id == dto.BagId && !b.IsDeleted)
                ?? throw new ArgumentException("Bag not found", nameof(dto));
            if (bag.IsComplete)
                throw new ArgumentException(
                    "Cannot log shot to a completed bag. Please reactivate the bag or select an active bag.",
                    nameof(dto));
            var created = new ShotRecord
            {
                Id = _store.NextShotId(),
                Timestamp = dto.Timestamp ?? DateTime.UtcNow,
                BagId = dto.BagId ?? 0,
                Bag = bag,
                MachineId = dto.MachineId,
                Machine = dto.MachineId.HasValue ? _store.Equipment.FirstOrDefault(e => e.Id == dto.MachineId) : null,
                GrinderId = dto.GrinderId,
                Grinder = dto.GrinderId.HasValue ? _store.Equipment.FirstOrDefault(e => e.Id == dto.GrinderId) : null,
                MadeById = dto.MadeById,
                MadeBy = dto.MadeById.HasValue ? _store.Profiles.FirstOrDefault(p => p.Id == dto.MadeById) : null,
                MadeForId = dto.MadeForId,
                MadeFor = dto.MadeForId.HasValue ? _store.Profiles.FirstOrDefault(p => p.Id == dto.MadeForId) : null,
                BrewMethod = dto.BrewMethod,
                ParametersJson = dto.ParametersJson,
                DoseIn = dto.DoseIn,
                GrindMicrons = dto.GrindMicrons,
                WaterTempC = dto.WaterTempC,
                ExpectedTime = dto.ExpectedTime,
                ExpectedOutput = dto.ExpectedOutput,
                DrinkType = dto.DrinkType,
                ActualTime = dto.ActualTime,
                ActualOutput = dto.ActualOutput,
                PreinfusionTime = dto.PreinfusionTime,
                Rating = dto.Rating,
                TastingNotes = dto.TastingNotes,
                SyncId = Guid.NewGuid(),
                LastModifiedAt = DateTime.UtcNow,
            };
            _store.Shots.Add(created);
            foreach (var accId in dto.AccessoryIds)
                _store.ShotEquipments.Add(new ShotEquipment { ShotRecordId = created.Id, EquipmentId = accId });
            SaveRecentValues(dto);
            _store.SaveChanges();
            return created;
        });
        _notifier.NotifyDataChanged(DataChangeType.ShotCreated, shot);
        return Task.FromResult(MapToDto(shot));
    }

    public Task<ShotRecordDto> UpdateShotAsync(int id, UpdateShotDto dto)
    {
        var shot = _store.ExecuteMutation(() =>
        {
            var existing = _store.Shots.FirstOrDefault(s => s.Id == id && !s.IsDeleted)
                ?? throw new KeyNotFoundException($"Shot {id} not found");
            ValidateUpdateShot(dto, existing);
            if (dto.BagId.HasValue) { existing.BagId = dto.BagId.Value; existing.Bag = _store.Bags.FirstOrDefault(b => b.Id == dto.BagId); }
            if (dto.MachineId.HasValue || dto.ClearMachine)
            {
                existing.MachineId = dto.MachineId;
                existing.Machine = _store.Equipment.FirstOrDefault(e => e.Id == dto.MachineId);
            }
            if (dto.GrinderId.HasValue || dto.ClearGrinder)
            {
                existing.GrinderId = dto.GrinderId;
                existing.Grinder = _store.Equipment.FirstOrDefault(e => e.Id == dto.GrinderId);
            }
            if (dto.MadeById.HasValue) { existing.MadeById = dto.MadeById; existing.MadeBy = _store.Profiles.FirstOrDefault(p => p.Id == dto.MadeById); }
            if (dto.MadeForId.HasValue) { existing.MadeForId = dto.MadeForId; existing.MadeFor = _store.Profiles.FirstOrDefault(p => p.Id == dto.MadeForId); }
            if (dto.AccessoryIds != null)
            {
                _store.ShotEquipments.RemoveAll(item => item.ShotRecordId == id);
                foreach (var accessoryId in dto.AccessoryIds.Distinct())
                    _store.ShotEquipments.Add(new ShotEquipment { ShotRecordId = id, EquipmentId = accessoryId });
            }
            if (dto.ActualTime.HasValue) existing.ActualTime = dto.ActualTime;
            if (dto.ActualOutput.HasValue) existing.ActualOutput = dto.ActualOutput;
            if (dto.PreinfusionTime.HasValue) existing.PreinfusionTime = dto.PreinfusionTime;
            existing.Rating = dto.Rating;
            existing.DrinkType = dto.DrinkType;
            if (dto.DoseIn.HasValue) existing.DoseIn = dto.DoseIn.Value;
            if (dto.GrindMicrons.HasValue) existing.GrindMicrons = dto.GrindMicrons;
            if (dto.WaterTempC.HasValue) existing.WaterTempC = dto.WaterTempC;
            if (dto.ExpectedTime.HasValue) existing.ExpectedTime = dto.ExpectedTime.Value;
            if (dto.ExpectedOutput.HasValue) existing.ExpectedOutput = dto.ExpectedOutput.Value;
            existing.TastingNotes = dto.TastingNotes;
            if (dto.BrewMethod.HasValue) existing.BrewMethod = dto.BrewMethod.Value;
            if (dto.ParametersJson != null) existing.ParametersJson = string.IsNullOrEmpty(dto.ParametersJson) ? null : dto.ParametersJson;
            existing.LastModifiedAt = DateTime.UtcNow;
            _store.SaveChanges();
            return existing;
        });
        _notifier.NotifyDataChanged(DataChangeType.ShotUpdated, shot);
        return Task.FromResult(MapToDto(shot));
    }

    public Task DeleteShotAsync(int id)
    {
        var shot = _store.ExecuteMutation(() =>
        {
            var existing = _store.Shots.FirstOrDefault(s => s.Id == id && !s.IsDeleted)
                ?? throw new KeyNotFoundException($"Shot {id} not found");
            existing.IsDeleted = true;
            existing.LastModifiedAt = DateTime.UtcNow;
            _store.SaveChanges();
            return existing;
        });
        _notifier.NotifyDataChanged(DataChangeType.ShotDeleted, shot);
        return Task.CompletedTask;
    }

    public Task<PagedResult<ShotRecordDto>> GetShotHistoryAsync(int pageIndex, int pageSize)
        => GetFilteredShotHistoryAsync(null, pageIndex, pageSize);

    public Task<PagedResult<ShotRecordDto>> GetShotHistoryByUserAsync(int userProfileId, int pageIndex, int pageSize) =>
        Page(_store.Shots.Where(shot =>
            !shot.IsDeleted && (shot.MadeById == userProfileId || shot.MadeForId == userProfileId)),
            pageIndex,
            pageSize,
            _store.Shots.Count(shot => !shot.IsDeleted));

    public Task<PagedResult<ShotRecordDto>> GetShotHistoryByBeanAsync(int beanId, int pageIndex, int pageSize)
    {
        var bagIds = _store.Bags.Where(bag => bag.BeanId == beanId).Select(bag => bag.Id).ToHashSet();
        return Page(
            _store.Shots.Where(shot => !shot.IsDeleted && bagIds.Contains(shot.BagId)),
            pageIndex,
            pageSize,
            _store.Shots.Count(shot => !shot.IsDeleted));
    }

    public Task<PagedResult<ShotRecordDto>> GetShotHistoryByEquipmentAsync(int equipmentId, int pageIndex, int pageSize)
    {
        var accessoryShotIds = _store.ShotEquipments
            .Where(item => item.EquipmentId == equipmentId)
            .Select(item => item.ShotRecordId)
            .ToHashSet();
        return Page(_store.Shots.Where(shot =>
            !shot.IsDeleted &&
            (shot.MachineId == equipmentId || shot.GrinderId == equipmentId || accessoryShotIds.Contains(shot.Id))),
            pageIndex,
            pageSize,
            _store.Shots.Count(shot => !shot.IsDeleted));
    }

    public Task<PagedResult<ShotRecordDto>> GetFilteredShotHistoryAsync(ShotFilterCriteriaDto? criteria, int pageIndex, int pageSize)
    {
        ValidatePage(pageIndex, pageSize);
        var query = _store.Shots.Where(s => !s.IsDeleted).AsEnumerable();

        if (criteria?.BeanIds is { Count: > 0 } beanIds)
        {
            var bagIds = _store.Bags.Where(b => beanIds.Contains(b.BeanId)).Select(b => b.Id).ToHashSet();
            query = query.Where(s => bagIds.Contains(s.BagId));
        }
        if (criteria?.MadeForIds is { Count: > 0 } madeForIds)
            query = query.Where(s => s.MadeForId.HasValue && madeForIds.Contains(s.MadeForId.Value));
        if (criteria?.Ratings is { Count: > 0 } ratings)
            query = query.Where(s => s.Rating.HasValue && ratings.Contains(s.Rating.Value));
        if (criteria?.PeriodStart is { } periodStart)
            query = query.Where(s => s.Timestamp >= periodStart);
        if (criteria?.PeriodEnd is { } periodEnd)
            query = query.Where(s => s.Timestamp < periodEnd);

        var ordered = query.OrderByDescending(s => s.Timestamp).ToList();
        var items = ordered.Skip(pageIndex * pageSize).Take(pageSize).Select(MapToDto).ToList();

        return Task.FromResult(new PagedResult<ShotRecordDto>
        {
            Items = items,
            TotalCount = ordered.Count,
            PageIndex = pageIndex,
            PageSize = pageSize,
        });
    }

    public Task<ShotRecordDto?> GetShotByIdAsync(int id)
    {
        var shot = _store.Shots.FirstOrDefault(s => s.Id == id && !s.IsDeleted);
        return Task.FromResult(shot == null ? null : MapToDto(shot));
    }

    public Task<ShotRecordDto?> GetBestRatedShotByBeanAsync(int beanId)
    {
        var bagIds = _store.Bags.Where(bag => bag.BeanId == beanId).Select(bag => bag.Id).ToHashSet();
        var shot = _store.Shots
            .Where(item => !item.IsDeleted && item.Rating.HasValue && bagIds.Contains(item.BagId))
            .OrderByDescending(item => item.Rating)
            .ThenByDescending(item => item.Timestamp)
            .FirstOrDefault();
        return Task.FromResult(shot == null ? null : MapToDto(shot));
    }

    public Task<ShotRecordDto?> GetBestRatedShotByBagAsync(int bagId)
    {
        var shot = _store.Shots
            .Where(item => !item.IsDeleted && item.Rating.HasValue && item.BagId == bagId)
            .OrderByDescending(item => item.Rating)
            .ThenByDescending(item => item.Timestamp)
            .FirstOrDefault();
        return Task.FromResult(shot == null ? null : MapToDto(shot));
    }

    public Task<List<BeanFilterOptionDto>> GetBeansWithShotsAsync()
    {
        var beanIds = _store.Shots.Where(s => !s.IsDeleted)
            .Select(s => _store.Bags.FirstOrDefault(b => b.Id == s.BagId)?.BeanId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct();
        var result = _store.Beans
            .Where(b => beanIds.Contains(b.Id) && !b.IsDeleted)
            .Select(b => new BeanFilterOptionDto { Id = b.Id, Name = b.Name })
            .ToList();
        return Task.FromResult(result);
    }

    public Task<List<UserProfileDto>> GetPeopleWithShotsAsync()
    {
        var ids = _store.Shots.Where(s => !s.IsDeleted && s.MadeForId.HasValue)
            .Select(s => s.MadeForId!.Value).Distinct();
        var result = _store.Profiles
            .Where(p => ids.Contains(p.Id) && !p.IsDeleted)
            .Select(p => new UserProfileDto { Id = p.Id, Name = p.Name, AvatarPath = p.AvatarPath, Context = p.Context, CreatedAt = p.CreatedAt })
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int?> GetMostRecentBeanIdAsync()
    {
        var shot = _store.Shots.Where(s => !s.IsDeleted).OrderByDescending(s => s.Timestamp).FirstOrDefault();
        if (shot == null) return Task.FromResult<int?>(null);
        var bag = _store.Bags.FirstOrDefault(b => b.Id == shot.BagId);
        return Task.FromResult<int?>(bag?.BeanId);
    }

    public Task<AIAdviceRequestDto?> GetShotContextForAIAsync(int shotId)
    {
        var shot = _store.Shots.FirstOrDefault(item => item.Id == shotId && !item.IsDeleted);
        if (shot is null)
            return Task.FromResult<AIAdviceRequestDto?>(null);
        var bag = _store.Bags.FirstOrDefault(item => item.Id == shot.BagId);
        var bean = bag is null ? null : _store.Beans.FirstOrDefault(item => item.Id == bag.BeanId && !item.IsDeleted);
        if (bag is null || bean is null)
            return Task.FromResult<AIAdviceRequestDto?>(null);

        var historical = _store.Shots
            .Where(item => item.Id != shotId && item.BagId == bag.Id && !item.IsDeleted)
            .OrderByDescending(item => item.Rating ?? -1)
            .ThenByDescending(item => item.Timestamp)
            .Take(10)
            .Select(MapContext)
            .ToList();
        var madeFor = shot.MadeForId.HasValue
            ? _store.Profiles.FirstOrDefault(profile => profile.Id == shot.MadeForId && !profile.IsDeleted)
            : null;
        return Task.FromResult<AIAdviceRequestDto?>(new AIAdviceRequestDto
        {
            ShotId = shot.Id,
            CurrentShot = MapContext(shot),
            HistoricalShots = historical,
            BeanInfo = new BeanContextDto
            {
                Name = bean.Name,
                Roaster = bean.Roaster,
                Origin = bean.Origin,
                RoastDate = bag.RoastDate,
                DaysFromRoast = Math.Max(0, (DateTime.Today - bag.RoastDate.Date).Days),
                Notes = bean.Notes
            },
            Equipment = new EquipmentContextDto
            {
                MachineName = _store.Equipment.FirstOrDefault(item => item.Id == shot.MachineId)?.Name,
                GrinderName = _store.Equipment.FirstOrDefault(item => item.Id == shot.GrinderId)?.Name
            },
            MadeFor = madeFor is null ? null : new UserProfileDto
            {
                Id = madeFor.Id,
                Name = madeFor.Name,
                AvatarPath = madeFor.AvatarPath,
                Context = madeFor.Context,
                CreatedAt = madeFor.CreatedAt
            }
        });
    }

    public Task<bool> BeanHasHistoryAsync(int beanId)
    {
        var bagIds = _store.Bags.Where(bag => bag.BeanId == beanId).Select(bag => bag.Id).ToHashSet();
        return Task.FromResult(_store.Shots.Any(shot => !shot.IsDeleted && bagIds.Contains(shot.BagId)));
    }

    public Task<BeanRecommendationContextDto?> GetBeanRecommendationContextAsync(int beanId)
    {
        var bean = _store.Beans.FirstOrDefault(item => item.Id == beanId && !item.IsDeleted);
        if (bean is null)
            return Task.FromResult<BeanRecommendationContextDto?>(null);
        var bags = _store.Bags.Where(item => item.BeanId == beanId).ToList();
        var bagIds = bags.Select(item => item.Id).ToHashSet();
        var historical = _store.Shots
            .Where(item => bagIds.Contains(item.BagId) && !item.IsDeleted)
            .OrderByDescending(item => item.Rating ?? -1)
            .ThenByDescending(item => item.Timestamp)
            .Take(10)
            .Select(MapContext)
            .ToList();
        var latestBag = bags.Where(item => !item.IsDeleted).OrderByDescending(item => item.RoastDate).FirstOrDefault();
        var latestShot = _store.Shots
            .Where(item => bagIds.Contains(item.BagId) && !item.IsDeleted)
            .OrderByDescending(item => item.Timestamp)
            .FirstOrDefault();
        return Task.FromResult<BeanRecommendationContextDto?>(new BeanRecommendationContextDto
        {
            BeanId = bean.Id,
            BeanName = bean.Name,
            Roaster = bean.Roaster,
            Origin = bean.Origin,
            Notes = bean.Notes,
            RoastDate = latestBag?.RoastDate,
            DaysFromRoast = latestBag is null ? null : Math.Max(0, (DateTime.Today - latestBag.RoastDate.Date).Days),
            HasHistory = historical.Count != 0,
            HistoricalShots = historical,
            Equipment = latestShot is null ? null : new EquipmentContextDto
            {
                MachineName = _store.Equipment.FirstOrDefault(item => item.Id == latestShot.MachineId)?.Name,
                GrinderName = _store.Equipment.FirstOrDefault(item => item.Id == latestShot.GrinderId)?.Name
            }
        });
    }

    private Task<PagedResult<ShotRecordDto>> Page(
        IEnumerable<ShotRecord> query,
        int pageIndex,
        int pageSize,
        int? totalCount = null)
    {
        ValidatePage(pageIndex, pageSize);
        var ordered = query.OrderByDescending(shot => shot.Timestamp).ToList();
        return Task.FromResult(new PagedResult<ShotRecordDto>
        {
            Items = ordered.Skip(pageIndex * pageSize).Take(pageSize).Select(MapToDto).ToList(),
            TotalCount = totalCount ?? ordered.Count,
            PageIndex = pageIndex,
            PageSize = pageSize
        });
    }

    private static void ValidatePage(int pageIndex, int pageSize)
    {
        if (pageIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
    }

    private static ShotContextDto MapContext(ShotRecord shot) => new()
    {
        DoseIn = shot.DoseIn,
        ActualOutput = shot.ActualOutput,
        ActualTime = shot.ActualTime,
        GrindMicrons = shot.GrindMicrons,
        Rating = shot.Rating,
        TastingNotes = shot.TastingNotes,
        DrinkType = shot.DrinkType,
        BrewMethod = shot.BrewMethod,
        Timestamp = shot.Timestamp
    };

    private static void ValidateCreateShot(CreateShotDto dto)
    {
        var dose = BrewMethodValueRangeCatalog.GetDefinition(dto.BrewMethod, DrinkValueMetric.DoseIn).HardRange;
        var time = BrewMethodValueRangeCatalog.GetDefinition(dto.BrewMethod, DrinkValueMetric.Time).HardRange;
        var output = BrewMethodValueRangeCatalog.GetDefinition(dto.BrewMethod, DrinkValueMetric.Yield).HardRange;
        var grind = BrewMethodValueRangeCatalog.GetDefinition(dto.BrewMethod, DrinkValueMetric.GrindMicrons).HardRange;
        if (!dose.Contains(dto.DoseIn)) throw new ArgumentOutOfRangeException(nameof(dto.DoseIn));
        if (!time.Contains(dto.ExpectedTime)) throw new ArgumentOutOfRangeException(nameof(dto.ExpectedTime));
        if (!output.Contains(dto.ExpectedOutput)) throw new ArgumentOutOfRangeException(nameof(dto.ExpectedOutput));
        if (dto.GrindMicrons.HasValue && !grind.Contains(dto.GrindMicrons.Value))
            throw new ArgumentOutOfRangeException(nameof(dto.GrindMicrons));
        if (string.IsNullOrWhiteSpace(dto.DrinkType)) throw new ArgumentException("Drink type is required", nameof(dto));
        if (dto.Rating is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(dto.Rating));
    }

    private static void ValidateUpdateShot(UpdateShotDto dto, ShotRecord existing)
    {
        if (dto.ClearMachine && dto.MachineId.HasValue)
            throw new ArgumentException("Cannot set and clear the machine in the same update.", nameof(dto));
        if (dto.ClearGrinder && dto.GrinderId.HasValue)
            throw new ArgumentException("Cannot set and clear the grinder in the same update.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.DrinkType))
            throw new ArgumentException("Drink type is required", nameof(dto));
        if (dto.Rating is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(dto.Rating));
        if (dto.PreinfusionTime is < 0 or > 60)
            throw new ArgumentOutOfRangeException(nameof(dto.PreinfusionTime));

        var method = dto.BrewMethod ?? existing.BrewMethod;
        var methodChanged = dto.BrewMethod.HasValue && dto.BrewMethod.Value != existing.BrewMethod;
        var dose = dto.DoseIn ?? (methodChanged ? existing.DoseIn : null);
        var time = dto.ExpectedTime ?? (methodChanged ? existing.ExpectedTime : null);
        var output = dto.ExpectedOutput ?? (methodChanged ? existing.ExpectedOutput : null);
        var grind = dto.GrindMicrons ?? (methodChanged ? existing.GrindMicrons : null);
        var actualTime = dto.ActualTime ?? (methodChanged ? existing.ActualTime : null);
        var actualOutput = dto.ActualOutput ?? (methodChanged ? existing.ActualOutput : null);
        var timeRange = BrewMethodValueRangeCatalog.GetDefinition(method, DrinkValueMetric.Time).HardRange;
        var outputRange = BrewMethodValueRangeCatalog.GetDefinition(method, DrinkValueMetric.Yield).HardRange;
        if (dose.HasValue && !BrewMethodValueRangeCatalog.GetDefinition(method, DrinkValueMetric.DoseIn).HardRange.Contains(dose.Value))
            throw new ArgumentOutOfRangeException(nameof(dto.DoseIn));
        if (time.HasValue && !timeRange.Contains(time.Value))
            throw new ArgumentOutOfRangeException(nameof(dto.ExpectedTime));
        if (output.HasValue && !outputRange.Contains(output.Value))
            throw new ArgumentOutOfRangeException(nameof(dto.ExpectedOutput));
        if (grind.HasValue && !BrewMethodValueRangeCatalog.GetDefinition(method, DrinkValueMetric.GrindMicrons).HardRange.Contains(grind.Value))
            throw new ArgumentOutOfRangeException(nameof(dto.GrindMicrons));
        if (actualTime.HasValue && (actualTime <= 0 || actualTime > timeRange.Maximum))
            throw new ArgumentOutOfRangeException(nameof(dto.ActualTime));
        if (actualOutput.HasValue && (actualOutput <= 0 || actualOutput > outputRange.Maximum))
            throw new ArgumentOutOfRangeException(nameof(dto.ActualOutput));
    }

    private void SaveRecentValues(CreateShotDto dto)
    {
        if (_preferences is null)
            return;
        _preferences.SetLastDrinkType(dto.DrinkType);
        _preferences.SetLastBagId(dto.BagId);
        _preferences.SetLastBeanId(_store.Bags.FirstOrDefault(bag => bag.Id == dto.BagId)?.BeanId);
        _preferences.SetLastMachineId(dto.MachineId);
        _preferences.SetLastGrinderId(dto.GrinderId);
        _preferences.SetLastAccessoryIds(dto.AccessoryIds);
        _preferences.SetLastMadeById(dto.MadeById);
        _preferences.SetLastMadeForId(dto.MadeForId);
        _preferences.SetLastDoseIn(dto.DoseIn);
        _preferences.SetLastGrindMicrons(dto.GrindMicrons);
        _preferences.SetLastExpectedTime(dto.ExpectedTime);
        _preferences.SetLastExpectedOutput(dto.ExpectedOutput);
        _preferences.SetLastPreinfusionTime(dto.PreinfusionTime);
    }

    private ShotRecordDto MapToDto(ShotRecord s)
    {
        var bag = s.Bag ?? _store.Bags.FirstOrDefault(b => b.Id == s.BagId);
        var bean = bag?.Bean ?? (bag != null ? _store.Beans.FirstOrDefault(b => b.Id == bag.BeanId) : null);
        var accessories = _store.ShotEquipments
            .Where(se => se.ShotRecordId == s.Id)
            .Select(se => _store.Equipment.FirstOrDefault(e => e.Id == se.EquipmentId))
            .Where(e => e != null)
            .Select(e => new EquipmentDto { Id = e!.Id, Name = e.Name, Type = e.Type, Notes = e.Notes, IsActive = e.IsActive, CreatedAt = e.CreatedAt })
            .ToList();

        return new ShotRecordDto
        {
            Id = s.Id,
            Timestamp = s.Timestamp,
            Bean = bean == null ? null : new BeanDto { Id = bean.Id, Name = bean.Name, Roaster = bean.Roaster, Origin = bean.Origin, Notes = bean.Notes, RoasterUrl = bean.RoasterUrl, IsActive = bean.IsActive, CreatedAt = bean.CreatedAt },
            Bag = bag == null ? null : new BagSummaryDto { Id = bag.Id, BeanId = bag.BeanId, BeanName = bean?.Name ?? "", RoastDate = bag.RoastDate, Notes = bag.Notes, IsComplete = bag.IsComplete },
            Machine = s.Machine == null ? null : new EquipmentDto { Id = s.Machine.Id, Name = s.Machine.Name, Type = s.Machine.Type },
            Grinder = s.Grinder == null ? null : new EquipmentDto { Id = s.Grinder.Id, Name = s.Grinder.Name, Type = s.Grinder.Type },
            Accessories = accessories,
            MadeBy = s.MadeBy == null ? null : new UserProfileDto { Id = s.MadeBy.Id, Name = s.MadeBy.Name },
            MadeFor = s.MadeFor == null ? null : new UserProfileDto { Id = s.MadeFor.Id, Name = s.MadeFor.Name },
            BrewMethod = s.BrewMethod,
            ParametersJson = s.ParametersJson,
            DoseIn = s.DoseIn,
            GrindMicrons = s.GrindMicrons,
            WaterTempC = s.WaterTempC,
            ExpectedTime = s.ExpectedTime,
            ExpectedOutput = s.ExpectedOutput,
            DrinkType = s.DrinkType,
            ActualTime = s.ActualTime,
            ActualOutput = s.ActualOutput,
            PreinfusionTime = s.PreinfusionTime,
            Rating = s.Rating,
            TastingNotes = s.TastingNotes,
        };
    }
}
