using System;
using System.Collections.Generic;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Data;

/// <summary>
/// In-memory data store for the first app skeleton. Sufficient for CRUD on all
/// entity types without SQLite. Replace with EF Core + SQLite in Phase 4.
/// </summary>
public class InMemoryDataStore : IBaristaDataStore
{
    private readonly object _lock = new();
    private int _nextBeanId = 1;
    private int _nextBagId = 1;
    private int _nextShotId = 1;
    private int _nextEquipmentId = 1;
    private int _nextProfileId = 1;
    private int _nextRecipeId = 1;
    private int _nextGrinderProfileId = 1;
    private int _nextGrindTranslationCacheId = 1;

    public List<Bean> Beans { get; } = new();
    public List<Bag> Bags { get; } = new();
    public List<ShotRecord> Shots { get; } = new();
    public List<Equipment> Equipment { get; } = new();
    public List<UserProfile> Profiles { get; } = new();
    public List<ShotEquipment> ShotEquipments { get; } = new();
    public List<Recipe> Recipes { get; } = new();
    public List<GrinderProfile> GrinderProfiles { get; } = new();
    public List<GrindTranslationCache> GrindTranslationCache { get; } = new();
    public List<PendingAvatarCleanup> PendingAvatarCleanups { get; } = new();

    public int NextBeanId() { lock (_lock) return _nextBeanId++; }
    public int NextBagId() { lock (_lock) return _nextBagId++; }
    public int NextShotId() { lock (_lock) return _nextShotId++; }
    public int NextEquipmentId() { lock (_lock) return _nextEquipmentId++; }
    public int NextProfileId() { lock (_lock) return _nextProfileId++; }
    public int NextRecipeId() { lock (_lock) return _nextRecipeId++; }
    public int NextGrinderProfileId() { lock (_lock) return _nextGrinderProfileId++; }
    public int NextGrindTranslationCacheId() { lock (_lock) return _nextGrindTranslationCacheId++; }

    public void ExecuteMutation(Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        lock (_lock)
            mutation();
    }

    public T ExecuteMutation<T>(Func<T> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        lock (_lock)
            return mutation();
    }

    protected void SetNextIds(
        int bean,
        int bag,
        int shot,
        int equipment,
        int profile,
        int recipe,
        int grinderProfile,
        int grindTranslationCache)
    {
        lock (_lock)
        {
            _nextBeanId = bean;
            _nextBagId = bag;
            _nextShotId = shot;
            _nextEquipmentId = equipment;
            _nextProfileId = profile;
            _nextRecipeId = recipe;
            _nextGrinderProfileId = grinderProfile;
            _nextGrindTranslationCacheId = grindTranslationCache;
        }
    }

    public virtual void SaveChanges()
    {
    }

    public virtual void Dispose()
    {
    }

    /// <summary>
    /// Seeds sample data for development. Call once at startup.
    /// </summary>
    public void Seed() => ExecuteMutation(SeedCore);

    private void SeedCore()
    {
        if (Beans.Count != 0)
            return;

        var now = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        // Beans
        var bean1 = new Bean { Id = NextBeanId(), Name = "Ethiopian Yirgacheffe", Roaster = "Counter Culture", Origin = "Ethiopia", Notes = "Fruity, floral", IsActive = true, CreatedAt = now, SyncId = Guid.Parse("10000000-0000-0000-0000-000000000001"), LastModifiedAt = now };
        var bean2 = new Bean { Id = NextBeanId(), Name = "Colombia El Paraíso", Roaster = "Onyx Coffee Lab", Origin = "Colombia", Notes = "Tropical, sweet", IsActive = true, CreatedAt = now, SyncId = Guid.Parse("10000000-0000-0000-0000-000000000002"), LastModifiedAt = now };
        Beans.AddRange(new[] { bean1, bean2 });

        // Bags
        var bag1 = new Bag { Id = NextBagId(), BeanId = bean1.Id, RoastDate = now.AddDays(-7), IsActive = true, CreatedAt = now, Bean = bean1, SyncId = Guid.Parse("20000000-0000-0000-0000-000000000001"), LastModifiedAt = now };
        var bag2 = new Bag { Id = NextBagId(), BeanId = bean2.Id, RoastDate = now.AddDays(-3), IsActive = true, CreatedAt = now, Bean = bean2, SyncId = Guid.Parse("20000000-0000-0000-0000-000000000002"), LastModifiedAt = now };
        Bags.AddRange(new[] { bag1, bag2 });

        // Equipment
        var machine = new Equipment { Id = NextEquipmentId(), Name = "Breville Barista Express", Type = EquipmentType.Machine, IsActive = true, CreatedAt = now, SyncId = Guid.Parse("30000000-0000-0000-0000-000000000001"), LastModifiedAt = now };
        var grinder = new Equipment { Id = NextEquipmentId(), Name = "Turin DF64V", Type = EquipmentType.Grinder, IsActive = true, CreatedAt = now, SyncId = Guid.Parse("30000000-0000-0000-0000-000000000002"), LastModifiedAt = now };
        Equipment.AddRange(new[] { machine, grinder });

        // Profiles
        var profile = new UserProfile { Id = NextProfileId(), Name = "David", CreatedAt = now, SyncId = Guid.Parse("40000000-0000-0000-0000-000000000001"), LastModifiedAt = now };
        Profiles.Add(profile);

        // Sample shot
        var shot = new ShotRecord
        {
            Id = NextShotId(),
            Timestamp = now,
            BagId = bag1.Id,
            Bag = bag1,
            MachineId = machine.Id,
            Machine = machine,
            GrinderId = grinder.Id,
            Grinder = grinder,
            MadeById = profile.Id,
            MadeBy = profile,
            BrewMethod = BrewMethod.Espresso,
            DoseIn = 18m,
            GrindMicrons = 270,
            ExpectedTime = 28,
            ExpectedOutput = 36,
            DrinkType = "Espresso",
            ActualTime = 27,
            ActualOutput = 35,
            Rating = 3,
            TastingNotes = "Sweet, balanced",
            SyncId = Guid.Parse("50000000-0000-0000-0000-000000000001"),
            LastModifiedAt = now,
        };
        Shots.Add(shot);
        SaveChanges();
    }
}
