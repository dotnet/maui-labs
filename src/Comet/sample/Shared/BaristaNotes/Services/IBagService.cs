using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CometBaristaNotes.Models;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public interface IBagService
{
    Task<OperationResult<Bag>> CreateBagAsync(Bag bag);
    Task<OperationResult<BagSummaryDto>> CreateNewBagForBeanAsync(int beanId, DateTime roastDate, string? notes = null);
    Task<Bag?> GetBagByIdAsync(int id);
    Task<List<Bag>> GetBagsForBeanAsync(int beanId, bool includeCompleted = true);
    Task<List<BagSummaryDto>> GetActiveBagsForShotLoggingAsync();
    Task<List<BagSummaryDto>> GetBagSummariesForBeanAsync(int beanId, bool includeCompleted = true);
    Task<Bag?> GetMostRecentActiveBagForBeanAsync(int beanId);
    Task<OperationResult<Bag>> UpdateBagAsync(Bag bag);
    Task MarkBagCompleteAsync(int id);
    Task ReactivateBagAsync(int id);
    Task DeleteBagAsync(int id);
}
