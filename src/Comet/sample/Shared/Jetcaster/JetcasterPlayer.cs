#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Comet.Reactive;

namespace CometSamples.Jetcaster
{
	/// <summary>
	/// The gold's MockEpisodePlayer (MockEpisodePlayer.kt:35-112) — playback IS a
	/// timer: a 1s-tick loop advances TimeElapsed until the episode duration, then
	/// auto-advances the queue. No audio stack in the gold either, so this mock is
	/// parity, not a deviation. Signals mirror the gold's EpisodePlayerState flow.
	/// </summary>
	public static class MockEpisodePlayer
	{
		public static readonly Signal<string> CurrentEpisodeUri = new(string.Empty);
		public static readonly Signal<bool> IsPlaying = new(false);
		public static readonly Signal<double> TimeElapsedSeconds = new(0);

		static readonly List<string> Queue = new();
		static CancellationTokenSource? _ticker;

		// One rule for "how long is this episode" everywhere (play limit and seek
		// clamps): unknown duration = unbounded — ticks continue and the queue only
		// auto-advances on a KNOWN completion.
		static double LimitSeconds(Episode? episode) =>
			episode?.Duration?.TotalSeconds ?? double.MaxValue;

		public static void SetCurrent(string episodeUri)
		{
			Pause();
			CurrentEpisodeUri.Value = episodeUri;
			TimeElapsedSeconds.Value = 0;
		}

		public static void AddToQueue(string episodeUri)
		{
			if (!Queue.Contains(episodeUri))
				Queue.Add(episodeUri);
		}

		public static void Play()
		{
			if (IsPlaying.Peek() || PodcastStore.GetEpisode(CurrentEpisodeUri.Peek()) is not { } episode)
				return;
			IsPlaying.Value = true;
			var cts = new CancellationTokenSource();
			_ticker = cts;
			double limit = LimitSeconds(episode);
			_ = Task.Run(async () =>
			{
				while (!cts.IsCancellationRequested)
				{
					try
					{
						await Task.Delay(1000, cts.Token).ConfigureAwait(false);
					}
					catch (OperationCanceledException)
					{
						break;   // paused/advanced — no exception per pause
					}
					// Signals must mutate on the UI thread (the repo-wide contract) —
					// ThreadHelper is wired by every probe, unlike SynchronizationContext.
					Comet.ThreadHelper.RunOnMainThread(() =>
					{
						if (cts.IsCancellationRequested)
							return;
						var next = TimeElapsedSeconds.Peek() + 1;
						if (next >= limit)
						{
							TimeElapsedSeconds.Value = limit;
							Next();   // the gold auto-advances the queue at the end
						}
						else
							TimeElapsedSeconds.Value = next;
					});
				}
			}, cts.Token);
		}

		public static void Pause()
		{
			_ticker?.Cancel();
			_ticker?.Dispose();
			_ticker = null;
			IsPlaying.Value = false;
		}

		public static void SeekBy(double deltaSeconds) =>
			SeekTo(TimeElapsedSeconds.Peek() + deltaSeconds);

		public static void SeekTo(double seconds)
		{
			double limit = LimitSeconds(PodcastStore.GetEpisode(CurrentEpisodeUri.Peek()));
			TimeElapsedSeconds.Value = Math.Clamp(seconds, 0, limit);
		}

		public static void Next()
		{
			Pause();
			if (Queue.Count == 0)
				return;
			var next = Queue[0];
			Queue.RemoveAt(0);
			SetCurrent(next);
			Play();
		}

		public static void Previous()
		{
			// The gold restarts the current episode (no back-queue history).
			TimeElapsedSeconds.Value = 0;
		}

	}
}
