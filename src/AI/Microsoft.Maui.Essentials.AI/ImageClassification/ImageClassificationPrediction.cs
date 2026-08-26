// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.AI;

/// <summary>Represents one normalized image classification prediction.</summary>
public sealed class ImageClassificationPrediction
{
	/// <summary>Initializes a new instance of the <see cref="ImageClassificationPrediction"/> class.</summary>
	/// <param name="label">The provider-neutral display label for the predicted class.</param>
	/// <param name="confidence">The normalized prediction confidence, from 0 through 1.</param>
	/// <exception cref="ArgumentException"><paramref name="label"/> is empty or whitespace.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="confidence"/> is not finite or is outside the inclusive range from 0 through 1.
	/// </exception>
	public ImageClassificationPrediction(string label, float confidence)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(label);

		if (!float.IsFinite(confidence) || confidence < 0 || confidence > 1)
		{
			throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be a finite value from 0 through 1.");
		}

		Label = label;
		Confidence = confidence;
	}

	/// <summary>Gets the predicted class label.</summary>
	public string Label { get; }

	/// <summary>Gets the normalized prediction confidence, from 0 through 1.</summary>
	public float Confidence { get; }
}
