#nullable enable
using System;
using Microsoft.Maui.Graphics;

namespace Comet.Backend
{
	/// <summary>Discriminates the payload carried by a <see cref="PropertyValue"/>.</summary>
	public enum PropertyValueKind : byte
	{
		None = 0,
		Bool,
		Int,
		Long,
		Single,
		Double,
		Color,
		String,
		Object,
	}

	/// <summary>
	/// A small tagged union used to carry a single property value across the
	/// <see cref="ICometBackendNode.ApplyProperty"/> boundary without boxing primitives.
	/// </summary>
	/// <remarks>
	/// Numeric and boolean values live in an inline <c>long</c> (reinterpreted as
	/// needed); reference values (string, <see cref="Microsoft.Maui.Graphics.Color"/>,
	/// arbitrary objects) live in a single object slot. Primitives therefore cost
	/// zero heap allocations.
	/// <para>
	/// This is a plain <c>readonly struct</c> rather than a <c>ref struct</c> on
	/// purpose: the diff layer and test recorders need to <em>store</em> values
	/// (e.g. a recorded patch stream), which a <c>ref struct</c> forbids. It is
	/// passed by <c>in</c> on the hot path to avoid copies.
	/// </para>
	/// </remarks>
	public readonly struct PropertyValue : IEquatable<PropertyValue>
	{
		readonly long _bits;
		readonly object? _obj;

		public PropertyValueKind Kind { get; }

		PropertyValue(PropertyValueKind kind, long bits, object? obj)
		{
			Kind = kind;
			_bits = bits;
			_obj = obj;
		}

		public static PropertyValue None => default;

		public static PropertyValue From(bool value) => new(PropertyValueKind.Bool, value ? 1 : 0, null);
		public static PropertyValue From(int value) => new(PropertyValueKind.Int, value, null);
		public static PropertyValue From(long value) => new(PropertyValueKind.Long, value, null);
		public static PropertyValue From(float value) => new(PropertyValueKind.Single, BitConverter.DoubleToInt64Bits(value), null);
		public static PropertyValue From(double value) => new(PropertyValueKind.Double, BitConverter.DoubleToInt64Bits(value), null);
		public static PropertyValue From(Color? value) => new(PropertyValueKind.Color, 0, value);
		public static PropertyValue From(string? value) => new(PropertyValueKind.String, 0, value);
		public static PropertyValue FromObject(object? value) => new(PropertyValueKind.Object, 0, value);

		public bool AsBool => _bits != 0;
		public int AsInt => (int)_bits;
		public long AsLong => _bits;
		public float AsSingle => (float)BitConverter.Int64BitsToDouble(_bits);
		public double AsDouble => BitConverter.Int64BitsToDouble(_bits);
		public Color? AsColor => _obj as Color;
		public string? AsString => _obj as string;
		public object? AsObject => _obj;

		public bool Equals(PropertyValue other)
		{
			if (Kind != other.Kind)
				return false;
			return Kind switch
			{
				PropertyValueKind.None => true,
				PropertyValueKind.Color or PropertyValueKind.String or PropertyValueKind.Object
					=> Equals(_obj, other._obj),
				_ => _bits == other._bits,
			};
		}

		public override bool Equals(object? obj) => obj is PropertyValue pv && Equals(pv);

		public override int GetHashCode()
		{
			return Kind switch
			{
				PropertyValueKind.None => 0,
				PropertyValueKind.Color or PropertyValueKind.String or PropertyValueKind.Object
					=> HashCode.Combine(Kind, _obj),
				_ => HashCode.Combine(Kind, _bits),
			};
		}

		public override string ToString()
		{
			return Kind switch
			{
				PropertyValueKind.None => "None",
				PropertyValueKind.Bool => $"Bool({AsBool})",
				PropertyValueKind.Int => $"Int({AsInt})",
				PropertyValueKind.Long => $"Long({AsLong})",
				PropertyValueKind.Single => $"Single({AsSingle})",
				PropertyValueKind.Double => $"Double({AsDouble})",
				PropertyValueKind.Color => $"Color({_obj})",
				PropertyValueKind.String => $"String({_obj})",
				_ => $"Object({_obj})",
			};
		}
	}
}
