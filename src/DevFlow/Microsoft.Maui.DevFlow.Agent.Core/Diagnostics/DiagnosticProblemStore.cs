namespace Microsoft.Maui.DevFlow.Agent.Core.Diagnostics;

internal sealed class DiagnosticProblemStore
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, DiagnosticProblem> _problems = new(StringComparer.Ordinal);
    private long _revision;
    private long _evicted;

    public DiagnosticProblemStore(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public (long Revision, int Count) Add(DiagnosticProblem problem)
    {
        lock (_gate)
        {
            if (_problems.TryGetValue(problem.Id, out var existing))
            {
                existing.Count++;
                existing.LastSeenUtc = problem.LastSeenUtc;
                existing.Message = problem.Message;
                existing.ElementId ??= problem.ElementId;
                existing.SourceFile ??= problem.SourceFile;
                existing.SourceLine ??= problem.SourceLine;
                existing.SourceColumn ??= problem.SourceColumn;
            }
            else
            {
                if (_problems.Count >= _capacity)
                {
                    var oldest = _problems.Values.MinBy(static item => item.LastSeenUtc);
                    if (oldest is not null)
                    {
                        _problems.Remove(oldest.Id);
                        _evicted++;
                    }
                }

                _problems[problem.Id] = problem;
            }

            _revision++;
            return (_revision, _problems.Count);
        }
    }

    public DiagnosticProblemBatch Snapshot(bool enabled, int limit, string? elementId = null)
    {
        lock (_gate)
        {
            var problems = _problems.Values
                .Where(problem => string.IsNullOrWhiteSpace(elementId)
                    || string.Equals(problem.ElementId, elementId, StringComparison.Ordinal))
                .OrderByDescending(static problem => problem.LastSeenUtc)
                .Take(Math.Clamp(limit, 1, _capacity))
                .Select(Clone)
                .ToList();

            return new DiagnosticProblemBatch
            {
                Enabled = enabled,
                Revision = _revision,
                Count = _problems.Count,
                Evicted = _evicted,
                Problems = problems
            };
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _problems.Clear();
            _revision++;
        }
    }

    private static DiagnosticProblem Clone(DiagnosticProblem problem)
        => new()
        {
            Id = problem.Id,
            Kind = problem.Kind,
            Severity = problem.Severity,
            Code = problem.Code,
            Message = problem.Message,
            Count = problem.Count,
            FirstSeenUtc = problem.FirstSeenUtc,
            LastSeenUtc = problem.LastSeenUtc,
            ElementId = problem.ElementId,
            ElementType = problem.ElementType,
            Property = problem.Property,
            BindingType = problem.BindingType,
            BindingPath = problem.BindingPath,
            BindingMode = problem.BindingMode,
            SourceType = problem.SourceType,
            ConverterType = problem.ConverterType,
            SourceFile = problem.SourceFile,
            SourceLine = problem.SourceLine,
            SourceColumn = problem.SourceColumn
        };
}
