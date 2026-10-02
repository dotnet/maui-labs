// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Represents a normalized image classification result.</summary>
public sealed class ImageClassificationResult
{
	/// <summary>Initializes a new instance of the <see cref="ImageClassificationResult"/> class.</summary>
	/// <param name="predictions">The predictions produced by the classification client.</param>
	/// <exception cref="ArgumentNullException"><paramref name="predictions"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="predictions"/> contains a <see langword="null"/> item.</exception>
	public ImageClassificationResult(IEnumerable<ImageClassificationPrediction> predictions)
	{
		ArgumentNullException.ThrowIfNull(predictions);

		ImageClassificationPrediction[] snapshot = predictions.ToArray();
		if (snapshot.Any(static prediction => prediction is null))
		{
			throw new ArgumentException("The predictions collection must not contain null items.", nameof(predictions));
		}

		Predictions = Array.AsReadOnly(snapshot);
	}

	/// <summary>Gets the predictions in the order supplied by the implementation.</summary>
	public IReadOnlyList<ImageClassificationPrediction> Predictions { get; }

	/// <summary>Gets or initializes the model identifier that produced this result, when known.</summary>
	public string? ModelId { get; init; }

	/// <summary>Gets or sets the raw representation of the result from the underlying implementation.</summary>
	/// <remarks>
	/// This property can preserve the original provider response for debugging or provider-specific access
	/// without expanding the provider-neutral result contract.
	/// </remarks>
	[JsonIgnore]
	public object? RawRepresentation { get; set; }

	/// <summary>Gets or sets any additional properties associated with the result.</summary>
	public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}
