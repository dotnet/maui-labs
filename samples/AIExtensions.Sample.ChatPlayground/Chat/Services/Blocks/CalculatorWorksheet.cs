using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Every <c>calculate</c> call of a response as one activity: a worksheet with a row per calculation, which fills in as
/// the results arrive. <see cref="CalculatorWorksheetHandler"/> creates and updates it.
/// </summary>
public sealed class CalculatorWorksheetBlock : ActivityContentBlock
{
    /// <summary>Initializes an empty worksheet.</summary>
    public CalculatorWorksheetBlock() => ActivityType = "calculator-worksheet";

    /// <summary>Gets a row per call, in the order the calls arrived. Views bind to it, so it is observable.</summary>
    public ObservableCollection<CalculationRow> Rows { get; } = [];

    /// <inheritdoc />
    protected override bool Abandon()
    {
        var abandoned = false;
        foreach (var row in Rows)
        {
            if (row.Abandon())
                abandoned = true;
        }

        return abandoned;
    }

    internal CalculationRow? Find(string callId) =>
        Rows.FirstOrDefault(row => string.Equals(row.CallId, callId, StringComparison.Ordinal));
}

/// <summary>One calculation of a <see cref="CalculatorWorksheetBlock"/>: a <c>calculate</c> call and its result.</summary>
/// <param name="call">The call the row shows.</param>
public sealed class CalculationRow(FunctionCallContent call) : ObservableObject
{
    private FunctionResultContent? _result;
    private bool _abandoned;

    /// <summary>Gets the call the row shows.</summary>
    public FunctionCallContent Call { get; } = call;

    /// <summary>Gets the call's id, which its result carries.</summary>
    public string CallId => Call.CallId;

    /// <summary>Gets the operands and the operation, such as <c>12 × 7</c>.</summary>
    public string Expression { get; } = CalculatorDisplay.FormatExpression(call.Arguments) ?? "\u2026";

    /// <summary>Gets the result, once it arrived.</summary>
    public FunctionResultContent? Result
    {
        get => _result;
        internal set
        {
            if (!SetProperty(ref _result, value))
                return;

            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(IsAbandoned));
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(Answer));
            OnPropertyChanged(nameof(HasAnswer));
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(HasMessage));
            OnPropertyChanged(nameof(IsError));
        }
    }

    /// <summary>Gets whether the result arrived.</summary>
    public bool HasResult => Result is not null;

    /// <summary>Gets whether the row will never get a result, because its turn ended without one.</summary>
    public bool IsAbandoned => _abandoned && !HasResult;

    /// <summary>Gets whether the row still waits for its result.</summary>
    public bool IsPending => !HasResult && !_abandoned;

    /// <summary>Gets the answer, such as <c>= 84</c>, once a number arrived.</summary>
    public string? Answer => CalculatorDisplay.FormatResult(Result) is { } number ? $"= {number}" : null;

    /// <summary>Gets whether a number arrived.</summary>
    public bool HasAnswer => Answer is not null;

    /// <summary>
    /// Gets what arrived instead of a number: why the calculation failed, such as a division by zero, or that it never
    /// ran.
    /// </summary>
    public string? Message => CalculatorDisplay.FormatMessage(Result);

    /// <summary>Gets whether something other than a number arrived.</summary>
    public bool HasMessage => Message is not null;

    /// <summary>Gets whether the calculation failed, such as a division by zero.</summary>
    public bool IsError => Result is not null && CalculatorDisplay.FormatError(Result) is not null;

    internal bool Abandon()
    {
        if (_abandoned || HasResult)
            return false;

        _abandoned = true;
        OnPropertyChanged(nameof(IsAbandoned));
        OnPropertyChanged(nameof(IsPending));
        return true;
    }
}

/// <summary>
/// Folds the <c>calculate</c> calls of a response, and their results, into one <see cref="CalculatorWorksheetBlock"/>
/// instead of a card per call.
/// </summary>
/// <remarks>
/// The first update with a call creates the worksheet. The engine then offers the worksheet's handler every later
/// update of the response first, while the activity is active: further calls add rows, and each result fills in its
/// row. The worksheet is not one of the engine's function blocks, so the handler claims the results of its calls
/// itself; nothing else would show them. A result for any other call is left to the other handlers.
/// </remarks>
public sealed class CalculatorWorksheetHandler : ActivityHandler<CalculatorWorksheetBlock>
{
    /// <summary>The name of the tool whose calls the worksheet shows.</summary>
    public const string ToolName = "calculate";

    /// <inheritdoc />
    protected override bool TryCreateBlock(BlockMappingContext context, CalculatorWorksheetBlock block) =>
        Apply(context, block);

    /// <inheritdoc />
    protected override bool TryUpdateBlock(
        BlockMappingContext context, CalculatorWorksheetBlock block, out bool isCompleted)
    {
        // The response may calculate again, so the worksheet stays open until the response ends.
        isCompleted = false;
        return Apply(context, block);
    }

    private static bool Apply(BlockMappingContext context, CalculatorWorksheetBlock block)
    {
        var changed = false;
        foreach (var content in context.UnhandledContents)
        {
            switch (content)
            {
                case FunctionCallContent { Name: ToolName } call:
                    context.MarkHandled(call);
                    if (block.Find(call.CallId) is null)
                        block.Rows.Add(new CalculationRow(call));
                    changed = true;
                    break;

                case FunctionResultContent result when block.Find(result.CallId) is { } row:
                    context.MarkHandled(result);
                    row.Result = result;
                    changed = true;
                    break;
            }
        }

        return changed;
    }
}
