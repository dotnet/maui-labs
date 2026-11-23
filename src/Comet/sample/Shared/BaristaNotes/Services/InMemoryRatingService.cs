using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public sealed class InMemoryRatingService : IRatingService
{
    private readonly IBaristaDataStore _store;

    public InMemoryRatingService(IBaristaDataStore store) => _store = store;

    public Task<RatingAggregateDto> GetBeanRatingAsync(int beanId)
    {
        var bagIds = _store.Bags.Where(b => b.BeanId == beanId).Select(b => b.Id).ToHashSet();
        var shots = _store.Shots.Where(s => bagIds.Contains(s.BagId) && !s.IsDeleted).ToList();
        return Task.FromResult(Aggregate(shots));
    }

    public Task<RatingAggregateDto> GetBagRatingAsync(int bagId)
    {
        var shots = _store.Shots.Where(s => s.BagId == bagId && !s.IsDeleted).ToList();
        return Task.FromResult(Aggregate(shots));
    }

    public Task<Dictionary<int, RatingAggregateDto>> GetBagRatingsBatchAsync(IEnumerable<int> bagIds)
    {
        var result = new Dictionary<int, RatingAggregateDto>();
        foreach (var bagId in bagIds)
        {
            var shots = _store.Shots.Where(s => s.BagId == bagId && !s.IsDeleted).ToList();
            result[bagId] = Aggregate(shots);
        }
        return Task.FromResult(result);
    }

    private static RatingAggregateDto Aggregate(List<Models.ShotRecord> shots)
    {
        var rated = shots.Where(s => s.Rating.HasValue).ToList();
        var dist = new Dictionary<int, int>();
        for (int i = 0; i <= 4; i++)
            dist[i] = rated.Count(s => s.Rating == i);
        return new RatingAggregateDto
        {
            AverageRating = rated.Count > 0 ? rated.Average(s => s.Rating!.Value) : 0,
            TotalShots = shots.Count,
            RatedShots = rated.Count,
            Distribution = dist,
        };
    }
}
