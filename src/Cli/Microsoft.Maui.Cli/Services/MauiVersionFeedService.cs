// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Microsoft.Maui.Cli.Services;

public enum MauiVersionChannel
{
	Stable,
	Nightly,
}

public sealed record MauiPackageFeedVersion
{
	public required string Version { get; init; }
	public bool IsPrerelease { get; init; }
}

public interface IMauiVersionFeedService
{
	Task<IReadOnlyList<MauiPackageFeedVersion>> GetVersionsAsync(
		MauiVersionChannel channel,
		bool includePrerelease,
		CancellationToken cancellationToken = default);

	Task<MauiPackageFeedVersion?> GetLatestVersionAsync(
		MauiVersionChannel channel,
		bool includePrerelease,
		CancellationToken cancellationToken = default);

	string GetFeedUrl(MauiVersionChannel channel);
}

public sealed class MauiVersionFeedService : IMauiVersionFeedService
{
	internal const string PackageId = "Microsoft.Maui.Controls";
	internal const string StableFeedUrl = "https://api.nuget.org/v3/index.json";
	internal const string StableFlatContainerUrl = "https://api.nuget.org/v3-flatcontainer";
	internal const string NightlyFeedUrl = "https://pkgs.dev.azure.com/xamarin/public/_packaging/maui-nightly/nuget/v3/index.json";
	internal const string NightlyFlatContainerUrl = "https://pkgs.dev.azure.com/xamarin/public/_packaging/maui-nightly/nuget/v3/flat2";

	readonly HttpClient _httpClient;

	public MauiVersionFeedService(HttpClient? httpClient = null)
	{
		_httpClient = httpClient ?? new HttpClient();
	}

	public string GetFeedUrl(MauiVersionChannel channel) => channel switch
	{
		MauiVersionChannel.Nightly => NightlyFeedUrl,
		_ => StableFeedUrl,
	};

	public async Task<MauiPackageFeedVersion?> GetLatestVersionAsync(
		MauiVersionChannel channel,
		bool includePrerelease,
		CancellationToken cancellationToken = default)
	{
		var versions = await GetVersionsAsync(channel, includePrerelease, cancellationToken);
		return versions.LastOrDefault();
	}

	public async Task<IReadOnlyList<MauiPackageFeedVersion>> GetVersionsAsync(
		MauiVersionChannel channel,
		bool includePrerelease,
		CancellationToken cancellationToken = default)
	{
		var flatContainerUrl = channel switch
		{
			MauiVersionChannel.Nightly => NightlyFlatContainerUrl,
			_ => StableFlatContainerUrl,
		};

		var url = $"{flatContainerUrl.TrimEnd('/')}/{PackageId.ToLowerInvariant()}/index.json";
		using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

		if (!document.RootElement.TryGetProperty("versions", out var versionsElement) ||
			versionsElement.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidOperationException($"The MAUI package feed did not return a versions array: {url}");
		}

		var versions = new List<MauiPackageFeedVersion>();
		foreach (var versionElement in versionsElement.EnumerateArray())
		{
			var version = versionElement.GetString();
			if (string.IsNullOrWhiteSpace(version))
				continue;

			var isPrerelease = IsPrerelease(version);
			if (channel == MauiVersionChannel.Stable && !includePrerelease && isPrerelease)
				continue;

			versions.Add(new MauiPackageFeedVersion
			{
				Version = version,
				IsPrerelease = isPrerelease,
			});
		}

		versions.Sort((left, right) => MauiVersionComparer.Compare(left.Version, right.Version));
		return versions;
	}

	static bool IsPrerelease(string version) => version.Contains('-', StringComparison.Ordinal);

	internal static class MauiVersionComparer
	{
		public static int Compare(string? left, string? right)
		{
			if (ReferenceEquals(left, right))
				return 0;
			if (left is null)
				return -1;
			if (right is null)
				return 1;

			var leftVersion = ParsedVersion.Parse(left);
			var rightVersion = ParsedVersion.Parse(right);

			var coreComparison = CompareCore(leftVersion.Core, rightVersion.Core);
			if (coreComparison != 0)
				return coreComparison;

			if (leftVersion.Prerelease.Length == 0 && rightVersion.Prerelease.Length == 0)
				return 0;
			if (leftVersion.Prerelease.Length == 0)
				return 1;
			if (rightVersion.Prerelease.Length == 0)
				return -1;

			return ComparePrerelease(leftVersion.Prerelease, rightVersion.Prerelease);
		}

		static int CompareCore(int[] left, int[] right)
		{
			var count = Math.Max(left.Length, right.Length);
			for (var i = 0; i < count; i++)
			{
				var leftValue = i < left.Length ? left[i] : 0;
				var rightValue = i < right.Length ? right[i] : 0;
				var comparison = leftValue.CompareTo(rightValue);
				if (comparison != 0)
					return comparison;
			}

			return 0;
		}

		static int ComparePrerelease(string[] left, string[] right)
		{
			var count = Math.Max(left.Length, right.Length);
			for (var i = 0; i < count; i++)
			{
				if (i >= left.Length)
					return -1;
				if (i >= right.Length)
					return 1;

				var leftIsNumber = int.TryParse(left[i], out var leftNumber);
				var rightIsNumber = int.TryParse(right[i], out var rightNumber);

				int comparison;
				if (leftIsNumber && rightIsNumber)
					comparison = leftNumber.CompareTo(rightNumber);
				else if (leftIsNumber)
					comparison = -1;
				else if (rightIsNumber)
					comparison = 1;
				else
					comparison = string.Compare(left[i], right[i], StringComparison.OrdinalIgnoreCase);

				if (comparison != 0)
					return comparison;
			}

			return 0;
		}

		readonly record struct ParsedVersion(int[] Core, string[] Prerelease)
		{
			public static ParsedVersion Parse(string version)
			{
				var withoutMetadata = version.Split('+', 2)[0];
				var parts = withoutMetadata.Split('-', 2);
				var core = parts[0]
					.Split('.', StringSplitOptions.RemoveEmptyEntries)
					.Select(part => int.TryParse(part, out var value) ? value : 0)
					.ToArray();
				var prerelease = parts.Length > 1
					? parts[1].Split('.', StringSplitOptions.RemoveEmptyEntries)
					: Array.Empty<string>();

				return new ParsedVersion(core, prerelease);
			}
		}
	}
}
