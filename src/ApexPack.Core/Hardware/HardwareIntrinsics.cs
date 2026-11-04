using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace ApexPack.Hardware;

/// <summary>
/// Hardware-accelerated SIMD and bit-manipulation primitives for ApexPack.
/// </summary>
public static class HardwareIntrinsics
{
    public static readonly bool HasVector256 = Vector256.IsHardwareAccelerated;
    public static readonly bool HasVector128 = Vector128.IsHardwareAccelerated;
    public static readonly bool HasBmi2 = Bmi2.X64.IsSupported;

    /// <summary>
    /// Checks if a character span contains only ASCII characters (code points 0x00..0x7F) using SIMD.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool IsAllAscii(ReadOnlySpan<char> chars)
    {
        if (chars.IsEmpty) return true;

        fixed (char* ptr = chars)
        {
            int length = chars.Length;
            int i = 0;

            if (HasVector256 && length >= 16)
            {
                var asciiMask256 = Vector256.Create((ushort)0xFF80);
                while (i <= length - 16)
                {
                    var v = Vector256.Load((ushort*)(ptr + i));
                    if ((v & asciiMask256) != Vector256<ushort>.Zero)
                    {
                        return false;
                    }
                    i += 16;
                }
            }

            if (HasVector128 && i <= length - 8)
            {
                var asciiMask128 = Vector128.Create((ushort)0xFF80);
                while (i <= length - 8)
                {
                    var v = Vector128.Load((ushort*)(ptr + i));
                    if ((v & asciiMask128) != Vector128<ushort>.Zero)
                    {
                        return false;
                    }
                    i += 8;
                }
            }

            while (i < length)
            {
                if (ptr[i] > 0x7F) return false;
                i++;
            }
        }

        return true;
    }

    /// <summary>
    /// Vectorized copy of ASCII UTF-16 chars directly to UTF-8 bytes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void CopyAsciiCharsToUtf8(ReadOnlySpan<char> source, Span<byte> destination)
    {
        fixed (char* srcPtr = source)
        fixed (byte* dstPtr = destination)
        {
            int length = source.Length;
            int i = 0;

            if (Avx2.IsSupported && length >= 16)
            {
                while (i <= length - 16)
                {
                    var v = Vector256.Load((ushort*)(srcPtr + i));
                    var low = v.GetLower();
                    var high = v.GetUpper();
                    var packed = Sse2.PackUnsignedSaturate(low.AsInt16(), high.AsInt16());
                    packed.Store(dstPtr + i);
                    i += 16;
                }
            }

            while (i < length)
            {
                dstPtr[i] = (byte)srcPtr[i];
                i++;
            }
        }
    }

    /// <summary>
    /// Parallel bit extract using BMI2 if available, or scalar bit-scan loop.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ParallelBitExtract(ulong val, ulong mask)
    {
        if (Bmi2.X64.IsSupported)
        {
            return Bmi2.X64.ParallelBitExtract(val, mask);
        }

        ulong res = 0;
        int outBit = 0;
        while (mask != 0)
        {
            int bit = BitOperations.TrailingZeroCount(mask);
            if (((val >> bit) & 1) != 0)
            {
                res |= (1UL << outBit);
            }
            outBit++;
            mask &= mask - 1;
        }
        return res;
    }

    /// <summary>
    /// Parallel bit deposit using BMI2 if available, or scalar bit-deposit loop.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ParallelBitDeposit(ulong val, ulong mask)
    {
        if (Bmi2.X64.IsSupported)
        {
            return Bmi2.X64.ParallelBitDeposit(val, mask);
        }

        ulong res = 0;
        int inBit = 0;
        while (mask != 0)
        {
            int bit = BitOperations.TrailingZeroCount(mask);
            if (((val >> inBit) & 1) != 0)
            {
                res |= (1UL << bit);
            }
            inBit++;
            mask &= mask - 1;
        }
        return res;
    }
}
