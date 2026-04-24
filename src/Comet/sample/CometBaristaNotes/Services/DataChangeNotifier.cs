namespace CometBaristaNotes.Services;

public class DataChangeNotifier : IDataChangeNotifier
{
	public event Action<string, int, DataChangeType>? DataChanged;

	public void NotifyChange(string entityType, int entityId, DataChangeType changeType)
	{
		DataChanged?.Invoke(entityType, entityId, changeType);
	}
}
