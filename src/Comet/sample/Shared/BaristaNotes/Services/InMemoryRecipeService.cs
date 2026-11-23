using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public sealed class InMemoryRecipeService(IBaristaDataStore store) : IRecipeService
{
    public Task<IReadOnlyList<RecipeDto>> GetRecipesForBeanAsync(int beanId) =>
        Task.FromResult<IReadOnlyList<RecipeDto>>(store.Recipes
            .Where(recipe => recipe.BeanId == beanId && !recipe.IsDeleted)
            .OrderBy(recipe => recipe.BrewMethod)
            .ThenByDescending(recipe => recipe.FetchedAt)
            .Select(Map)
            .ToList());

    public Task<RecipeDto?> GetRecipeForBeanAndMethodAsync(int beanId, BrewMethod method)
    {
        var recipe = store.Recipes
            .Where(item => item.BeanId == beanId && item.BrewMethod == method && !item.IsDeleted)
            .OrderByDescending(item => item.IsEditedByUser)
            .ThenByDescending(item => item.FetchedAt)
            .FirstOrDefault();
        return Task.FromResult(recipe is null ? null : Map(recipe));
    }

    public Task<RecipeDto?> GetByIdAsync(int id)
    {
        var recipe = store.Recipes.FirstOrDefault(item => item.Id == id && !item.IsDeleted);
        return Task.FromResult(recipe is null ? null : Map(recipe));
    }

    public Task<RecipeDto> CreateAsync(CreateRecipeDto dto)
    {
        var result = store.ExecuteMutation(() => CreateCore(dto));
        return Task.FromResult(result);
    }

    public Task<RecipeDto> UpsertFromSourceAsync(CreateRecipeDto dto)
    {
        var result = store.ExecuteMutation(() =>
        {
            var matches = store.Recipes
                .Where(item => item.BeanId == dto.BeanId && item.BrewMethod == dto.BrewMethod && !item.IsDeleted)
                .OrderByDescending(item => item.IsEditedByUser)
                .ThenByDescending(item => item.FetchedAt)
                .ToList();
            var existing = matches.FirstOrDefault();
            if (existing is null)
                return CreateCore(dto);

            foreach (var duplicate in matches.Skip(1))
            {
                duplicate.IsDeleted = true;
                duplicate.LastModifiedAt = DateTime.UtcNow;
            }
            if (existing.IsEditedByUser)
            {
                if (matches.Count > 1)
                    store.SaveChanges();
                return Map(existing);
            }

            Apply(existing, dto);
            existing.FetchedAt = DateTime.UtcNow;
            existing.LastModifiedAt = DateTime.UtcNow;
            store.SaveChanges();
            return Map(existing);
        });
        return Task.FromResult(result);
    }

    public Task<RecipeDto> UpdateAsync(int id, UpdateRecipeDto dto)
    {
        var result = store.ExecuteMutation(() =>
        {
            var recipe = store.Recipes.FirstOrDefault(item => item.Id == id && !item.IsDeleted)
                ?? throw new KeyNotFoundException($"Recipe {id} not found");
            if (dto.Title != null) recipe.Title = dto.Title;
            if (dto.DoseIn.HasValue) recipe.DoseIn = dto.DoseIn;
            if (dto.OutputAmount.HasValue) recipe.OutputAmount = dto.OutputAmount;
            if (dto.GrindHint != null) recipe.GrindHint = dto.GrindHint;
            if (dto.BrewTempC.HasValue) recipe.BrewTempC = dto.BrewTempC;
            if (dto.TotalTimeSeconds.HasValue) recipe.TotalTimeSeconds = dto.TotalTimeSeconds;
            if (dto.ParametersJson != null) recipe.ParametersJson = dto.ParametersJson;
            if (dto.Notes != null) recipe.Notes = dto.Notes;
            recipe.IsEditedByUser = true;
            recipe.LastModifiedAt = DateTime.UtcNow;
            store.SaveChanges();
            return Map(recipe);
        });
        return Task.FromResult(result);
    }

    public Task DeleteAsync(int id)
    {
        store.ExecuteMutation(() =>
        {
            var recipe = store.Recipes.FirstOrDefault(item => item.Id == id && !item.IsDeleted)
                ?? throw new KeyNotFoundException($"Recipe {id} not found");
            recipe.IsDeleted = true;
            recipe.LastModifiedAt = DateTime.UtcNow;
            store.SaveChanges();
        });
        return Task.CompletedTask;
    }

    private RecipeDto CreateCore(CreateRecipeDto dto)
    {
        if (!store.Beans.Any(bean => bean.Id == dto.BeanId && !bean.IsDeleted))
            throw new ArgumentException("Bean not found", nameof(dto));
        if (store.Recipes.Any(recipe =>
                recipe.BeanId == dto.BeanId &&
                recipe.BrewMethod == dto.BrewMethod &&
                !recipe.IsDeleted))
        {
            throw new InvalidOperationException(
                "A recipe already exists for this bean and brew method.");
        }
        var now = DateTime.UtcNow;
        var recipe = new Recipe
        {
            Id = store.NextRecipeId(),
            BeanId = dto.BeanId,
            BrewMethod = dto.BrewMethod,
            Source = dto.Source,
            SourceUrl = dto.SourceUrl,
            Title = dto.Title,
            DoseIn = dto.DoseIn,
            OutputAmount = dto.OutputAmount,
            GrindHint = dto.GrindHint,
            BrewTempC = dto.BrewTempC,
            TotalTimeSeconds = dto.TotalTimeSeconds,
            ParametersJson = dto.ParametersJson,
            Notes = dto.Notes,
            FetchedAt = now,
            SyncId = Guid.NewGuid(),
            LastModifiedAt = now
        };
        store.Recipes.Add(recipe);
        store.SaveChanges();
        return Map(recipe);
    }

    private static void Apply(Recipe recipe, CreateRecipeDto dto)
    {
        recipe.Source = dto.Source;
        recipe.SourceUrl = dto.SourceUrl;
        recipe.Title = dto.Title;
        recipe.DoseIn = dto.DoseIn;
        recipe.OutputAmount = dto.OutputAmount;
        recipe.GrindHint = dto.GrindHint;
        recipe.BrewTempC = dto.BrewTempC;
        recipe.TotalTimeSeconds = dto.TotalTimeSeconds;
        recipe.ParametersJson = dto.ParametersJson;
        recipe.Notes = dto.Notes;
    }

    private static RecipeDto Map(Recipe recipe) => new()
    {
        Id = recipe.Id,
        BeanId = recipe.BeanId,
        BrewMethod = recipe.BrewMethod,
        Source = recipe.Source,
        SourceUrl = recipe.SourceUrl,
        Title = recipe.Title,
        DoseIn = recipe.DoseIn,
        OutputAmount = recipe.OutputAmount,
        GrindHint = recipe.GrindHint,
        BrewTempC = recipe.BrewTempC,
        TotalTimeSeconds = recipe.TotalTimeSeconds,
        ParametersJson = recipe.ParametersJson,
        Notes = recipe.Notes,
        FetchedAt = recipe.FetchedAt,
        IsEditedByUser = recipe.IsEditedByUser
    };
}
