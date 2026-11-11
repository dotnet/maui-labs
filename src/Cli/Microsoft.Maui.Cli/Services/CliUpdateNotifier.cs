// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text.Json;

namespace Microsoft.Maui.Cli.Services;

internal sealed class CliUpdateNotifier
{
	internal const string FeedUrl = "https://api.nuget.org/v3/index.json";
	static readonly HttpClient SharedClient = new();
	readonly HttpClient _httpClient;
	readonly Func<DateTimeOffset> _clock;
	readonly string _cachePath;

	internal CliUpdateNotifier(HttpClient? httpClient = null, Func<DateTimeOffset>? clock = null, string? cachePath = null)
	{
		_httpClient = httpClient ?? SharedClient;
		_clock = clock ?? (() => DateTimeOffset.UtcNow);
		_cachePath = cachePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".maui", "cli-update-check-v2.json");
	}

	// Eligibility, opt-out, and printing the returned notice belong to the caller.
	internal async Task<string?> CheckAsync(string currentVersion, bool verbose)
	{
		currentVersion = StripMetadata(currentVersion);
		var now = _clock().ToUniversalTime();
		Cache? cache = null;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_cachePath))!);
			FileStream cacheLock;
			try
			{
				cacheLock = new FileStream(_cachePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			}
			catch (IOException exception)
			{
				Report(exception, verbose);
				return null;
			}

			using (cacheLock)
			{
				cache = ReadCache(verbose);
				if (cache?.CurrentVersion != currentVersion)
					cache = null;
				if (cache is not null && cache.LastAttemptUtc <= now && cache.LastAttemptUtc.UtcDateTime.Date == now.UtcDateTime.Date)
					return Notice(cache, currentVersion, now);

				cache = new Cache(currentVersion, now, cache?.LatestCandidate, cache?.LastSuccessUtc);
				// Persist before contacting NuGet so offline invocations do not hammer the feed.
				WriteCache(cache);
				using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
				var versionsUrl = await GetVersionsUrlAsync(timeout.Token);
				using var response = await _httpClient.GetAsync(versionsUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
				response.EnsureSuccessStatusCode();
				using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
				using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
				if (document.RootElement.ValueKind != JsonValueKind.Object ||
					!document.RootElement.TryGetProperty("data", out var versions) || versions.ValueKind != JsonValueKind.Array)
					throw new JsonException("The CLI update feed did not contain a version data array.");

				string? latest = null;
				foreach (var element in versions.EnumerateArray())
				{
					if (element.ValueKind != JsonValueKind.String)
						throw new JsonException("The CLI update feed contained a non-string version.");
					var candidate = StripMetadata(element.GetString()!);
					if (string.IsNullOrWhiteSpace(candidate) ||
						(!currentVersion.Contains('-') && candidate.Contains('-')) ||
						MauiVersionFeedService.MauiVersionComparer.Compare(candidate, currentVersion) <= 0)
						continue;
					if (latest is null || MauiVersionFeedService.MauiVersionComparer.Compare(candidate, latest) > 0)
						latest = candidate;
				}

				cache = cache with { LatestCandidate = latest, LastSuccessUtc = now };
				WriteCache(cache);
				return Notice(cache, currentVersion, now);
			}
		}
		catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or
			JsonException or IOException or UnauthorizedAccessException)
		{
			Report(exception, verbose);
			return Notice(cache, currentVersion, now);
		}
	}

	async Task<string> GetVersionsUrlAsync(CancellationToken cancellationToken)
	{
		using var response = await _httpClient.GetAsync(FeedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
		if (document.RootElement.ValueKind != JsonValueKind.Object ||
			!document.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
			throw new JsonException("The NuGet service index did not contain resources.");
		foreach (var resource in resources.EnumerateArray())
		{
			if (resource.ValueKind != JsonValueKind.Object ||
				!resource.TryGetProperty("@type", out var type) || type.ValueKind != JsonValueKind.String ||
				type.GetString() is not ("SearchAutocompleteService" or "SearchAutocompleteService/3.0.0-beta" or "SearchAutocompleteService/3.0.0-rc" or "SearchAutocompleteService/3.5.0") ||
				!resource.TryGetProperty("@id", out var id) || id.ValueKind != JsonValueKind.String)
				continue;
			if (!Uri.TryCreate(id.GetString(), UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
				throw new JsonException("The NuGet version service requires an absolute HTTPS endpoint.");
			// Autocomplete excludes unlisted versions; the flat-container index does not.
			return endpoint.AbsoluteUri + (endpoint.Query.Length == 0 ? "?" : "&") +
				"id=Microsoft.Maui.Cli&prerelease=true&semVerLevel=2.0.0";
		}
		throw new JsonException("The NuGet service index did not advertise version autocomplete.");
	}

	Cache? ReadCache(bool verbose)
	{
		if (!File.Exists(_cachePath))
			return null;
		try
		{
			using var document = JsonDocument.Parse(File.ReadAllBytes(_cachePath));
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!root.TryGetProperty("currentVersion", out var current) || current.ValueKind != JsonValueKind.String ||
				!root.TryGetProperty("lastAttemptUtc", out var attempt) || attempt.ValueKind != JsonValueKind.String ||
				!attempt.TryGetDateTimeOffset(out var lastAttempt) ||
				!root.TryGetProperty("latestCandidate", out var candidate) ||
				(candidate.ValueKind != JsonValueKind.String && candidate.ValueKind != JsonValueKind.Null) ||
				!root.TryGetProperty("lastSuccessUtc", out var success) ||
				(success.ValueKind != JsonValueKind.Null &&
					(success.ValueKind != JsonValueKind.String || !success.TryGetDateTimeOffset(out _))))
				throw new JsonException("The CLI update cache is malformed.");
			return new Cache(current.GetString()!, lastAttempt.ToUniversalTime(), candidate.GetString(),
				success.ValueKind == JsonValueKind.Null ? null : success.GetDateTimeOffset().ToUniversalTime());
		}
		catch (JsonException exception)
		{
			Report(exception, verbose);
			return null;
		}
	}

	void WriteCache(Cache cache)
	{
		var pendingPath = _cachePath + "." + Guid.NewGuid().ToString("N") + ".pending";
		try
		{
			using (var stream = new FileStream(pendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			using (var writer = new Utf8JsonWriter(stream))
			{
				writer.WriteStartObject();
				writer.WriteString("currentVersion", cache.CurrentVersion);
				writer.WriteString("lastAttemptUtc", cache.LastAttemptUtc);
				writer.WriteString("latestCandidate", cache.LatestCandidate);
				if (cache.LastSuccessUtc is { } success)
					writer.WriteString("lastSuccessUtc", success);
				else
					writer.WriteNull("lastSuccessUtc");
				writer.WriteEndObject();
			}
			if (File.Exists(_cachePath))
				File.Replace(pendingPath, _cachePath, destinationBackupFileName: null);
			else
				File.Move(pendingPath, _cachePath);
		}
		finally
		{
			File.Delete(pendingPath);
		}
	}

	static string? Notice(Cache? cache, string currentVersion, DateTimeOffset now)
	{
		if (cache?.LatestCandidate is not { } candidate || cache.LastSuccessUtc is not { } success ||
			success > now || now - success > TimeSpan.FromDays(7) ||
			(!currentVersion.Contains('-') && candidate.Contains('-')) ||
			MauiVersionFeedService.MauiVersionComparer.Compare(candidate, currentVersion) <= 0)
			return null;
		return $"A newer version of the MAUI CLI is available ({currentVersion} → {candidate}). Run 'maui update --self' to update.";
	}

	static string StripMetadata(string version) => version.Split('+', 2)[0];

	static void Report(Exception exception, bool verbose)
	{
		var message = $"MAUI CLI update check skipped: {exception.Message}";
		Trace.WriteLine(message);
		if (verbose)
			Console.Error.WriteLine(message);
	}

	sealed record Cache(string CurrentVersion, DateTimeOffset LastAttemptUtc, string? LatestCandidate, DateTimeOffset? LastSuccessUtc);
}
