#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Maui.Graphics;

namespace Comet.Backend
{
	/// <summary>
	/// Small per-node cache for intrinsic native measurements. Layouts commonly ask the same
	/// leaf for its size several times while resolving Auto/Grid tracks and arranging the result.
	/// Retained nodes keep those answers across route activation until a mutation invalidates them.
	/// </summary>
	internal sealed class BackendMeasureCache
	{
		const int Capacity = 4;
		readonly Entry[] _entries = new Entry[Capacity];
		int _count;
		int _next;
		Dictionary<PropertyId, PropertyValue>? _measurementProperties;

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Opacity || id == PropertyIds.BackgroundColor ||
				id == PropertyIds.TranslationX || id == PropertyIds.TranslationY ||
				id == PropertyIds.AutomationId || id == PropertyIds.ClipShape ||
				id == PropertyIds.CornerRadius || id == PropertyIds.HasTapGesture ||
				id == PropertyIds.HasLongPressGesture || id == PropertyIds.HasRecordGesture ||
				id == PropertyIds.Text_Color || id == PropertyIds.Button_TextColor)
				return;

			// Declarative updates replay unchanged properties too. Only compare known
			// immutable measurement inputs; mutable payloads and unknown ids stay conservative.
			if (id == PropertyIds.Padding ||
				id == PropertyIds.Text_Value || id == PropertyIds.Text_FontSize ||
				id == PropertyIds.Text_FontFamily || id == PropertyIds.Text_FontWeight ||
				id == PropertyIds.Text_MaxLines || id == PropertyIds.Text_LineHeight ||
				id == PropertyIds.Text_LineBreak || id == PropertyIds.Text_LineBreakMode ||
				id == PropertyIds.Text_Italic || id == PropertyIds.Text_CharacterSpacing ||
				id == PropertyIds.Text_HorizontalAlignment || id == PropertyIds.Text_VerticalAlignment)
			{
				_measurementProperties ??= new();
				if (_measurementProperties.TryGetValue(id, out var previous) && previous.Equals(value))
					return;
				_measurementProperties[id] = value;
			}
			Clear();
		}

		public Size GetOrMeasure(
			double widthConstraint,
			double heightConstraint,
			Func<double, double, Size> measure)
		{
			for (var index = 0; index < _count; index++)
			{
				ref readonly var entry = ref _entries[index];
				if (entry.Width.Equals(widthConstraint) &&
					entry.Height.Equals(heightConstraint))
					return entry.Size;
			}

			var size = measure(widthConstraint, heightConstraint);
			_entries[_next] = new Entry(widthConstraint, heightConstraint, size);
			if (_count < Capacity)
				_count++;
			_next = (_next + 1) % Capacity;
			return size;
		}

		public void Clear()
		{
			_count = 0;
			_next = 0;
		}

		readonly record struct Entry(double Width, double Height, Size Size);
	}
}
