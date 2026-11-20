using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models;
using CometBaristaNotes.Services.DTOs;
using CometBaristaNotes.Services.Recipes;

namespace CometBaristaNotes.Services;

public interface IBeanService
{
    Task<List<BeanDto>> GetAllActiveBeansAsync();
    Task<BeanDto?> GetBeanByIdAsync(int id);
    Task<BeanDto?> GetBeanWithRatingsAsync(int id);
    Task<OperationResult<BeanDto>> CreateBeanAsync(CreateBeanDto dto);
    Task<BeanDto> UpdateBeanAsync(int id, UpdateBeanDto dto);
    Task ArchiveBeanAsync(int id);
    Task DeleteBeanAsync(int id);
    Task<IReadOnlyList<BeanDto>> GetRecentBeansAsync(int limit = 6, int withinDays = 90);
    Task<BeanDto?> FuzzyFindByNameRoasterAsync(string name, string? roaster);
    Task<IReadOnlyList<string>> GetDistinctRoastersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetDistinctOriginsAsync(CancellationToken ct = default);
    Task<RecipeSourcingResult> RefreshRecipesAsync(int beanId, CancellationToken ct = default);
}
