using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Driver;

internal static class ProcessNameResolver
{
    internal static int? FindUniqueProcessId(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;

        var candidates = new List<(int ProcessId, string ProcessName)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase))
                        candidates.Add((process.Id, process.ProcessName));
                }
                catch
                {
                    // A process may exit or deny metadata access while enumerating.
                }
            }
        }

        return FindUniqueProcessId(processName, candidates);
    }

    internal static int? FindUniqueProcessId(
        string? processName,
        IEnumerable<(int ProcessId, string ProcessName)> candidates)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;

        var matches = candidates
            .Where(candidate => candidate.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var exactMatches = matches
            .Where(candidate => candidate.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (exactMatches.Length == 1)
            return exactMatches[0].ProcessId;
        if (exactMatches.Length > 1)
            return null;

        return matches.Length == 1 ? matches[0].ProcessId : null;
    }
}
