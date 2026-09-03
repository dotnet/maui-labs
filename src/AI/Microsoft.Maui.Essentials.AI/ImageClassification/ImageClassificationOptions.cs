// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Represents options that constrain an image classification response.</summary>
public class ImageClassificationOptions
{
	private int? _maximumPredictions;
	private float? _minimumConfidence;

	/// <summary>Initializes a new instance of the <see cref="ImageClassificationOptions"/> class.</summary>
	public ImageClassificationOptions()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ImageClassificationOptions"/> class by copying
	/// <paramref name="other"/>.
	/// </summary>
	/// <param name="other">The options to copy, or <see langword="null"/> to use defaults.</param>
	protected ImageClassificationOptions(ImageClassificationOptions? other)
	{
		if (other is null)
		{
			return;
		}

		MaximumPredictions = other.MaximumPredictions;
		MinimumConfidence = other.MinimumConfidence;
	}

	/// <summary>Gets or sets the maximum number of predictions that may be returned.</summary>
	/// <value>
	/// <see langword="null"/> to use the implementation default; otherwise, a value greater than zero.
	/// Implementations may return fewer predictions.
	/// </value>
	public int? MaximumPredictions
	{
		get => _maximumPredictions;
		set
		{
			if (value is <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum number of predictions must be greater than zero.");
			}

			_maximumPredictions = value;
		}
	}

	/// <summary>Gets or sets the minimum confidence required for a prediction.</summary>
	/// <value><see langword="null"/> to use the implementation default; otherwise, a finite value from 0 through 1.</value>
	/// <remarks>
	/// Implementations that cannot provide confidence values must throw <see cref="NotSupportedException"/> when this
	/// property is not <see langword="null"/>.
	/// </remarks>
	public float? MinimumConfidence
	{
		get => _minimumConfidence;
		set
		{
			if (value is float confidence &&
				(!float.IsFinite(confidence) || confidence < 0 || confidence > 1))
			{
				throw new ArgumentOutOfRangeException(nameof(value), value, "The minimum confidence must be a finite value from 0 through 1.");
			}

			_minimumConfidence = value;
		}
	}

	/// <summary>Produces a copy of the current options.</summary>
	/// <returns>A new options instance with the same values.</returns>
	public virtual ImageClassificationOptions Clone() => new(this);
}
