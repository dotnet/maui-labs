using System.Collections.Generic;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public interface IShotService
{
    Task<ShotRecordDto?> GetMostRecentShotAsync();
    Task<ShotRecordDto> CreateShotAsync(CreateShotDto dto);
    Task<ShotRecordDto> UpdateShotAsync(int id, UpdateShotDto dto);
    Task DeleteShotAsync(int id);
    Task<PagedResult<ShotRecordDto>> GetShotHistoryAsync(int pageIndex, int pageSize);
    Task<PagedResult<ShotRecordDto>> GetShotHistoryByUserAsync(int userProfileId, int pageIndex, int pageSize);
    Task<PagedResult<ShotRecordDto>> GetShotHistoryByBeanAsync(int beanId, int pageIndex, int pageSize);
    Task<PagedResult<ShotRecordDto>> GetShotHistoryByEquipmentAsync(int equipmentId, int pageIndex, int pageSize);
    Task<PagedResult<ShotRecordDto>> GetFilteredShotHistoryAsync(ShotFilterCriteriaDto? criteria, int pageIndex, int pageSize);
    Task<ShotRecordDto?> GetShotByIdAsync(int id);
    Task<ShotRecordDto?> GetBestRatedShotByBeanAsync(int beanId);
    Task<ShotRecordDto?> GetBestRatedShotByBagAsync(int bagId);
    Task<List<BeanFilterOptionDto>> GetBeansWithShotsAsync();
    Task<List<UserProfileDto>> GetPeopleWithShotsAsync();
    Task<AIAdviceRequestDto?> GetShotContextForAIAsync(int shotId);
    Task<int?> GetMostRecentBeanIdAsync();
    Task<bool> BeanHasHistoryAsync(int beanId);
    Task<BeanRecommendationContextDto?> GetBeanRecommendationContextAsync(int beanId);
}
