namespace CometDigitsGame;

record GameModel(int TargetValue, GameNumber[] Values)
{
	public static GameModel FirstGame { get; } = new GameModel(58, new[]
	{
		new GameNumber(1, new GameNumberPosition(0, 0), 2),
		new GameNumber(2, new GameNumberPosition(0, 1), 7),
		new GameNumber(3, new GameNumberPosition(0, 2), 9),
		new GameNumber(4, new GameNumberPosition(1, 0), 10),
		new GameNumber(5, new GameNumberPosition(1, 1), 11),
		new GameNumber(6, new GameNumberPosition(1, 2), 25)
	});
}

record GameNumberPosition(int Row, int Column);

record GameNumber(int Id, GameNumberPosition Position, int Value);

enum Operation
{
	Add,
	Subtract,
	Multiply,
	Divide,
}

record OperationItem(GameNumber Left, GameNumber Right, Operation Operation)
{
	internal int CalcValue()
	{
		return Operation switch
		{
			Operation.Add => Left.Value + Right.Value,
			Operation.Subtract => Left.Value - Right.Value,
			Operation.Multiply => Left.Value * Right.Value,
			Operation.Divide => Left.Value / Right.Value,
			_ => throw new NotImplementedException(),
		};
	}

	public bool IsValid()
	{
		return Operation switch
		{
			Operation.Subtract => Left.Value >= Right.Value,
			Operation.Divide => Left.Value % Right.Value == 0,
			_ => true,
		};
	}
}
