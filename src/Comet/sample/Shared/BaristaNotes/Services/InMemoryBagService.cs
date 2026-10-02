using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public sealed class InMemoryBagService : IBagService
{
    private readonly IBaristaDataStore _store;
    private readonly IDataChangeNotifier _notifier;

    public InMemoryBagService(IBaristaDataStore store, IDataChangeNotifier notifier)
    {
        _store = store;
        _notifier = notifier;
    }

    public Task<OperationResult<Bag>> CreateBagAsync(Bag bag)
    {
        if (bag.RoastDate > DateTime.Now)
            return Task.FromResult(OperationResult<Bag>.Fail("Roast date cannot be in the future"));
        if (bag.Notes?.Length > 500)
            return Task.FromResult(OperationResult<Bag>.Fail("Notes cannot exceed 500 characters"));

        var result = _store.ExecuteMutation(() =>
        {
            if (!_store.Beans.Any(bean => bean.Id == bag.BeanId && !bean.IsDeleted))
                return OperationResult<Bag>.Fail("Bean not found");
            var now = DateTime.UtcNow;
            bag.Id = _store.NextBagId();
            bag.CreatedAt = now;
            bag.LastModifiedAt = now;
            bag.SyncId = bag.SyncId == Guid.Empty ? Guid.NewGuid() : bag.SyncId;
            bag.IsActive = true;
            _store.Bags.Add(bag);
            _store.SaveChanges();
            return OperationResult<Bag>.Ok(bag);
        });
        if (!result.Success)
            return Task.FromResult(result);
        _notifier.NotifyDataChanged(DataChangeType.BagCreated, bag);
        return Task.FromResult(result);
    }

    public Task<OperationResult<BagSummaryDto>> CreateNewBagForBeanAsync(int beanId, DateTime roastDate, string? notes = null)
    {
        if (roastDate.Date > DateTime.Today)
            return Task.FromResult(OperationResult<BagSummaryDto>.Fail("Roast date cannot be in the future."));
        if (notes?.Length > 500)
            return Task.FromResult(OperationResult<BagSummaryDto>.Fail("Notes cannot exceed 500 characters"));

        var result = _store.ExecuteMutation(() =>
        {
            var bean = _store.Beans.FirstOrDefault(b => b.Id == beanId && !b.IsDeleted && b.IsActive);
            if (bean == null)
                return (Result: OperationResult<BagSummaryDto>.Fail("Bean not found or inactive"), Bag: (Bag?)null);
            var bag = new Bag
            {
                Id = _store.NextBagId(),
                BeanId = beanId,
                RoastDate = roastDate,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Bean = bean,
                SyncId = Guid.NewGuid(),
                LastModifiedAt = DateTime.UtcNow,
            };
            _store.Bags.Add(bag);
            _store.SaveChanges();
            var dto = new BagSummaryDto { Id = bag.Id, BeanId = beanId, BeanName = bean.Name, RoastDate = roastDate, Notes = bag.Notes };
            return (Result: OperationResult<BagSummaryDto>.Ok(dto), Bag: (Bag?)bag);
        });
        if (!result.Result.Success)
            return Task.FromResult(result.Result);
        var bag = result.Bag!;
        _notifier.NotifyDataChanged(DataChangeType.BagCreated, bag);
        return Task.FromResult(result.Result);
    }

    public Task<Bag?> GetBagByIdAsync(int id)
        => Task.FromResult(_store.Bags.FirstOrDefault(b => b.Id == id && !b.IsDeleted));

    public Task<List<Bag>> GetBagsForBeanAsync(int beanId, bool includeCompleted = true)
    {
        var query = _store.Bags.Where(b => b.BeanId == beanId && !b.IsDeleted);
        if (!includeCompleted) query = query.Where(b => !b.IsComplete);
        return Task.FromResult(query.OrderByDescending(b => b.RoastDate).ToList());
    }

    public Task<List<BagSummaryDto>> GetActiveBagsForShotLoggingAsync()
    {
        var result = _store.Bags
            .Where(b => !b.IsDeleted && b.IsActive && !b.IsComplete)
            .OrderByDescending(b => b.RoastDate)
            .Select(b =>
            {
                var bean = _store.Beans.FirstOrDefault(bn => bn.Id == b.BeanId);
                var shots = _store.Shots.Where(s => s.BagId == b.Id && !s.IsDeleted).ToList();
                return new BagSummaryDto
                {
                    Id = b.Id, BeanId = b.BeanId, BeanName = bean?.Name ?? "", RoastDate = b.RoastDate,
                    Notes = b.Notes, IsComplete = b.IsComplete, ShotCount = shots.Count,
                    AverageRating = AverageRating(shots),
                };
            }).ToList();
        return Task.FromResult(result);
    }

    public Task<List<BagSummaryDto>> GetBagSummariesForBeanAsync(int beanId, bool includeCompleted = true)
    {
        var query = _store.Bags.Where(b => b.BeanId == beanId && !b.IsDeleted);
        if (!includeCompleted) query = query.Where(b => !b.IsComplete);
        var result = query.OrderByDescending(b => b.RoastDate).Select(b =>
        {
            var bean = _store.Beans.FirstOrDefault(bn => bn.Id == b.BeanId);
            var shots = _store.Shots.Where(s => s.BagId == b.Id && !s.IsDeleted).ToList();
            return new BagSummaryDto
            {
                Id = b.Id, BeanId = b.BeanId, BeanName = bean?.Name ?? "", RoastDate = b.RoastDate,
                Notes = b.Notes, IsComplete = b.IsComplete, ShotCount = shots.Count,
                AverageRating = AverageRating(shots),
            };
        }).ToList();
        return Task.FromResult(result);
    }

    public Task<Bag?> GetMostRecentActiveBagForBeanAsync(int beanId)
        => Task.FromResult(_store.Bags
            .Where(b => b.BeanId == beanId && !b.IsDeleted && !b.IsComplete && b.IsActive)
            .OrderByDescending(b => b.RoastDate)
            .FirstOrDefault());

    public Task<OperationResult<Bag>> UpdateBagAsync(Bag bag)
    {
        if (bag.RoastDate > DateTime.Now)
            return Task.FromResult(OperationResult<Bag>.Fail("Roast date cannot be in the future"));
        if (bag.Notes?.Length > 500)
            return Task.FromResult(OperationResult<Bag>.Fail("Notes cannot exceed 500 characters"));

        var result = _store.ExecuteMutation(() =>
        {
            var existing = _store.Bags.FirstOrDefault(b => b.Id == bag.Id && !b.IsDeleted);
            if (existing == null)
                return (Result: OperationResult<Bag>.Fail("Bag not found"), Bag: (Bag?)null);
            existing.RoastDate = bag.RoastDate;
            existing.Notes = bag.Notes;
            existing.IsComplete = bag.IsComplete;
            existing.LastModifiedAt = DateTime.UtcNow;
            _store.SaveChanges();
            return (Result: OperationResult<Bag>.Ok(existing), Bag: (Bag?)existing);
        });
        if (!result.Result.Success)
            return Task.FromResult(result.Result);
        var existing = result.Bag!;
        _notifier.NotifyDataChanged(DataChangeType.BagUpdated, existing);
        return Task.FromResult(result.Result);
    }

    public Task MarkBagCompleteAsync(int id)
    {
        var bag = _store.ExecuteMutation(() =>
        {
            var existing = _store.Bags.FirstOrDefault(b => b.Id == id && !b.IsDeleted)
                ?? throw new KeyNotFoundException($"Bag {id} not found");
            existing.IsComplete = true;
            existing.LastModifiedAt = DateTime.UtcNow;
            _store.SaveChanges();
            return existing;
        });
        _notifier.NotifyDataChanged(DataChangeType.BagUpdated, bag);
        return Task.CompletedTask;
    }

    public Task ReactivateBagAsync(int id)
    {
        var bag = _store.ExecuteMutation(() =>
        {
            var existing = _store.Bags.FirstOrDefault(b => b.Id == id && !b.IsDeleted)
                ?? throw new KeyNotFoundException($"Bag {id} not found");
            existing.IsComplete = false;
            existing.LastModifiedAt = DateTime.UtcNow;
            existing.IsActive = true;
            _store.SaveChanges();
            return existing;
        });
        _notifier.NotifyDataChanged(DataChangeType.BagUpdated, bag);
        return Task.CompletedTask;
    }

    public Task DeleteBagAsync(int id)
    {
        _store.ExecuteMutation(() =>
        {
            var bag = _store.Bags.FirstOrDefault(b => b.Id == id && !b.IsDeleted)
                ?? throw new KeyNotFoundException($"Bag {id} not found");
            bag.IsDeleted = true;
            bag.LastModifiedAt = DateTime.UtcNow;
            _store.SaveChanges();
        });
        return Task.CompletedTask;
    }

    private static double? AverageRating(List<ShotRecord> shots)
    {
        var ratings = shots.Where(shot => shot.Rating.HasValue).Select(shot => (double)shot.Rating!.Value).ToList();
        return ratings.Count == 0 ? null : ratings.Average();
    }
}
