namespace CometBaristaNotes.Services;

public enum DataChangeType { Created, Updated, Deleted }

public interface IDataChangeNotifier
{
	event Action<string, int, DataChangeType> DataChanged;
	void NotifyChange(string entityType, int entityId, DataChangeType changeType);
}
