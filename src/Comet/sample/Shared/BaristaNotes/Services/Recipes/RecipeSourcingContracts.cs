using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services.Recipes;

public interface IRecipeSourcingService
{
    Task<RecipeSourcingResult> SourceRecipesAsync(
        int beanId,
        CancellationToken cancellationToken = default);
}

public enum RecipeSourcingStatus
{
    Success,
    NoMatch,
    Cancelled,
    Unavailable,
    Failed
}

public sealed class RecipeSourcingResult : IReadOnlyList<RecipeDto>
{
    public RecipeSourcingStatus Status { get; init; }
    public IReadOnlyList<RecipeDto> Recipes { get; init; } = [];
    public string? Source { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Success => Status == RecipeSourcingStatus.Success;
    public int Count => Recipes.Count;
    public RecipeDto this[int index] => Recipes[index];
    public IEnumerator<RecipeDto> GetEnumerator() => Recipes.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class RecipeSourcingErrors
{
    public const string Cancelled = "RECIPE_CANCELLED";
    public const string Unavailable = "RECIPE_SOURCE_UNAVAILABLE";
    public const string Failed = "RECIPE_SOURCE_FAILED";
    public const string BeanNotFound = "BEAN_NOT_FOUND";
}

public interface IRecipeSourcingLogger
{
    void LogFailure(
        int beanId,
        string source,
        RecipeSourcingStatus status,
        string message,
        Exception? exception = null);
}

public sealed class ConsoleRecipeSourcingLogger : IRecipeSourcingLogger
{
    public static ConsoleRecipeSourcingLogger Instance { get; } = new();

    private ConsoleRecipeSourcingLogger()
    {
    }

    public void LogFailure(
        int beanId,
        string source,
        RecipeSourcingStatus status,
        string message,
        Exception? exception = null)
    {
        var detail = exception is null
            ? string.Empty
            : $" ({exception.GetType().Name}: {exception.Message})";
        Console.Error.WriteLine(
            $"Recipe sourcing {status} for bean {beanId} from {source}: {message}{detail}");
    }
}

public interface IRoasterRecipeAdapter
{
    string Id { get; }
    string RoasterName { get; }
    bool CanHandle(Bean bean);
    Task<IReadOnlyList<ScrapedRecipe>> FetchAsync(Bean bean, CancellationToken cancellationToken);
}

public interface IRoasterRecipeAdapterRegistry
{
    IReadOnlyList<IRoasterRecipeAdapter> All { get; }
    IRoasterRecipeAdapter? FindAdapter(Bean bean);
}

public interface IAIRecipeGenerator
{
    bool IsAvailable { get; }
    Task<IReadOnlyList<ScrapedRecipe>> GenerateAsync(Bean bean, CancellationToken cancellationToken);
}

public sealed record ScrapedRecipe
{
    public required BrewMethod BrewMethod { get; init; }
    public string? Title { get; init; }
    public string? SourceUrl { get; init; }
    public decimal? DoseIn { get; init; }
    public decimal? OutputAmount { get; init; }
    public string? GrindHint { get; init; }
    public decimal? BrewTempC { get; init; }
    public decimal? TotalTimeSeconds { get; init; }
    public string? ParametersJson { get; init; }
    public string? Notes { get; init; }
}

public sealed class NullAIRecipeGenerator : IAIRecipeGenerator
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<ScrapedRecipe>> GenerateAsync(
        Bean bean,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ScrapedRecipe>>([]);
    }
}
