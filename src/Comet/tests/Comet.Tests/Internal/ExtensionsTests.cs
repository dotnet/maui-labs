#nullable enable
using System;
using Comet.Internal;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Internal
{
	public class ExtensionsTests
	{
		[Fact]
		public void GetValueOfType_ConvertibleScalar_PreservesConversion()
			=> Assert.Equal(42, ((object)"42").GetValueOfType<int>());

		[Fact]
		public void GetValueOfType_UnrelatedTarget_ReturnsDefaultWithoutInvokingConversion()
		{
			var value = new TrackingConvertible();

			Assert.Null(value.GetValueOfType<Paint>());
			Assert.Equal(0, value.ToTypeCalls);
		}

		sealed class TrackingConvertible : IConvertible
		{
			public int ToTypeCalls { get; private set; }

			public TypeCode GetTypeCode() => TypeCode.Object;
			public object ToType(Type conversionType, IFormatProvider? provider)
			{
				ToTypeCalls++;
				throw new InvalidCastException();
			}

			public bool ToBoolean(IFormatProvider? provider) => throw new NotSupportedException();
			public byte ToByte(IFormatProvider? provider) => throw new NotSupportedException();
			public char ToChar(IFormatProvider? provider) => throw new NotSupportedException();
			public DateTime ToDateTime(IFormatProvider? provider) => throw new NotSupportedException();
			public decimal ToDecimal(IFormatProvider? provider) => throw new NotSupportedException();
			public double ToDouble(IFormatProvider? provider) => throw new NotSupportedException();
			public short ToInt16(IFormatProvider? provider) => throw new NotSupportedException();
			public int ToInt32(IFormatProvider? provider) => throw new NotSupportedException();
			public long ToInt64(IFormatProvider? provider) => throw new NotSupportedException();
			public sbyte ToSByte(IFormatProvider? provider) => throw new NotSupportedException();
			public float ToSingle(IFormatProvider? provider) => throw new NotSupportedException();
			public string ToString(IFormatProvider? provider) => throw new NotSupportedException();
			public ushort ToUInt16(IFormatProvider? provider) => throw new NotSupportedException();
			public uint ToUInt32(IFormatProvider? provider) => throw new NotSupportedException();
			public ulong ToUInt64(IFormatProvider? provider) => throw new NotSupportedException();
		}
	}
}
