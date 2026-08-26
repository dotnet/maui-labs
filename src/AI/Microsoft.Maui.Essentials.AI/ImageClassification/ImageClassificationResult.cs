// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.AI;

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

		Predictions = Array.AsReadOnly(
			snapshot
				.OrderByDescending(static prediction => prediction.Confidence)
				.ToArray());
	}

	/// <summary>Gets the predictions, ordered from highest to lowest confidence.</summary>
	public IReadOnlyList<ImageClassificationPrediction> Predictions { get; }

	/// <summary>Gets or initializes the model identifier that produced this result, when known.</summary>
	public string? ModelId { get; init; }
}
