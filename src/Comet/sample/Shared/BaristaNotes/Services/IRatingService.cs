using System.Collections.Generic;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public interface IRatingService
{
    Task<RatingAggregateDto> GetBeanRatingAsync(int beanId);
    Task<RatingAggregateDto> GetBagRatingAsync(int bagId);
    Task<Dictionary<int, RatingAggregateDto>> GetBagRatingsBatchAsync(IEnumerable<int> bagIds);
}
