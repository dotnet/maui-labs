using System;
using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Services;

public sealed class DataChangeNotifier : IDataChangeNotifier
{
    public event EventHandler<DataChangedEventArgs>? DataChanged;

    public void NotifyDataChanged(DataChangeType changeType, object? entity = null)
        => DataChanged?.Invoke(this, new DataChangedEventArgs(changeType, entity));
}
