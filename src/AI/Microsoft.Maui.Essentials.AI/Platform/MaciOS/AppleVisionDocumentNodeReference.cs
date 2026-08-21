using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Provides access to the Apple Vision object underlying a normalized document node.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleVisionDocumentNodeReference
{
	private readonly VisionDocumentObservationNative[] _observations;
	private readonly VisionDocumentNodeNative? _node;

	internal AppleVisionDocumentNodeReference(
		VisionDocumentObservationNative observation,
		VisionDocumentNodeNative node)
	{
		_observations = [observation];
		_node = node;
		Path = node.Path;
	}

	internal AppleVisionDocumentNodeReference(
		VisionDocumentObservationNative[] observations)
	{
		_observations = observations;
	}

	/// <summary>Gets the provider path of this node, or <see langword="null"/> for a page reference.</summary>
	public string? Path { get; }

	/// <summary>Gets the raw JSON for this node or page.</summary>
	/// <remarks>
	/// Node references return one JSON object. Page references return a JSON array containing every
	/// <c>DocumentObservation</c> produced for the image, including an empty array when no observations were found.
	/// </remarks>
	public ReadOnlyMemory<byte> GetRawJson()
	{
		if (_node is not null)
		{
			return _node.JsonData?.ToArray()
				?? throw new InvalidOperationException(
					$"Apple Vision could not encode node '{_node.Path}' as JSON.");
		}

		using var stream = new MemoryStream();
		stream.WriteByte((byte)'[');
		for (var index = 0; index < _observations.Length; index++)
		{
			if (index > 0)
			{
				stream.WriteByte((byte)',');
			}

			var json = _observations[index].JsonData?.ToArray()
				?? throw new InvalidOperationException("Apple Vision could not encode the observation as JSON.");
			stream.Write(json);
		}
		stream.WriteByte((byte)']');
		return stream.ToArray();
	}

	/// <summary>Gets the raw JSON as UTF-8 text.</summary>
	public string GetRawJsonText() => Encoding.UTF8.GetString(GetRawJson().Span);

	/// <summary>Gets geometry for a UTF-16 text range when this node represents text.</summary>
	public DocumentBoundingRegion? GetBoundingRegion(
		int pageNumber,
		int utf16Location,
		int utf16Length)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
		ArgumentOutOfRangeException.ThrowIfNegative(utf16Location);
		ArgumentOutOfRangeException.ThrowIfNegative(utf16Length);

		return _node?.GetBoundingRegion(utf16Location, utf16Length) is { Length: > 1 } polygon
			? AppleVisionDocumentMapper.ToBoundingRegion(pageNumber, polygon)
			: null;
	}
}
