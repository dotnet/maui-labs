using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml.Diagnostics;
using Microsoft.Maui.DevFlow.Agent.Core.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class DevFlowAgentService
{
    private DiagnosticProblemStore _problemStore = null!;
    private int _problemsNotificationScheduled;
    private bool _bindingDiagnosticsSubscribed;

    private void InitializeDiagnostics()
    {
        _problemStore = new DiagnosticProblemStore(_options.MaxDiagnosticProblems);
        if (!_options.EnableBindingProblems)
            return;

        if (_options.EnableMauiDiagnostics)
            AppContext.SetSwitch("Microsoft.Maui.RuntimeFeature.EnableMauiDiagnostics", true);

        BindingDiagnostics.BindingFailed += OnBindingFailed;
        _bindingDiagnosticsSubscribed = true;
    }

    private void DisposeDiagnostics()
    {
        if (_bindingDiagnosticsSubscribed)
        {
            BindingDiagnostics.BindingFailed -= OnBindingFailed;
            _bindingDiagnosticsSubscribed = false;
        }
    }

    private void OnBindingFailed(object? sender, BindingBaseErrorEventArgs args)
    {
        if (_disposed || !_options.EnableBindingProblems)
            return;

        var now = DateTime.UtcNow;
        var extended = args as BindingErrorEventArgs;
        var binding = args.Binding as Binding;
        var sourceInfo = args.XamlSourceInfo;
        var target = extended?.Target;
        string? elementId = null;

        if (target is IVisualTreeElement visualTarget && _app is not null)
        {
            try
            {
                // Use only the map from the most recent explicit tree walk. Walking the tree from
                // inside a binding-failure callback can recursively trigger more bindings.
                elementId = _treeWalker.GetIdForElement(visualTarget);
            }
            catch
            {
                // Binding diagnostics must never destabilize the app.
            }
        }

        var message = BuildBindingProblemMessage(args.ErrorCode, binding, extended);
        var problemKey = string.Join(
            "|",
            "binding",
            args.ErrorCode,
            elementId,
            extended?.TargetProperty?.PropertyName,
            sourceInfo?.SourceUri?.ToString(),
            sourceInfo?.LineNumber,
            message);
        var problem = new DiagnosticProblem
        {
            Id = DiagnosticRedactor.StableId(problemKey),
            Kind = "binding",
            Severity = "warning",
            Code = args.ErrorCode,
            Message = message,
            FirstSeenUtc = now,
            LastSeenUtc = now,
            ElementId = elementId,
            ElementType = target?.GetType().FullName,
            Property = extended?.TargetProperty?.PropertyName,
            BindingType = args.Binding?.GetType().FullName,
            BindingPath = binding?.Path,
            BindingMode = binding?.Mode.ToString(),
            SourceType = extended?.Source?.GetType().FullName,
            ConverterType = binding?.Converter?.GetType().FullName,
            SourceFile = GetBindingSourceFile(sourceInfo?.SourceUri),
            SourceLine = sourceInfo?.LineNumber,
            SourceColumn = sourceInfo?.LinePosition
        };

        _problemStore.Add(problem);
        ScheduleProblemsChanged();
    }

    internal static string? GetBindingSourceFile(Uri? sourceUri)
        => sourceUri is null
            ? null
            : sourceUri.IsAbsoluteUri
                ? sourceUri.LocalPath
                : sourceUri.OriginalString;

    private void ScheduleProblemsChanged()
    {
        if (Interlocked.Exchange(ref _problemsNotificationScheduled, 1) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100).ConfigureAwait(false);
                var snapshot = _problemStore.Snapshot(
                    _options.EnableBindingProblems,
                    limit: 1);
                PublishUiEvent("problemsChange", new
                {
                    revision = snapshot.Revision,
                    count = snapshot.Count,
                    timestamp = DateTimeOffset.UtcNow.ToString("O")
                });
                Interlocked.Exchange(ref _problemsNotificationScheduled, 0);

                var latest = _problemStore.Snapshot(
                    _options.EnableBindingProblems,
                    limit: 1);
                if (latest.Revision != snapshot.Revision)
                    ScheduleProblemsChanged();
            }

            catch
            {
                Interlocked.Exchange(ref _problemsNotificationScheduled, 0);
            }
        });
    }

    private static string BuildBindingProblemMessage(
        string? errorCode,
        Binding? binding,
        BindingErrorEventArgs? error)
    {
        static string Metadata(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;
            var safe = new string(value
                .Where(static character => !char.IsControl(character))
                .Take(256)
                .ToArray());
            return safe.Length == 0 ? fallback : safe;
        }

        var code = Metadata(errorCode, "unknown");
        var path = Metadata(binding?.Path, "(unknown path)");
        var targetType = Metadata(error?.Target?.GetType().FullName, "target");
        var property = Metadata(error?.TargetProperty?.PropertyName, "property");
        return $"MAUI binding failure {code}: '{path}' could not update {targetType}.{property}.";
    }

    private Task<HttpResponse> HandleDiagnosticProblems(HttpRequest request)
    {
        var limit = int.TryParse(request.QueryParams.GetValueOrDefault("limit", "100"), out var parsed)
            ? Math.Clamp(parsed, 1, _options.MaxDiagnosticProblems)
            : Math.Min(100, _options.MaxDiagnosticProblems);
        var elementId = request.QueryParams.GetValueOrDefault("elementId");
        return Task.FromResult(HttpResponse.Json(
            _problemStore.Snapshot(_options.EnableBindingProblems, limit, elementId)));
    }

    private Task<HttpResponse> HandleDiagnosticProblemsClear(HttpRequest request)
    {
        _problemStore.Clear();
        ScheduleProblemsChanged();
        return Task.FromResult(HttpResponse.Json(new { success = true }));
    }
}
