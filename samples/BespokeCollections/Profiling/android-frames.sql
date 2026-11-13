SELECT count(*) AS workload_spans,
       min(ts) AS start_ns,
       max(ts + dur) AS end_ns,
       sum(dur) / 1e9 AS duration_seconds
FROM slice
WHERE name = 'MasonryScrollWorkload' AND dur > 0;

WITH workload AS (
  SELECT ts, dur
  FROM slice
  WHERE name = 'MasonryScrollWorkload' AND dur > 0
),
app AS (
  SELECT f.*
  FROM actual_frame_timeline_slice f
  JOIN process p USING (upid), workload
  WHERE p.name = 'org.dotnet.mauilabs.bespokecollections'
    AND f.ts >= workload.ts AND f.ts < workload.ts + workload.dur
    AND f.dur > 0
),
display AS (
  SELECT d.display_frame_token, d.ts + d.dur AS present_ns
  FROM actual_frame_timeline_slice d
  JOIN process p USING (upid)
  WHERE p.name = '/system/bin/surfaceflinger' AND d.dur > 0
),
intervals AS (
  SELECT present_ns - lag(present_ns) OVER (ORDER BY present_ns) AS delta
  FROM (
    SELECT DISTINCT d.present_ns
    FROM app a JOIN display d USING (display_frame_token)
  )
)
SELECT count(delta) AS presentation_intervals,
       percentile(delta / 1e6, 50) AS presentation_p50_ms,
       percentile(delta / 1e6, 95) AS presentation_p95_ms,
       percentile(delta / 1e6, 99) AS presentation_p99_ms,
       max(delta) / 1e6 AS presentation_max_ms
FROM intervals WHERE delta > 0;

WITH workload AS (
  SELECT ts, dur
  FROM slice
  WHERE name = 'MasonryScrollWorkload' AND dur > 0
)
SELECT count(*) AS app_frames,
       percentile(f.dur / 1e6, 50) AS app_duration_p50_ms,
       percentile(f.dur / 1e6, 95) AS app_duration_p95_ms,
       percentile(f.dur / 1e6, 99) AS app_duration_p99_ms,
       max(f.dur) / 1e6 AS app_duration_max_ms,
       sum(f.on_time_finish = 0) AS app_finish_late
FROM actual_frame_timeline_slice f
JOIN process p USING (upid), workload
WHERE p.name = 'org.dotnet.mauilabs.bespokecollections'
  AND f.ts >= workload.ts AND f.ts < workload.ts + workload.dur
  AND f.dur > 0;

WITH workload AS (
  SELECT ts, dur
  FROM slice
  WHERE name = 'MasonryScrollWorkload' AND dur > 0
)
SELECT f.jank_type, f.present_type, f.layer_name, count(*) AS app_frames
FROM actual_frame_timeline_slice f
JOIN process p USING (upid), workload
WHERE p.name = 'org.dotnet.mauilabs.bespokecollections'
  AND f.ts >= workload.ts AND f.ts < workload.ts + workload.dur
  AND f.dur > 0
GROUP BY f.jank_type, f.present_type, f.layer_name;

SELECT name, value
FROM stats
WHERE severity = 'error' AND value > 0;
