using System;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services;

public class DataChangedEventArgs : EventArgs
{
    public DataChangeType ChangeType { get; }
    public object? Entity { get; }
    public DataChangedEventArgs(DataChangeType changeType, object? entity = null)
    {
        ChangeType = changeType;
        Entity = entity;
    }
}

public interface IDataChangeNotifier
{
    event EventHandler<DataChangedEventArgs>? DataChanged;
    void NotifyDataChanged(DataChangeType changeType, object? entity = null);
}
