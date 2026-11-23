using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services.Recipes;

public sealed class RecipeSourcingService(
    IBaristaDataStore store,
    IRecipeService recipeService,
    IRoasterRecipeAdapterRegistry registry,
    IAIRecipeGenerator? aiGenerator = null,
    IRecipeSourcingLogger? logger = null) : IRecipeSourcingService
{
    private readonly IRecipeSourcingLogger _logger =
        logger ?? ConsoleRecipeSourcingLogger.Instance;

    public async Task<RecipeSourcingResult> SourceRecipesAsync(
        int beanId,
        CancellationToken cancellationToken = default)
    {
        var sourceName = "registry";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bean = store.Beans.Find(item => item.Id == beanId && !item.IsDeleted);
            if (bean is null)
            {
                const string message = "Bean not found.";
                _logger.LogFailure(
                    beanId,
                    sourceName,
                    RecipeSourcingStatus.Failed,
                    message);
                return new RecipeSourcingResult
                {
                    Status = RecipeSourcingStatus.Failed,
                    ErrorCode = RecipeSourcingErrors.BeanNotFound,
                    ErrorMessage = message,
                    Source = sourceName
                };
            }

            var source = RecipeSource.RoasterSite;
            IReadOnlyList<ScrapedRecipe> sourced = [];
            var adapter = registry.FindAdapter(bean);
            if (adapter is not null)
            {
                sourceName = adapter.Id;
                sourced = await adapter.FetchAsync(bean, cancellationToken);
            }

            if (sourced.Count == 0)
            {
                if (aiGenerator is not { IsAvailable: true })
                {
                    if (adapter is not null)
                    {
                        return new RecipeSourcingResult
                        {
                            Status = RecipeSourcingStatus.NoMatch,
                            Source = sourceName
                        };
                    }

                    const string message = "No configured recipe source supports this bean.";
                    _logger.LogFailure(
                        beanId,
                        sourceName,
                        RecipeSourcingStatus.Unavailable,
                        message);
                    return new RecipeSourcingResult
                    {
                        Status = RecipeSourcingStatus.Unavailable,
                        ErrorCode = RecipeSourcingErrors.Unavailable,
                        ErrorMessage = message,
                        Source = sourceName
                    };
                }

                sourceName = "ai";
                sourced = await aiGenerator.GenerateAsync(bean, cancellationToken);
                source = RecipeSource.AIGenerated;
                if (sourced.Count == 0)
                {
                    return new RecipeSourcingResult
                    {
                        Status = RecipeSourcingStatus.NoMatch,
                        Source = sourceName
                    };
                }
            }

            var persisted = new List<RecipeDto>(sourced.Count);
            foreach (var recipe in sourced)
            {
                cancellationToken.ThrowIfCancellationRequested();
                persisted.Add(await recipeService.UpsertFromSourceAsync(new CreateRecipeDto
                {
                    BeanId = bean.Id,
                    BrewMethod = recipe.BrewMethod,
                    Source = source,
                    SourceUrl = recipe.SourceUrl,
                    Title = recipe.Title,
                    DoseIn = recipe.DoseIn,
                    OutputAmount = recipe.OutputAmount,
                    GrindHint = recipe.GrindHint,
                    BrewTempC = recipe.BrewTempC,
                    TotalTimeSeconds = recipe.TotalTimeSeconds,
                    ParametersJson = recipe.ParametersJson,
                    Notes = recipe.Notes
                }));
            }

            return new RecipeSourcingResult
            {
                Status = RecipeSourcingStatus.Success,
                Recipes = persisted,
                Source = sourceName
            };
        }
        catch (OperationCanceledException exception)
        {
            var status = cancellationToken.IsCancellationRequested
                ? RecipeSourcingStatus.Cancelled
                : RecipeSourcingStatus.Unavailable;
            var message = status == RecipeSourcingStatus.Cancelled
                ? "Recipe sourcing was cancelled."
                : "The recipe source timed out.";
            _logger.LogFailure(beanId, sourceName, status, message, exception);
            return new RecipeSourcingResult
            {
                Status = status,
                ErrorCode = status == RecipeSourcingStatus.Cancelled
                    ? RecipeSourcingErrors.Cancelled
                    : RecipeSourcingErrors.Unavailable,
                ErrorMessage = message,
                Source = sourceName
            };
        }
        catch (HttpRequestException exception)
        {
            const string message = "The recipe source is unavailable.";
            _logger.LogFailure(
                beanId,
                sourceName,
                RecipeSourcingStatus.Unavailable,
                message,
                exception);
            return new RecipeSourcingResult
            {
                Status = RecipeSourcingStatus.Unavailable,
                ErrorCode = RecipeSourcingErrors.Unavailable,
                ErrorMessage = message,
                Source = sourceName
            };
        }
        catch (Exception exception)
        {
            const string message = "Recipe sourcing failed.";
            _logger.LogFailure(
                beanId,
                sourceName,
                RecipeSourcingStatus.Failed,
                message,
                exception);
            return new RecipeSourcingResult
            {
                Status = RecipeSourcingStatus.Failed,
                ErrorCode = RecipeSourcingErrors.Failed,
                ErrorMessage = message,
                Source = sourceName
            };
        }
    }
}
