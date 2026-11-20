using System;
using System.Collections.Generic;
using CometBaristaNotes.Models;

namespace CometBaristaNotes.Data;

public interface IBaristaDataStore : IDisposable
{
    List<Bean> Beans { get; }
    List<Bag> Bags { get; }
    List<ShotRecord> Shots { get; }
    List<Equipment> Equipment { get; }
    List<UserProfile> Profiles { get; }
    List<ShotEquipment> ShotEquipments { get; }
    List<Recipe> Recipes { get; }
    List<GrinderProfile> GrinderProfiles { get; }
    List<GrindTranslationCache> GrindTranslationCache { get; }
    List<PendingAvatarCleanup> PendingAvatarCleanups { get; }

    int NextBeanId();
    int NextBagId();
    int NextShotId();
    int NextEquipmentId();
    int NextProfileId();
    int NextRecipeId();
    int NextGrinderProfileId();
    int NextGrindTranslationCacheId();

    void ExecuteMutation(Action mutation);
    T ExecuteMutation<T>(Func<T> mutation);
    void Seed();
    void SaveChanges();
}
