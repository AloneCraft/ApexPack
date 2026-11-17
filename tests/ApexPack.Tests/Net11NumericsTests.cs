#if NET11_0_OR_GREATER
using System.Numerics;
using ApexPack.Buffers;
using Xunit;

namespace ApexPack.Tests;

public class Net11NumericsTests
{
    [Fact]
    public void Scalars_PreserveBitPatternsInLittleEndian()
    {
        AssertScalar(BitConverter.UInt16BitsToBFloat16(0x8000), "0080", 2);
        AssertScalar(BitConverter.UInt16BitsToBFloat16(0x7FC3), "C37F", 2);
        AssertScalar(Decimal32.One, "01008032", 4);
        AssertScalar(Decimal64.One, "010000000000C031", 8);
        AssertScalar(Decimal128.One, "01000000000000000000000000004030", 16);
        AssertScalar(Decimal128.PositiveInfinity, "00000000000000000000000000000078", 16);
    }

    [Fact]
    public void GenericComplex_WritesRealThenImaginary()
    {
        AssertScalar(new Complex<Half>((Half)1, (Half)(-2)), "003C00C0", 2);
        AssertScalar(new Complex<float>(1, -2), "0000803F000000C0", 4);
        AssertScalar(new Complex<double>(1, -2), "000000000000F03F00000000000000C0", 8);
        AssertScalar(new Complex<BFloat16>(BFloat16.One, BitConverter.UInt16BitsToBFloat16(0x8000)), "803F0080", 2);
        AssertScalar(new Complex<Decimal32>(Decimal32.One, Decimal32.PositiveInfinity), "0100803200000078", 4);
        AssertScalar(new Complex<Decimal64>(Decimal64.One, Decimal64.PositiveInfinity), "010000000000C0310000000000000078", 8);
        AssertScalar(new Complex<Decimal128>(Decimal128.One, Decimal128.PositiveInfinity),
            "0100000000000000000000000000403000000000000000000000000000000078", 16);
    }

    [Fact]
    public void GeneratedMembers_KeepThePreviousFieldOrder()
    {
        var model = new Net11NumericRecord
        {
            Name = "z",
            Sample = BFloat16.One,
            Sequence = 0x01020304,
            Price = Decimal64.One,
            Impedance = new Complex<float>(1, -2),
        };

        // v2 writes the mask and the fixed int before the remaining members in declared order.
        var previousBytes = Convert.FromHexString("0304030201037A803F010000000000C0310000803F000000C0");
        Assert.Equal(previousBytes, ApexPackSerializer.Serialize(model));
        var copy = ApexPackSerializer.Deserialize<Net11NumericRecord>(previousBytes);
        Assert.Equal(model.Name, copy.Name);
        Assert.Equal(model.Sequence, copy.Sequence);
        Assert.Equal(model.Sample, copy.Sample);
        Assert.Equal(model.Price, copy.Price);
        Assert.Equal(model.Impedance, copy.Impedance);
    }

    [Fact]
    public void ComplexArrays_RoundTripAndRejectTruncation()
    {
        Complex<BFloat16>[] items = [new(BFloat16.One, BFloat16.Zero), new(BFloat16.Zero, BFloat16.One)];
        var bytes = ApexPackSerializer.Serialize(items);
        Assert.Equal(Convert.FromHexString("03803F00000000803F"), bytes);
        Assert.Equal(items, ApexPackSerializer.Deserialize<Complex<BFloat16>[]>(bytes));
        Assert.Equal(bytes, ApexPackSerializer.SerializeUnmanagedArray(items));
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.Deserialize<Complex<BFloat16>[]>(bytes.AsSpan(0, bytes.Length - 1)));
    }

    private static void AssertScalar<T>(T value, string hex, int componentSize) where T : unmanaged
    {
        var expected = Convert.FromHexString(hex);
        Assert.Equal(expected, ApexPackSerializer.Serialize(value));
        Assert.Equal(expected, ApexPackSerializer.Serialize(ApexPackSerializer.Deserialize<T>(expected)));

        var reversed = (byte[])expected.Clone();
        for (int offset = 0; offset < reversed.Length; offset += componentSize)
            Array.Reverse(reversed, offset, componentSize);

        var actual = new byte[expected.Length];
        ApexWire.Write(ref actual[0], value, le: false);
        Assert.Equal(reversed, actual);
        Assert.Equal(expected, ApexPackSerializer.Serialize(ApexWire.Read<T>(in reversed[0], le: false)));
    }
}

[ApexPackable]
public partial class Net11NumericRecord
{
    public string Name { get; set; } = "";
    public BFloat16 Sample { get; set; }
    public int Sequence { get; set; }
    public Decimal64? Price { get; set; }
    public Complex<float> Impedance { get; set; }
}
#endif
