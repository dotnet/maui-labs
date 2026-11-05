using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;

// Alias to avoid collision with mirror enums in BaristaNotesContractTests.cs
using EquipmentTypeReal = CometBaristaNotes.Models.Enums.EquipmentType;
using DataChangeTypeReal = CometBaristaNotes.Models.Enums.DataChangeType;
using BrewMethodReal = CometBaristaNotes.Models.Enums.BrewMethod;
using DrinkValueMetricReal = CometBaristaNotes.Models.Enums.DrinkValueMetric;
using ValueRangeModeReal = CometBaristaNotes.Models.Enums.ValueRangeMode;
using ValueRangeSourceReal = CometBaristaNotes.Models.Enums.ValueRangeSource;
using BagReal = CometBaristaNotes.Models.Bag;
using RecipeReal = CometBaristaNotes.Models.Recipe;
using ShotEquipmentReal = CometBaristaNotes.Models.ShotEquipment;

namespace Comet.Tests.BaristaNotes.Domain;

#region Test helpers

internal static class TestFactory
{
    public static (InMemoryDataStore Store, DataChangeNotifier Notifier, InMemoryRatingService Ratings) CreateCore()
    {
        var store = new InMemoryDataStore();
        var notifier = new DataChangeNotifier();
        var ratings = new InMemoryRatingService(store);
        return (store, notifier, ratings);
    }

    public static InMemoryBeanService BeanService(InMemoryDataStore store, DataChangeNotifier notifier, InMemoryRatingService ratings)
        => new(store, notifier, ratings);

    public static InMemoryBagService BagService(InMemoryDataStore store, DataChangeNotifier notifier)
        => new(store, notifier);

    public static InMemoryEquipmentService EquipmentService(InMemoryDataStore store, DataChangeNotifier notifier)
        => new(store, notifier);

    public static InMemoryUserProfileService ProfileService(InMemoryDataStore store, DataChangeNotifier notifier)
        => new(store, notifier, new RecordingImageProcessingService());
}

#endregion

#region Bean CRUD + Validation Tests

public class BeanServiceTests
{
    [Fact]
    public async Task CreateBean_ValidDto_ReturnsSuccess()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var result = await svc.CreateBeanAsync(new CreateBeanDto { Name = "Test Bean", Roaster = "Test Roaster" });
        Assert.True(result.Success);
        Assert.Equal("Test Bean", result.Data!.Name);
        Assert.Equal("Test Roaster", result.Data.Roaster);
        Assert.True(result.Data.IsActive);
    }

    [Fact]
    public async Task CreateBean_EmptyName_Fails()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var result = await svc.CreateBeanAsync(new CreateBeanDto { Name = " " });
        Assert.False(result.Success);
        Assert.Contains("Name is required", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateBean_NameTooLong_Fails()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var result = await svc.CreateBeanAsync(new CreateBeanDto { Name = new string('x', 101) });
        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateBean_NotesTooLong_Fails()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var result = await svc.CreateBeanAsync(new CreateBeanDto { Name = "OK", Notes = new string('x', 501) });
        Assert.False(result.Success);
    }

    [Fact]
    public async Task UpdateBean_ChangesName()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var created = (await svc.CreateBeanAsync(new CreateBeanDto { Name = "Before" })).Data!;
        var updated = await svc.UpdateBeanAsync(created.Id, new UpdateBeanDto { Name = "After" });
        Assert.Equal("After", updated.Name);
    }

    [Fact]
    public async Task ArchiveBean_SetsInactive()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var created = (await svc.CreateBeanAsync(new CreateBeanDto { Name = "Bean" })).Data!;
        await svc.ArchiveBeanAsync(created.Id);
        var fetched = await svc.GetBeanByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.False(fetched!.IsActive);
    }

    [Fact]
    public async Task ArchiveBean_ExcludedFromActiveList()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var created = (await svc.CreateBeanAsync(new CreateBeanDto { Name = "Bean" })).Data!;
        await svc.ArchiveBeanAsync(created.Id);
        var active = await svc.GetAllActiveBeansAsync();
        Assert.DoesNotContain(active, b => b.Id == created.Id);
    }

    [Fact]
    public async Task GetAllActiveBeans_OrdersByName()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        await svc.CreateBeanAsync(new CreateBeanDto { Name = "Zulu" });
        await svc.CreateBeanAsync(new CreateBeanDto { Name = "Alpha" });
        await svc.CreateBeanAsync(new CreateBeanDto { Name = "Middle" });

        var active = await svc.GetAllActiveBeansAsync();

        Assert.Equal(new[] { "Alpha", "Middle", "Zulu" }, active.Select(bean => bean.Name));
    }

    [Fact]
    public async Task DeleteBean_SoftDeletes()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var created = (await svc.CreateBeanAsync(new CreateBeanDto { Name = "Bean" })).Data!;
        await svc.DeleteBeanAsync(created.Id);
        var fetched = await svc.GetBeanByIdAsync(created.Id);
        Assert.Null(fetched);
    }

    [Fact]
    public async Task GetBeanWithRatings_ReturnsAggregate()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        store.Seed();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var beans = await svc.GetAllActiveBeansAsync();
        var first = beans.First();
        var withRatings = await svc.GetBeanWithRatingsAsync(first.Id);
        Assert.NotNull(withRatings);
        Assert.NotNull(withRatings!.RatingAggregate);
    }

    [Fact]
    public async Task GetDistinctRoasters_ReturnsAlphabetical()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        await svc.CreateBeanAsync(new CreateBeanDto { Name = "A", Roaster = "Zeta" });
        await svc.CreateBeanAsync(new CreateBeanDto { Name = "B", Roaster = "Alpha" });
        await svc.CreateBeanAsync(new CreateBeanDto { Name = "C", Roaster = "Alpha" }); // dup
        var roasters = await svc.GetDistinctRoastersAsync();
        Assert.Equal(2, roasters.Count);
        Assert.Equal("Alpha", roasters[0]);
        Assert.Equal("Zeta", roasters[1]);
    }

    [Fact]
    public async Task GetDistinctOrigins_ExcludesDeletedBeans()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var result = await svc.CreateBeanAsync(new CreateBeanDto { Name = "A", Origin = "Kenya" });
        await svc.DeleteBeanAsync(result.Data!.Id);
        var origins = await svc.GetDistinctOriginsAsync();
        Assert.Empty(origins);
    }

    [Fact]
    public async Task FuzzyFind_ExactMatch()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        await svc.CreateBeanAsync(new CreateBeanDto { Name = "Ethiopian Yirgacheffe", Roaster = "Counter Culture" });
        var found = await svc.FuzzyFindByNameRoasterAsync("Ethiopian Yirgacheffe", null);
        Assert.NotNull(found);
        Assert.Equal("Ethiopian Yirgacheffe", found!.Name);
    }

    [Fact]
    public async Task FuzzyFind_NearMatch_WithRoaster()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        await svc.CreateBeanAsync(new CreateBeanDto { Name = "Ethiopian Yirgacheffe", Roaster = "Counter Culture" });
        // "Yirgachefe" vs "Yirgacheffe" — Levenshtein 1
        var found = await svc.FuzzyFindByNameRoasterAsync("Ethiopian Yirgachefe", "Counter Culture");
        Assert.NotNull(found);
    }

    [Fact]
    public async Task FuzzyFind_NoMatch_WrongRoaster()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        await svc.CreateBeanAsync(new CreateBeanDto { Name = "Ethiopian Yirgacheffe", Roaster = "Counter Culture" });
        var found = await svc.FuzzyFindByNameRoasterAsync("Ethiopian Yirgachefe", "Wrong Roaster");
        Assert.Null(found);
    }

    [Fact]
    public async Task FuzzyFind_EmptyName_ReturnsNull()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var found = await svc.FuzzyFindByNameRoasterAsync("", null);
        Assert.Null(found);
    }
}

#endregion

#region Bean RecentBeans (activity-based ordering)

public class BeanRecentActivityTests
{
    [Fact]
    public async Task GetRecentBeans_OrdersByBagAndShotActivity()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        store.Seed(); // seeds 2 beans with bags and 1 shot
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var recent = await svc.GetRecentBeansAsync(10, 365);
        Assert.NotEmpty(recent);
        // The bean with the shot should appear (it has recent activity)
        Assert.True(recent.Count >= 1);
    }

    [Fact]
    public async Task GetRecentBeans_LimitZero_ReturnsEmpty()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        store.Seed();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var recent = await svc.GetRecentBeansAsync(0);
        Assert.Empty(recent);
    }
}

#endregion

#region Bag CRUD + Validation Tests

public class BagServiceTests
{
    [Fact]
    public async Task CreateBag_ValidBean_ReturnsSuccess()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        store.Seed();
        var bagSvc = TestFactory.BagService(store, notifier);
        var beanId = store.Beans.First().Id;
        var result = await bagSvc.CreateNewBagForBeanAsync(beanId, DateTime.UtcNow.AddDays(-3));
        Assert.True(result.Success);
        Assert.Equal(beanId, result.Data!.BeanId);
    }

    [Fact]
    public async Task CreateBag_InvalidBean_Fails()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var bagSvc = TestFactory.BagService(store, notifier);
        var result = await bagSvc.CreateNewBagForBeanAsync(999, DateTime.UtcNow);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateBag_FutureRoastDate_Fails()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        store.Seed();
        var bagSvc = TestFactory.BagService(store, notifier);
        var beanId = store.Beans.First().Id;
        var result = await bagSvc.CreateNewBagForBeanAsync(beanId, DateTime.UtcNow.AddDays(30));
        Assert.False(result.Success);
    }

    [Fact]
    public async Task MarkBagComplete_ExcludesFromShotLogging()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        store.Seed();
        var bagSvc = TestFactory.BagService(store, notifier);
        var bag = store.Bags.First();
        await bagSvc.MarkBagCompleteAsync(bag.Id);
        var active = await bagSvc.GetActiveBagsForShotLoggingAsync();
        Assert.DoesNotContain(active, b => b.Id == bag.Id);
    }

    [Fact]
    public async Task ReactivateBag_ReturnsToShotLogging()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        store.Seed();
        var bagSvc = TestFactory.BagService(store, notifier);
        var bag = store.Bags.First();
        await bagSvc.MarkBagCompleteAsync(bag.Id);
        await bagSvc.ReactivateBagAsync(bag.Id);
        var active = await bagSvc.GetActiveBagsForShotLoggingAsync();
        Assert.Contains(active, b => b.Id == bag.Id);
    }

    [Fact]
    public async Task DeleteBag_PreservesHistoricalShotsAndBagForeignKey()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        store.Seed();
        var bagSvc = TestFactory.BagService(store, notifier);
        var bag = store.Bags.First(b => store.Shots.Any(s => s.BagId == b.Id));
        var shot = store.Shots.First(s => s.BagId == bag.Id);
        await bagSvc.DeleteBagAsync(bag.Id);
        Assert.True(bag.IsDeleted);
        Assert.False(shot.IsDeleted);
        Assert.Equal(bag.Id, shot.BagId);
    }

    [Fact]
    public async Task UpdateBag_InvalidInput_DoesNotMutateStoredBag()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        store.Seed();
        var bagSvc = TestFactory.BagService(store, notifier);
        var bag = store.Bags.First();
        var originalDate = bag.RoastDate;
        var originalNotes = bag.Notes;

        var result = await bagSvc.UpdateBagAsync(new BagReal
        {
            Id = bag.Id,
            BeanId = bag.BeanId,
            RoastDate = DateTime.Now.AddDays(1),
            Notes = "must not be stored",
            IsComplete = !bag.IsComplete
        });

        Assert.False(result.Success);
        Assert.Equal(originalDate, bag.RoastDate);
        Assert.Equal(originalNotes, bag.Notes);
        Assert.False(bag.IsComplete);
    }

    [Fact]
    public async Task GetMostRecentActiveBag_ReturnsLatest()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        store.Seed();
        var bagSvc = TestFactory.BagService(store, notifier);
        var beanId = store.Beans.First().Id;
        // Add a newer bag
        await bagSvc.CreateNewBagForBeanAsync(beanId, DateTime.Today);
        var mostRecent = await bagSvc.GetMostRecentActiveBagForBeanAsync(beanId);
        Assert.NotNull(mostRecent);
        Assert.Equal(DateTime.Today, mostRecent!.RoastDate.Date);
    }
}

#endregion

#region Equipment CRUD + Archive Tests

public class EquipmentServiceTests
{
    [Fact]
    public async Task CreateEquipment_ValidDto_Succeeds()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.EquipmentService(store, notifier);
        var dto = await svc.CreateEquipmentAsync(new CreateEquipmentDto { Name = "Breville", Type = EquipmentTypeReal.Machine });
        Assert.Equal("Breville", dto.Name);
        Assert.Equal(EquipmentTypeReal.Machine, dto.Type);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task CreateEquipment_EmptyName_Throws()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.EquipmentService(store, notifier);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateEquipmentAsync(new CreateEquipmentDto { Name = "", Type = EquipmentTypeReal.Machine }));
    }

    [Fact]
    public async Task CreateEquipment_NameTooLong_Throws()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.EquipmentService(store, notifier);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateEquipmentAsync(new CreateEquipmentDto { Name = new string('x', 101), Type = EquipmentTypeReal.Machine }));
    }

    [Fact]
    public async Task ArchiveEquipment_SetsInactive()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.EquipmentService(store, notifier);
        var created = await svc.CreateEquipmentAsync(new CreateEquipmentDto { Name = "Grinder", Type = EquipmentTypeReal.Grinder });
        await svc.ArchiveEquipmentAsync(created.Id);
        var fetched = await svc.GetEquipmentByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.False(fetched!.IsActive);
    }

    [Fact]
    public async Task ArchiveEquipment_ExcludedFromActiveList()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.EquipmentService(store, notifier);
        var created = await svc.CreateEquipmentAsync(new CreateEquipmentDto { Name = "Tamper", Type = EquipmentTypeReal.Tamper });
        await svc.ArchiveEquipmentAsync(created.Id);
        var active = await svc.GetAllActiveEquipmentAsync();
        Assert.DoesNotContain(active, e => e.Id == created.Id);
    }

    [Fact]
    public async Task GetByType_FiltersCorrectly()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.EquipmentService(store, notifier);
        await svc.CreateEquipmentAsync(new CreateEquipmentDto { Name = "Machine1", Type = EquipmentTypeReal.Machine });
        await svc.CreateEquipmentAsync(new CreateEquipmentDto { Name = "Grinder1", Type = EquipmentTypeReal.Grinder });
        var machines = await svc.GetEquipmentByTypeAsync(EquipmentTypeReal.Machine);
        Assert.Single(machines);
        Assert.Equal("Machine1", machines[0].Name);
    }
}

#endregion

#region UserProfile CRUD + Validation Tests

public class UserProfileServiceTests
{
    [Fact]
    public void Constructor_NullImageProcessor_Throws()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        Assert.Throws<ArgumentNullException>(() => new InMemoryUserProfileService(store, notifier, null!));
    }

    [Fact]
    public async Task CreateProfile_ValidDto_Succeeds()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.ProfileService(store, notifier);
        var dto = await svc.CreateProfileAsync(new CreateUserProfileDto { Name = "Alice" });
        Assert.Equal("Alice", dto.Name);
    }

    [Fact]
    public async Task GetAllProfiles_OrdersActiveProfilesByName()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.ProfileService(store, notifier);
        await svc.CreateProfileAsync(new CreateUserProfileDto { Name = "Zoe" });
        var deleted = await svc.CreateProfileAsync(new CreateUserProfileDto { Name = "Aaron" });
        await svc.CreateProfileAsync(new CreateUserProfileDto { Name = "Maya" });
        await svc.DeleteProfileAsync(deleted.Id);

        var profiles = await svc.GetAllProfilesAsync();

        Assert.Equal(["Maya", "Zoe"], profiles.Select(profile => profile.Name));
    }

    [Fact]
    public async Task CreateProfile_EmptyName_Throws()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.ProfileService(store, notifier);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateProfileAsync(new CreateUserProfileDto { Name = "" }));
    }

    [Fact]
    public async Task CreateProfile_NameTooLong_Throws()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.ProfileService(store, notifier);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateProfileAsync(new CreateUserProfileDto { Name = new string('x', 51) }));
    }

    [Fact]
    public async Task CreateProfile_ContextTooLong_Throws()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.ProfileService(store, notifier);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateProfileAsync(new CreateUserProfileDto { Name = "OK", Context = new string('x', 2001) }));
    }

    [Fact]
    public async Task UpdateProfile_EmptyContextClearsIt()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.ProfileService(store, notifier);
        var created = await svc.CreateProfileAsync(new CreateUserProfileDto { Name = "Bob", Context = "Likes oat milk" });
        var updated = await svc.UpdateProfileAsync(created.Id, new UpdateUserProfileDto { Context = "" });
        Assert.Null(updated.Context);
    }

    [Fact]
    public async Task UpdateProfile_ContextTooLong_Throws()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var images = new RecordingImageProcessingService();
        var svc = new InMemoryUserProfileService(store, notifier, images);
        var created = await svc.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "Carol",
            AvatarPath = "profile_avatar_1_deadbeef.jpg"
        });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateProfileAsync(created.Id, new UpdateUserProfileDto
            {
                AvatarPath = "",
                Context = new string('x', 2001)
            }));
        Assert.Empty(images.Deleted);
        Assert.Equal("profile_avatar_1_deadbeef.jpg", store.Profiles.Single().AvatarPath);
    }

    [Fact]
    public async Task DeleteProfile_SoftDeletes()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.ProfileService(store, notifier);
        var created = await svc.CreateProfileAsync(new CreateUserProfileDto { Name = "Dave" });
        await svc.DeleteProfileAsync(created.Id);
        var fetched = await svc.GetProfileByIdAsync(created.Id);
        Assert.Null(fetched);
    }

    [Fact]
    public async Task UpdateProfileImage_ReplacesAndDeletesOwnedAvatar()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var images = new RecordingImageProcessingService();
        var svc = new InMemoryUserProfileService(store, notifier, images);
        var profile = await svc.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "Avatar owner",
            AvatarPath = "profile_avatar_1.jpg"
        });

        var result = await svc.UpdateProfileImageAsync(profile.Id, new MemoryStream([1, 2, 3]));

        Assert.True(result.Success);
        Assert.Matches(@"^profile_avatar_1_[0-9a-f]{8}\.jpg$", result.NewAvatarPath!);
        Assert.Contains("profile_avatar_1.jpg", images.Deleted);
        Assert.Contains(result.NewAvatarPath!, images.Saved);
    }

    [Fact]
    public async Task RemoveProfileImage_OwnedAvatar_DeletesFileAndClearsPath()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var images = new RecordingImageProcessingService();
        var svc = new InMemoryUserProfileService(store, notifier, images);
        var profile = await svc.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "Avatar owner",
            AvatarPath = "profile_avatar_1_deadbeef.jpg"
        });

        Assert.True(await svc.RemoveProfileImageAsync(profile.Id));
        Assert.Contains("profile_avatar_1_deadbeef.jpg", images.Deleted);
        Assert.Null(store.Profiles.Single().AvatarPath);
    }

    [Fact]
    public async Task DeleteProfile_OwnedAvatar_DeletesFileBeforeSoftDelete()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var images = new RecordingImageProcessingService();
        var svc = new InMemoryUserProfileService(store, notifier, images);
        var profile = await svc.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "Avatar owner",
            AvatarPath = "profile_avatar_1_deadbeef.jpg"
        });

        await svc.DeleteProfileAsync(profile.Id);

        Assert.Contains("profile_avatar_1_deadbeef.jpg", images.Deleted);
        Assert.True(store.Profiles.Single().IsDeleted);
    }

    [Theory]
    [InlineData("/outside/profile_avatar_1_deadbeef.jpg")]
    [InlineData("https://example.test/profile_avatar_1_deadbeef.jpg")]
    [InlineData("profile_avatar_2_deadbeef.jpg")]
    [InlineData("unrelated.jpg")]
    public async Task RemoveProfileImage_ExternalOrUnownedPath_DoesNotDeleteFile(string avatarPath)
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var images = new RecordingImageProcessingService();
        var svc = new InMemoryUserProfileService(store, notifier, images);
        var profile = await svc.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "External avatar",
            AvatarPath = avatarPath
        });

        Assert.True(await svc.RemoveProfileImageAsync(profile.Id));
        Assert.Empty(images.Deleted);
        Assert.Null(store.Profiles.Single().AvatarPath);
    }

    [Fact]
    public async Task UpdateProfile_AvatarReplacement_DeletesOnlyPreviousOwnedFile()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var images = new RecordingImageProcessingService();
        var svc = new InMemoryUserProfileService(store, notifier, images);
        var profile = await svc.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "Avatar owner",
            AvatarPath = "profile_avatar_1_deadbeef.jpg"
        });

        var updated = await svc.UpdateProfileAsync(profile.Id, new UpdateUserProfileDto
        {
            AvatarPath = "https://example.test/new-avatar.jpg"
        });

        Assert.Contains("profile_avatar_1_deadbeef.jpg", images.Deleted);
        Assert.Equal("https://example.test/new-avatar.jpg", updated.AvatarPath);
    }

    [Fact]
    public async Task UpdateProfile_ExternalPreviousAvatar_DoesNotDeleteExternalFile()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var images = new RecordingImageProcessingService();
        var svc = new InMemoryUserProfileService(store, notifier, images);
        var profile = await svc.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "External avatar",
            AvatarPath = "https://example.test/old-avatar.jpg"
        });

        await svc.UpdateProfileAsync(profile.Id, new UpdateUserProfileDto
        {
            AvatarPath = "profile_avatar_1_deadbeef.jpg"
        });

        Assert.Empty(images.Deleted);
    }

    [Fact]
    public async Task DeleteProfile_ExternalAvatar_DoesNotDeleteExternalFile()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var images = new RecordingImageProcessingService();
        var svc = new InMemoryUserProfileService(store, notifier, images);
        var profile = await svc.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "External avatar",
            AvatarPath = "/outside/avatar.jpg"
        });

        await svc.DeleteProfileAsync(profile.Id);

        Assert.Empty(images.Deleted);
        Assert.True(store.Profiles.Single().IsDeleted);
    }
}

internal sealed class RecordingImageProcessingService : IImageProcessingService
{
    public List<string> Deleted { get; } = [];
    public List<string> Saved { get; } = [];

    public Task<ImageValidationResult> ValidateImageAsync(Stream imageStream) =>
        Task.FromResult(ImageValidationResult.Valid());

    public Task<string> SaveImageAsync(Stream imageStream, string filename)
    {
        Saved.Add(filename);
        return Task.FromResult(System.IO.Path.Combine("managed", filename));
    }

    public Task<bool> DeleteImageAsync(string filename)
    {
        Deleted.Add(filename);
        return Task.FromResult(true);
    }

    public string GetImagePath(string filename) => System.IO.Path.Combine("managed", filename);
    public bool ImageExists(string filename) => true;
    public Task<MemoryStream?> DownsampleAsync(Stream imageStream, int maxDimension, int quality) =>
        Task.FromResult<MemoryStream?>(new MemoryStream([0xFF, 0xD8, 0xFF, 0xD9]));
}

public class BaristaServiceLocatorImageIntegrationTests
{
    [Fact]
    public async Task ProfileService_UsesConfiguredImageProcessingAndResolvesManagedPath()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var images = new RecordingImageProcessingService();
        BaristaServiceLocator.Initialize(store, notifier, new InMemoryPreferencesService(), images);
        var profile = await BaristaServiceLocator.ProfileService.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "Locator profile",
            AvatarPath = "profile_avatar_1_deadbeef.jpg"
        });

        var resolved = await BaristaServiceLocator.ProfileService.GetProfileImagePathAsync(profile.Id);
        Assert.Equal(System.IO.Path.Combine("managed", profile.AvatarPath!), resolved);

        Assert.True(await BaristaServiceLocator.ProfileService.RemoveProfileImageAsync(profile.Id));
        Assert.Contains("profile_avatar_1_deadbeef.jpg", images.Deleted);
    }

    [Fact]
    public async Task LocalImageProcessingService_SaveResolveDelete_UsesManagedRoot()
    {
        var root = System.IO.Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            "barista-avatar-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new LocalImageProcessingService(root);
            const string filename = "profile_avatar_1_deadbeef.jpg";
            await service.SaveImageAsync(new MemoryStream([0xFF, 0xD8, 0xFF, 0xD9]), filename);

            var resolved = service.GetImagePath(filename);
            Assert.Equal(System.IO.Path.Combine(root, filename), resolved);
            Assert.True(service.ImageExists(filename));
            Assert.True(await service.DeleteImageAsync(filename));
            Assert.False(service.ImageExists(filename));
            Assert.Throws<ArgumentException>(() => service.GetImagePath("../external.jpg"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root);
        }
    }
}

#endregion

#region DrinkValueRange Tests

public class DrinkValueRangeServiceTests
{
    private static InMemoryDrinkValueRangeService CreateService() => new();

    [Fact]
    public void DefaultMode_IsAuto()
    {
        var svc = CreateService();
        Assert.Equal(ValueRangeModeReal.Auto, svc.GetMode(DrinkValueMetricReal.DoseIn));
    }

    [Fact]
    public void Resolve_Auto_ReturnsAutoSource()
    {
        var svc = CreateService();
        var result = svc.Resolve(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso);
        Assert.Equal(ValueRangeSourceReal.Auto, result.Source);
        Assert.Equal("g", result.CanonicalUnit);
    }

    [Fact]
    public void SaveOverride_SwitchesToCustomMode()
    {
        var svc = CreateService();
        svc.SaveOverride(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso, 15.0m, 22.0m);
        Assert.Equal(ValueRangeModeReal.Custom, svc.GetMode(DrinkValueMetricReal.DoseIn));
    }

    [Fact]
    public void Resolve_WithOverride_ReturnsCustomSource()
    {
        var svc = CreateService();
        svc.SaveOverride(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso, 15.0m, 22.0m);
        var result = svc.Resolve(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso);
        Assert.Equal(ValueRangeSourceReal.Custom, result.Source);
        Assert.Equal(15.0m, result.Range.Minimum);
        Assert.Equal(22.0m, result.Range.Maximum);
    }

    [Fact]
    public void Resolve_CustomModeNoOverride_ReturnsAutoFallback()
    {
        var svc = CreateService();
        svc.SetMode(DrinkValueMetricReal.Time, ValueRangeModeReal.Custom);
        var result = svc.Resolve(DrinkValueMetricReal.Time, BrewMethodReal.Espresso);
        Assert.Equal(ValueRangeSourceReal.AutoFallback, result.Source);
    }

    [Fact]
    public void SaveOverride_MinGreaterThanMax_Throws()
    {
        var svc = CreateService();
        Assert.Throws<ArgumentException>(() =>
            svc.SaveOverride(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso, 25.0m, 10.0m));
    }

    [Fact]
    public void SaveOverride_OutsideHardRange_Throws()
    {
        var svc = CreateService();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            svc.SaveOverride(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso, 1.0m, 3.0m));
    }

    [Fact]
    public void SaveOverride_TimeWithDecimals_Throws()
    {
        var svc = CreateService();
        Assert.Throws<ArgumentException>(() =>
            svc.SaveOverride(DrinkValueMetricReal.Time, BrewMethodReal.Espresso, 15.5m, 45.0m));
    }

    [Fact]
    public void RemoveOverride_FallsBackToAuto()
    {
        var svc = CreateService();
        svc.SaveOverride(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso, 15.0m, 22.0m);
        svc.RemoveOverride(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso);
        var result = svc.Resolve(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso);
        // Mode is still Custom but no override exists → AutoFallback
        Assert.Equal(ValueRangeSourceReal.AutoFallback, result.Source);
    }

    [Fact]
    public void ResetOverrides_ClearsAllForMetric()
    {
        var svc = CreateService();
        svc.SaveOverride(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso, 15.0m, 22.0m);
        svc.SaveOverride(DrinkValueMetricReal.DoseIn, BrewMethodReal.PourOver, 15.0m, 40.0m);
        svc.ResetOverrides(DrinkValueMetricReal.DoseIn);
        var settings = svc.GetSettings();
        Assert.Empty(settings.Overrides);
    }

    [Fact]
    public void SettingsChanged_FiresOnSave()
    {
        var svc = CreateService();
        var fired = false;
        svc.SettingsChanged += (_, _) => fired = true;
        svc.SaveOverride(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso, 15.0m, 22.0m);
        Assert.True(fired);
    }

    [Fact]
    public void SettingsChanged_FiresOnSetMode()
    {
        var svc = CreateService();
        var fired = false;
        svc.SettingsChanged += (_, _) => fired = true;
        svc.SetMode(DrinkValueMetricReal.DoseIn, ValueRangeModeReal.Custom);
        Assert.True(fired);
    }

    [Fact]
    public void Resolve_CustomOverride_ClampsDefault()
    {
        var svc = CreateService();
        // Espresso dose default is 18g. Override to 20-25 should clamp default to 20.
        svc.SaveOverride(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso, 20.0m, 25.0m);
        var result = svc.Resolve(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso);
        Assert.Equal(20.0m, result.Default);
    }

    [Theory]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":null,\"Overrides\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":{},\"Overrides\":null}")]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":{},\"Overrides\":[null]}")]
    [InlineData("{")]
    public void Load_MalformedPersistedGraph_FallsBackToAutomaticRanges(string json)
    {
        var preferences = new InMemoryPreferencesService();
        preferences.SetDrinkValueRangeSettingsJson(json);

        var service = new InMemoryDrinkValueRangeService(preferences);
        var settings = service.GetSettings();

        Assert.Empty(settings.Modes);
        Assert.Empty(settings.Overrides);
        Assert.Equal(
            "Custom range settings could not be read. Automatic ranges are active.",
            settings.LoadWarning);
        Assert.Equal(ValueRangeSourceReal.Auto, service.Resolve(DrinkValueMetricReal.DoseIn, BrewMethodReal.Espresso).Source);
    }

    [Fact]
    public void Load_InvalidOverride_FallsBackWithInvalidSettingsWarning()
    {
        var preferences = new InMemoryPreferencesService();
        preferences.SetDrinkValueRangeSettingsJson(
            "{\"SchemaVersion\":1,\"Modes\":{},\"Overrides\":[{\"Metric\":1,\"Method\":1,\"Minimum\":1,\"Maximum\":2}]}");

        var settings = new InMemoryDrinkValueRangeService(preferences).GetSettings();

        Assert.Empty(settings.Overrides);
        Assert.Equal(
            "Custom range settings are invalid. Automatic ranges are active.",
            settings.LoadWarning);
    }

    [Fact]
    public void Load_UnsupportedSchema_FallsBackWithVersionWarning()
    {
        var preferences = new InMemoryPreferencesService();
        preferences.SetDrinkValueRangeSettingsJson(
            "{\"SchemaVersion\":99,\"Modes\":{},\"Overrides\":[]}");

        var settings = new InMemoryDrinkValueRangeService(preferences).GetSettings();

        Assert.Empty(settings.Overrides);
        Assert.Equal(
            "Custom ranges use an unsupported format. Automatic ranges are active.",
            settings.LoadWarning);
    }
}

#endregion

#region Drink value range formatting tests

public class DrinkValueRangeFormattingTests
{
    [Theory]
    [InlineData(DrinkValueMetricReal.DoseIn, "Dose In")]
    [InlineData(DrinkValueMetricReal.Yield, "Yield")]
    [InlineData(DrinkValueMetricReal.GrindMicrons, "Grind Size")]
    [InlineData(DrinkValueMetricReal.Time, "Time")]
    public void MetricTitle_MatchesSource(DrinkValueMetricReal metric, string expected) =>
        Assert.Equal(expected, DrinkValueRangeFormatting.MetricTitle(metric));

    [Theory]
    [InlineData(45, "45 s")]
    [InlineData(120, "2 min")]
    [InlineData(125, "2:05")]
    [InlineData(3600, "1 h")]
    [InlineData(5400, "1 h 30 min")]
    public void FormatValue_Time_UsesSourceDurationFormatting(int seconds, string expected) =>
        Assert.Equal(expected, DrinkValueRangeFormatting.FormatValue(DrinkValueMetricReal.Time, seconds));

    [Fact]
    public void FormatValue_WeightsAndGrind_UseSourceUnits()
    {
        Assert.Equal("18.3 g", DrinkValueRangeFormatting.FormatValue(DrinkValueMetricReal.DoseIn, 18.25m));
        Assert.Equal("600 µm", DrinkValueRangeFormatting.FormatValue(DrinkValueMetricReal.GrindMicrons, 600m));
        Assert.Equal(
            "1 min - 2:30",
            DrinkValueRangeFormatting.FormatRange(DrinkValueMetricReal.Time, new DrinkValueRange(60m, 150m)));
    }

    [Theory]
    [InlineData(BrewMethodReal.Espresso, 1, "seconds", "0")]
    [InlineData(BrewMethodReal.V60, 60, "minutes", "0.##")]
    [InlineData(BrewMethodReal.ColdBrew, 3600, "hours", "0.##")]
    [InlineData(BrewMethodReal.ColdDrip, 3600, "hours", "0.##")]
    public void TimeEditorUnit_IsMethodSpecific(
        BrewMethodReal method,
        int scale,
        string label,
        string format)
    {
        var unit = DrinkValueRangeFormatting.GetEditorUnit(DrinkValueMetricReal.Time, method);
        Assert.Equal(scale, unit.Scale);
        Assert.Equal(label, unit.Label);
        Assert.Equal(format, unit.Format);
    }

    [Fact]
    public void TimeEditorUnit_RoundTripsCanonicalStorageValue()
    {
        var unit = DrinkValueRangeFormatting.GetEditorUnit(DrinkValueMetricReal.Time, BrewMethodReal.V60);
        Assert.Equal("3.5", DrinkValueRangeFormatting.FormatEditorValue(210m, unit));
        Assert.Equal(210m, unit.ToCanonical(3.5m));
        Assert.Equal(3.5m, unit.ToDisplay(210m));
    }
}

#endregion

#region DataChanged Notification Tests

public class DataChangeNotifierTests
{
    [Fact]
    public async Task BeanCreate_FiresNotification()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        DataChangedEventArgs? captured = null;
        notifier.DataChanged += (_, args) => captured = args;
        await svc.CreateBeanAsync(new CreateBeanDto { Name = "Test" });
        Assert.NotNull(captured);
        Assert.Equal(DataChangeTypeReal.BeanCreated, captured!.ChangeType);
    }

    [Fact]
    public async Task BeanUpdate_FiresNotification()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var bean = (await svc.CreateBeanAsync(new CreateBeanDto { Name = "Test" })).Data!;
        DataChangedEventArgs? captured = null;
        notifier.DataChanged += (_, args) => captured = args;
        await svc.UpdateBeanAsync(bean.Id, new UpdateBeanDto { Name = "Updated" });
        Assert.NotNull(captured);
        Assert.Equal(DataChangeTypeReal.BeanUpdated, captured!.ChangeType);
    }

    [Fact]
    public async Task BeanArchive_FiresNotification()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        var svc = TestFactory.BeanService(store, notifier, ratings);
        var bean = (await svc.CreateBeanAsync(new CreateBeanDto { Name = "Test" })).Data!;
        DataChangedEventArgs? captured = null;
        notifier.DataChanged += (_, args) => captured = args;
        await svc.ArchiveBeanAsync(bean.Id);
        Assert.NotNull(captured);
        Assert.Equal(DataChangeTypeReal.BeanUpdated, captured!.ChangeType);
    }

    [Fact]
    public async Task EquipmentCreate_FiresNotification()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.EquipmentService(store, notifier);
        DataChangedEventArgs? captured = null;
        notifier.DataChanged += (_, args) => captured = args;
        await svc.CreateEquipmentAsync(new CreateEquipmentDto { Name = "M", Type = EquipmentTypeReal.Machine });
        Assert.NotNull(captured);
        Assert.Equal(DataChangeTypeReal.EquipmentCreated, captured!.ChangeType);
    }

    [Fact]
    public async Task EquipmentArchive_FiresNotification()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.EquipmentService(store, notifier);
        var eq = await svc.CreateEquipmentAsync(new CreateEquipmentDto { Name = "G", Type = EquipmentTypeReal.Grinder });
        DataChangedEventArgs? captured = null;
        notifier.DataChanged += (_, args) => captured = args;
        await svc.ArchiveEquipmentAsync(eq.Id);
        Assert.NotNull(captured);
        Assert.Equal(DataChangeTypeReal.EquipmentUpdated, captured!.ChangeType);
    }

    [Fact]
    public async Task ProfileCreate_FiresNotification()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        var svc = TestFactory.ProfileService(store, notifier);
        DataChangedEventArgs? captured = null;
        notifier.DataChanged += (_, args) => captured = args;
        await svc.CreateProfileAsync(new CreateUserProfileDto { Name = "Eve" });
        Assert.NotNull(captured);
        Assert.Equal(DataChangeTypeReal.ProfileCreated, captured!.ChangeType);
    }
}

#endregion

#region Soft-delete relationship and completed-bag regression tests

public class SoftDeleteRelationshipTests
{
    [Fact]
    public async Task DeleteBean_PreservesBagsRecipesAndHistoricalShots()
    {
        var (store, notifier, ratings) = TestFactory.CreateCore();
        store.Seed();
        var bean = store.Beans.First(item => store.Bags.Any(bag => bag.BeanId == item.Id && store.Shots.Any(shot => shot.BagId == bag.Id)));
        var bag = store.Bags.First(item => item.BeanId == bean.Id);
        var shot = store.Shots.First(item => item.BagId == bag.Id);
        var recipe = new RecipeReal { Id = store.NextRecipeId(), BeanId = bean.Id, Title = "Historical recipe" };
        store.Recipes.Add(recipe);

        await TestFactory.BeanService(store, notifier, ratings).DeleteBeanAsync(bean.Id);

        Assert.True(bean.IsDeleted);
        Assert.False(bag.IsDeleted);
        Assert.False(shot.IsDeleted);
        Assert.False(recipe.IsDeleted);
        Assert.Equal(bean.Id, bag.BeanId);
        Assert.Equal(bag.Id, shot.BagId);
        Assert.Equal(bean.Id, recipe.BeanId);
    }

    [Fact]
    public async Task DeleteEquipment_PreservesShotAndJunctionForeignKeys()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        store.Seed();
        var machine = store.Equipment.First(item => item.Type == EquipmentTypeReal.Machine);
        var shot = store.Shots.First(item => item.MachineId == machine.Id);
        store.ShotEquipments.Add(new ShotEquipmentReal { ShotRecordId = shot.Id, EquipmentId = machine.Id });

        await TestFactory.EquipmentService(store, notifier).DeleteEquipmentAsync(machine.Id);

        Assert.True(machine.IsDeleted);
        Assert.Equal(machine.Id, shot.MachineId);
        Assert.Contains(store.ShotEquipments, link => link.ShotRecordId == shot.Id && link.EquipmentId == machine.Id);
    }

    [Fact]
    public async Task DeleteProfile_PreservesHistoricalShotForeignKeys()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        store.Seed();
        var profile = store.Profiles.First();
        var shot = store.Shots.First(item => item.MadeById == profile.Id);

        await TestFactory.ProfileService(store, notifier).DeleteProfileAsync(profile.Id);

        Assert.True(profile.IsDeleted);
        Assert.Equal(profile.Id, shot.MadeById);
    }

    [Fact]
    public async Task CreateShot_CompletedBag_IsRejectedWithoutMutation()
    {
        var (store, notifier, _) = TestFactory.CreateCore();
        store.Seed();
        var bag = store.Bags.First();
        bag.IsComplete = true;
        var service = new InMemoryShotService(store, notifier);
        var initialCount = store.Shots.Count;

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShotAsync(new CreateShotDto
        {
            BagId = bag.Id,
            BrewMethod = BrewMethodReal.Espresso,
            DoseIn = 18m,
            ExpectedTime = 28m,
            ExpectedOutput = 36m,
            DrinkType = "Espresso"
        }));

        Assert.Contains("completed bag", error.Message);
        Assert.Equal(initialCount, store.Shots.Count);
    }
}

#endregion

#region Levenshtein Unit Tests

public class LevenshteinTests
{
    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("yirgacheffe", "yirgachefe", 1)]
    public void Levenshtein_ReturnsExpectedDistance(string a, string b, int expected)
    {
        Assert.Equal(expected, InMemoryBeanService.Levenshtein(a, b));
    }
}

#endregion
