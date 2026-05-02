namespace CometDigitsGame;

/// <summary>
/// Game state managed via Component<TState> pattern.
/// </summary>
class GameState
{
	public GameModel CurrentGame { get; set; } = GameModel.FirstGame;
	public Stack<GameNumber[]> BoardStates { get; set; } = new();
	public Stack<OperationItem> Operations { get; set; } = new();
	public Operation? CurrentOperation { get; set; }
	public GameNumber? CurrentNumber { get; set; }
	public OperationItem? OperationInError { get; set; }
	public PageView CurrentPageView { get; set; } = PageView.GameBoard;

	public GameNumber[] CurrentBoard =>
		BoardStates.Count > 0 ? BoardStates.Peek() : CurrentGame.Values;

	public bool IsWon => CurrentBoard.Any(n => n.Value == CurrentGame.TargetValue);
}
