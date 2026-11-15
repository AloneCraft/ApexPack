using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace ApexPack;

/// <summary>
/// Nullable&lt;T&gt;(値が 4 / 8 バイト)の列を AVX2 の並べ替え(vpermd)で詰める・展開する。
/// ワイヤ形式はスカラー経路と同じ「存在ビットマップ + 存在する値だけ」で、LE 機専用。
/// </summary>
/// <remarks>
/// スカラーで詰めると「次の書き込み位置が前の要素の有無に依存する」直列の連鎖になり 1 要素 1 ns 前後かかるため、
/// 1 ベクトル(32 バイト)ぶんの要素をまとめて並べ替えて連鎖を要素数分の 1 にする。
/// </remarks>
internal static class NullableVector<T> where T : struct
{
    /// <summary>この経路を使えるときの値のサイズ(4 または 8)。使えなければ 0。</summary>
    public static readonly int ValueSize = Detect();

    private static readonly int Stride = Unsafe.SizeOf<T?>();
    private static readonly int ElementsPerVector = ValueSize == 0 ? 0 : 32 / Stride;

    /// <summary>HasValue を置く dword 位置のビット(vmovmskps の結果に対するマスク)。</summary>
    private static readonly uint HasLaneBits = ValueSize == 4 ? 0x55u : 0x11u;

    // 書き込み側は vmovmskps の 8 ビット値をそのまま添字にする(ビットの詰め替えを省く)
    private static readonly Vector256<int>[] Compress = ValueSize == 0 ? Array.Empty<Vector256<int>>() : BuildCompress();
    private static readonly byte[] CompactBits = ValueSize == 0 ? Array.Empty<byte>() : BuildCompactBits();

    // 読み取り側は存在マスク(ElementsPerVector ビット)で引く
    private static readonly Vector256<int>[] Expand = ValueSize == 0 ? Array.Empty<Vector256<int>>() : BuildExpand();
    private static readonly Vector256<int>[] ExpandMask = ValueSize == 0 ? Array.Empty<Vector256<int>>() : BuildExpandMask();
    private static readonly Vector256<int>[] HasValueLanes = ValueSize == 0 ? Array.Empty<Vector256<int>>() : BuildHasValueLanes();

    /// <summary>
    /// Nullable&lt;T&gt; の配置(先頭 1 バイトが HasValue、値が既定の位置)を実測で確かめる。想定と違えば 0 を返しスカラー経路を使わせる。
    /// </summary>
    private static int Detect()
    {
        if (!Avx2.IsSupported || !BitConverter.IsLittleEndian) return 0;
        int size = Unsafe.SizeOf<T>();
        if (size != 4 && size != 8) return 0;
        if (Unsafe.SizeOf<T?>() != size * 2) return 0;

        T? probe = default(T);
        ref byte start = ref Unsafe.As<T?, byte>(ref probe);
        if (start != 1) return 0;
        ref readonly T value = ref Nullable.GetValueRefOrDefaultRef(in probe);
        nint offset = Unsafe.ByteOffset(ref start, ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in value)));
        return offset == size ? size : 0;
    }

    private static int ValueLane(int element) => (element * Stride + ValueSize) / 4;
    private static int HasLane(int element) => element * Stride / 4;
    private static int DwordsPerValue => ValueSize / 4;

    /// <summary>vmovmskps の 8 ビット値 → 要素ごとの存在ビット(ElementsPerVector ビット)。</summary>
    private static int ToElementMask(int laneBits)
    {
        int mask = 0;
        for (int e = 0; e < ElementsPerVector; e++)
        {
            if ((laneBits & (1 << HasLane(e))) != 0) mask |= 1 << e;
        }
        return mask;
    }

    private static byte[] BuildCompactBits()
    {
        var table = new byte[256];
        for (int laneBits = 0; laneBits < 256; laneBits++) table[laneBits] = (byte)ToElementMask(laneBits);
        return table;
    }

    private static Vector256<int>[] BuildCompress()
    {
        var table = new Vector256<int>[256];
        for (int laneBits = 0; laneBits < 256; laneBits++)
        {
            int mask = ToElementMask(laneBits);
            var idx = new int[8];
            int k = 0;
            for (int e = 0; e < ElementsPerVector; e++)
            {
                if ((mask & (1 << e)) == 0) continue;
                for (int d = 0; d < DwordsPerValue; d++) idx[k * DwordsPerValue + d] = ValueLane(e) + d;
                k++;
            }
            table[laneBits] = Vector256.Create(idx);
        }
        return table;
    }

    private static Vector256<int>[] BuildExpand()
    {
        var table = new Vector256<int>[1 << ElementsPerVector];
        for (int mask = 0; mask < table.Length; mask++)
        {
            var idx = new int[8];
            int k = 0;
            for (int e = 0; e < ElementsPerVector; e++)
            {
                if ((mask & (1 << e)) == 0) continue;
                for (int d = 0; d < DwordsPerValue; d++) idx[ValueLane(e) + d] = k * DwordsPerValue + d;
                k++;
            }
            table[mask] = Vector256.Create(idx);
        }
        return table;
    }

    private static Vector256<int>[] BuildExpandMask()
    {
        var table = new Vector256<int>[1 << ElementsPerVector];
        for (int mask = 0; mask < table.Length; mask++)
        {
            var lanes = new int[8];
            for (int e = 0; e < ElementsPerVector; e++)
            {
                if ((mask & (1 << e)) == 0) continue;
                for (int d = 0; d < DwordsPerValue; d++) lanes[ValueLane(e) + d] = -1;
            }
            table[mask] = Vector256.Create(lanes);
        }
        return table;
    }

    private static Vector256<int>[] BuildHasValueLanes()
    {
        var table = new Vector256<int>[1 << ElementsPerVector];
        for (int mask = 0; mask < table.Length; mask++)
        {
            var lanes = new int[8];
            for (int e = 0; e < ElementsPerVector; e++)
            {
                if ((mask & (1 << e)) != 0) lanes[HasLane(e)] = 1;
            }
            table[mask] = Vector256.Create(lanes);
        }
        return table;
    }

    /// <summary>
    /// 1 ベクトル分の HasValue を、dword ごとのビット(vmovmskps の形)で返す。HasValue の位置以外のビットは 0。
    /// HasValue は各要素の先頭バイトで、その dword の下位 8 ビットだけを見る(詰め物のバイトは見ない)。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PresenceLanes(Vector256<int> v)
        => ~Vector256.Equals(v & Vector256.Create(0xFF), Vector256<int>.Zero).ExtractMostSignificantBits() & HasLaneBits;

    /// <summary>
    /// 先頭 <paramref name="count"/> 要素を書き、値の領域に書いたバイト数を返す。<paramref name="values"/> の後ろに 16 バイトの余裕が必要。
    /// 端数(8 要素未満)は扱わず、処理した要素数を <paramref name="processed"/> に返す。
    /// </summary>
    public static int Write(ref T? source, int count, ref byte bitmap, ref byte values, out int processed)
    {
        ref byte src = ref Unsafe.As<T?, byte>(ref source);
        int vectorsPerByte = 8 / ElementsPerVector;
        int offset = 0;
        int i = 0;
        for (; i + 8 <= count; i += 8)
        {
            uint presenceByte = 0;
            for (int v = 0; v < vectorsPerByte; v++)
            {
                var vector = Vector256.LoadUnsafe(ref src, (nuint)((i + v * ElementsPerVector) * Stride)).AsInt32();
                uint lanes = PresenceLanes(vector);
                presenceByte |= (uint)Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(CompactBits), lanes) << (v * ElementsPerVector);
                var packed = Avx2.PermuteVar8x32(vector, Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Compress), lanes));
                packed.GetLower().AsByte().StoreUnsafe(ref Unsafe.Add(ref values, offset));
                offset += BitOperations.PopCount(lanes) * ValueSize;
            }
            Unsafe.Add(ref bitmap, i >> 3) = (byte)presenceByte;
        }
        processed = i;
        return offset;
    }

    /// <summary>
    /// 先頭から 8 要素単位で展開する。1 グループ分の値 + 16 バイトが残っていない所で止め(範囲外を読まない)、処理した要素数と消費したバイト数を返す。
    /// </summary>
    public static int Read(ref T? destination, int count, ref byte bitmap, ref byte values, int valuesLength, out int processed)
    {
        ref byte dst = ref Unsafe.As<T?, byte>(ref destination);
        int vectorsPerByte = 8 / ElementsPerVector;
        int offset = 0;
        int i = 0;
        for (; i + 8 <= count && offset + 8 * ValueSize + 16 <= valuesLength; i += 8)
        {
            uint presenceByte = Unsafe.Add(ref bitmap, i >> 3);
            for (int v = 0; v < vectorsPerByte; v++)
            {
                uint mask = (presenceByte >> (v * ElementsPerVector)) & ((1u << ElementsPerVector) - 1);
                var packed = Vector128.LoadUnsafe(ref Unsafe.Add(ref values, offset)).AsInt32().ToVector256Unsafe();
                var expanded = (Avx2.PermuteVar8x32(packed, Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Expand), mask))
                                & Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(ExpandMask), mask))
                               | Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(HasValueLanes), mask);
                expanded.AsByte().StoreUnsafe(ref dst, (nuint)((i + v * ElementsPerVector) * Stride));
                offset += BitOperations.PopCount(mask) * ValueSize;
            }
        }
        processed = i;
        return offset;
    }
}
