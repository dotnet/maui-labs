#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Maui;

namespace Comet
{
	/// <summary>Maps an application font alias to the platform face name that should be
	/// sent to a node backend.</summary>
	public readonly struct FontFamilyRegistration
	{
		public FontFamilyRegistration(string faceName, FontWeight? preferredWeight)
		{
			FaceName = faceName;
			PreferredWeight = preferredWeight;
		}

		public string FaceName { get; }
		public FontWeight? PreferredWeight { get; }
	}

	/// <summary>
	/// Cross-platform font alias registry. Applications register the public alias used by
	/// their source UI together with the platform face name loaded by that application.
	/// Unregistered family names pass through unchanged.
	/// </summary>
	public static class FontFamilyRegistry
	{
		static readonly object Gate = new();
		static readonly Dictionary<string, FontFamilyRegistration> Registrations =
			new(StringComparer.OrdinalIgnoreCase);

		public static void Register(
			string alias,
			string faceName,
			FontWeight? preferredWeight = null)
		{
			if (string.IsNullOrWhiteSpace(alias))
				throw new ArgumentException("A font alias is required.", nameof(alias));
			if (string.IsNullOrWhiteSpace(faceName))
				throw new ArgumentException("A font face name is required.", nameof(faceName));

			lock (Gate)
				Registrations[alias] = new FontFamilyRegistration(faceName, preferredWeight);
		}

		public static FontFamilyRegistration Resolve(string? family)
		{
			if (string.IsNullOrWhiteSpace(family))
				return new FontFamilyRegistration(string.Empty, null);

			lock (Gate)
				return Registrations.TryGetValue(family, out var registration)
					? registration
					: new FontFamilyRegistration(family, null);
		}
	}
}
