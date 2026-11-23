using System.Collections.Generic;
using System.Threading.Tasks;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public interface IRecipeService
{
    Task<IReadOnlyList<RecipeDto>> GetRecipesForBeanAsync(int beanId);
    Task<RecipeDto?> GetRecipeForBeanAndMethodAsync(int beanId, BrewMethod method);
    Task<RecipeDto?> GetByIdAsync(int id);
    Task<RecipeDto> CreateAsync(CreateRecipeDto dto);
    Task<RecipeDto> UpsertFromSourceAsync(CreateRecipeDto dto);
    Task<RecipeDto> UpdateAsync(int id, UpdateRecipeDto dto);
    Task DeleteAsync(int id);
}
