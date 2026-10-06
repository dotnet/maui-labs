using System.Collections.ObjectModel;

namespace AIExtensions.Sample.ChatPlayground;

public sealed class ChatDiagnosticGroup(string? traceId) : ObservableCollection<ChatDiagnosticEntry>
{
    public string? TraceId { get; } = traceId;
    public string Title => TraceId is null ? "Uncorrelated" : $"Trace {TraceId}";
    public string AutomationId => $"ChatLogTrace-{TraceId ?? "uncorrelated"}";
}

public sealed class ChatDiagnosticGroups : ObservableCollection<ChatDiagnosticGroup>
{
    public void ApplySnapshot(ChatDiagnosticEntry[] entries)
    {
        var desired = entries.GroupBy(entry => entry.TraceId).ToArray();
        for (var i = Count - 1; i >= 0; i--)
            if (!desired.Any(group => group.Key == this[i].TraceId))
                RemoveAt(i);

        for (var i = 0; i < desired.Length; i++)
        {
            var group = this.FirstOrDefault(group => group.TraceId == desired[i].Key);
            if (group is null)
            {
                group = new ChatDiagnosticGroup(desired[i].Key);
                Insert(i, group);
            }
            else if (IndexOf(group) != i)
                Move(IndexOf(group), i);

            var retained = desired[i].ToArray();
            // The receiver evicts only the oldest entries, preserving append order.
            while (group.Count > 0 && group[0].Sequence < retained[0].Sequence)
                group.RemoveAt(0);
            var last = group.Count == 0 ? -1 : group[^1].Sequence;
            foreach (var entry in retained)
                if (entry.Sequence > last)
                    group.Add(entry);
        }
    }
}
