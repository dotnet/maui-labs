namespace Microsoft.Maui.DevFlow.Agent.Core.Profiling;

public class ProfilerSessionStore
{
    private readonly ProfilerRingBuffer<ProfilerSample> _samples;
    private readonly ProfilerRingBuffer<ProfilerMarker> _markers;
    private readonly ProfilerRingBuffer<ProfilerSpan> _spans;
    private readonly object _gate = new();
    private DateTime _lastSampleTimestampUtc = DateTime.MinValue;
    private DateTime _lastMarkerTimestampUtc = DateTime.MinValue;
    private DateTime _lastSpanTimestampUtc = DateTime.MinValue;
    private ProfilerSessionInfo? _session;

    public ProfilerSessionStore(int maxSamples, int maxMarkers, int maxSpans)
    {
        _samples = new ProfilerRingBuffer<ProfilerSample>(maxSamples);
        _markers = new ProfilerRingBuffer<ProfilerMarker>(maxMarkers);
        _spans = new ProfilerRingBuffer<ProfilerSpan>(maxSpans);
    }

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _session?.IsActive == true;
            }
        }
    }

    public ProfilerSessionInfo? CurrentSession
    {
        get
        {
            lock (_gate)
            {
                return _session;
            }
        }
    }

    public ProfilerSessionInfo Start(int sampleIntervalMs)
    {
        lock (_gate)
        {
            _samples.Clear();
            _markers.Clear();
            _spans.Clear();
            _lastSampleTimestampUtc = DateTime.MinValue;
            _lastMarkerTimestampUtc = DateTime.MinValue;
            _lastSpanTimestampUtc = DateTime.MinValue;
            _session = new ProfilerSessionInfo
            {
                SessionId = Guid.NewGuid().ToString("N"),
                StartedAtUtc = DateTime.UtcNow,
                SampleIntervalMs = sampleIntervalMs,
                IsActive = true
            };
            return _session;
        }
    }

    public ProfilerSessionInfo? Stop()
    {
        lock (_gate)
        {
            if (_session != null)
                _session.IsActive = false;
            return _session;
        }
    }

    public void AddSample(ProfilerSample sample)
    {
        lock (_gate)
        {
            if (_session?.IsActive != true)
                return;

            if (sample.TsUtc <= _lastSampleTimestampUtc)
                sample.TsUtc = _lastSampleTimestampUtc.AddTicks(1);
            _lastSampleTimestampUtc = sample.TsUtc;
            _samples.Add(sample);
        }
    }

    public void AddMarker(ProfilerMarker marker)
    {
        lock (_gate)
        {
            if (_session?.IsActive != true)
                return;

            if (marker.TsUtc <= _lastMarkerTimestampUtc)
                marker.TsUtc = _lastMarkerTimestampUtc.AddTicks(1);
            _lastMarkerTimestampUtc = marker.TsUtc;
            _markers.Add(marker);
        }
    }

    public void AddSpan(ProfilerSpan span)
    {
        lock (_gate)
        {
            if (_session?.IsActive != true)
                return;

            if (span.StartTsUtc == default)
                span.StartTsUtc = DateTime.UtcNow;

            if (span.StartTsUtc <= _lastSpanTimestampUtc)
                span.StartTsUtc = _lastSpanTimestampUtc.AddTicks(1);

            if (span.EndTsUtc == default || span.EndTsUtc < span.StartTsUtc)
                span.EndTsUtc = span.StartTsUtc;

            if (span.SpanId.Length == 0)
                span.SpanId = Guid.NewGuid().ToString("N");

            span.DurationMs = Math.Max(0d, (span.EndTsUtc - span.StartTsUtc).TotalMilliseconds);
            _lastSpanTimestampUtc = span.EndTsUtc;
            _spans.Add(span);
        }
    }

    public List<ProfilerHotspot> GetHotspots(int limit, int minDurationMs, string? kind = null)
    {
        List<ProfilerSpan> spans;
        lock (_gate)
        {
            spans = _spans.ReadAfter(0, _spans.Capacity, out _);
        }

        if (spans.Count == 0)
            return new List<ProfilerHotspot>();

        var filtered = spans
            .Where(span => span.DurationMs >= minDurationMs)
            .Where(span => string.IsNullOrWhiteSpace(kind) || span.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));

        var hotspots = filtered
            .GroupBy(span => new
            {
                span.Kind,
                span.Name,
                Screen = string.IsNullOrWhiteSpace(span.Screen) ? null : span.Screen
            })
            .Select(group =>
            {
                var durations = group
                    .Select(span => span.DurationMs)
                    .OrderBy(value => value)
                    .ToArray();

                var count = durations.Length;
                var p95Index = count <= 1
                    ? 0
                    : Math.Min((int)Math.Ceiling(count * 0.95), count - 1);

                return new ProfilerHotspot
                {
                    Kind = group.Key.Kind,
                    Name = group.Key.Name,
                    Screen = group.Key.Screen,
                    Count = count,
                    ErrorCount = group.Count(span => !span.Status.Equals("ok", StringComparison.OrdinalIgnoreCase)),
                    AvgDurationMs = count == 0 ? 0 : durations.Average(),
                    P95DurationMs = count == 0 ? 0 : durations[p95Index],
                    MaxDurationMs = count == 0 ? 0 : durations[^1]
                };
            })
            .OrderByDescending(h => h.P95DurationMs)
            .ThenByDescending(h => h.MaxDurationMs)
            .ThenByDescending(h => h.Count)
            .Take(Math.Max(1, limit))
            .ToList();

        return hotspots;
    }

    public ProfilerBatch GetBatch(long sampleCursor, long markerCursor, int limit, long spanCursor = 0)
    {
        lock (_gate)
        {
            if (_session == null)
            {
                return new ProfilerBatch
                {
                    SessionId = "",
                    IsActive = false,
                    Samples = new(),
                    Markers = new(),
                    Spans = new(),
                    SampleCursor = 0,
                    MarkerCursor = 0,
                    SpanCursor = 0
                };
            }

            var samples = _samples.ReadAfter(sampleCursor, limit, out var latestSampleCursor);
            var markers = _markers.ReadAfter(markerCursor, limit, out var latestMarkerCursor);
            var spans = _spans.ReadAfter(spanCursor, limit, out var latestSpanCursor);

            return new ProfilerBatch
            {
                SessionId = _session.SessionId,
                IsActive = _session.IsActive,
                Samples = samples,
                Markers = markers,
                Spans = spans,
                SampleCursor = latestSampleCursor,
                MarkerCursor = latestMarkerCursor,
                SpanCursor = latestSpanCursor
            };
        }
    }
}
