using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.InteropServices;
using System.Text;

namespace ApexPack.Buffers;

/// <summary>
/// ワイヤ形式の基本符号化。すべてリトルエンディアン正規形。
/// <c>le</c> 引数には呼び出し側の <c>TWriter.IsHostLittleEndian</c> などの JIT 時定数を渡す。
/// </summary>
public static class ApexWire
{
    /// <summary>CompactUInt の 1 バイト表現の上限。</summary>
    public const uint CompactOneByteMax = 0xFC;
    private const byte CompactUInt16Tag = 0xFD;
    private const byte CompactUInt32Tag = 0xFE;

    /// <summary>CompactUInt の最大長。</summary>
    public const int CompactMaxSize = 5;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CompactSize(uint value) => value <= CompactOneByteMax ? 1 : value <= ushort.MaxValue ? 3 : 5;

    /// <summary>CompactUInt を書き、使ったバイト数を返す。<paramref name="p"/> から 5 バイトが書き込み可能であること。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteCompact(ref byte p, uint value, bool le)
    {
        if (value <= CompactOneByteMax)
        {
            p = (byte)value;
            return 1;
        }
        return WriteCompactLong(ref p, value, le);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int WriteCompactLong(ref byte p, uint value, bool le)
    {
        if (value <= ushort.MaxValue)
        {
            p = CompactUInt16Tag;
            WriteUInt16(ref Unsafe.Add(ref p, 1), (ushort)value, le);
            return 3;
        }
        p = CompactUInt32Tag;
        WriteUInt32(ref Unsafe.Add(ref p, 1), value, le);
        return 5;
    }

    // ---------------- primitives ----------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16(ref byte p, ushort v, bool le) => Unsafe.WriteUnaligned(ref p, le ? v : BinaryPrimitives.ReverseEndianness(v));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32(ref byte p, uint v, bool le) => Unsafe.WriteUnaligned(ref p, le ? v : BinaryPrimitives.ReverseEndianness(v));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt64(ref byte p, ulong v, bool le) => Unsafe.WriteUnaligned(ref p, le ? v : BinaryPrimitives.ReverseEndianness(v));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadUInt16(ref readonly byte p, bool le)
    {
        ushort v = Unsafe.ReadUnaligned<ushort>(in p);
        return le ? v : BinaryPrimitives.ReverseEndianness(v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUInt32(ref readonly byte p, bool le)
    {
        uint v = Unsafe.ReadUnaligned<uint>(in p);
        return le ? v : BinaryPrimitives.ReverseEndianness(v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReadUInt64(ref readonly byte p, bool le)
    {
        ulong v = Unsafe.ReadUnaligned<ulong>(in p);
        return le ? v : BinaryPrimitives.ReverseEndianness(v);
    }

    // ---------------- scalars (generic) ----------------

    /// <summary>
    /// スカラー型のワイヤ上のサイズ。nint/nuint は 8 バイト、DateTimeOffset は 10 バイト(時計 ticks + オフセット分)に正規化する。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SizeOf<T>() where T : unmanaged
    {
        if (typeof(T) == typeof(DateTimeOffset)) return 10;
        if (typeof(T) == typeof(nint) || typeof(T) == typeof(nuint)) return 8;
        return Unsafe.SizeOf<T>();
    }

    /// <summary>LE 機のメモリ表現がそのままワイヤ形式になる型なら true(配列を一括コピーできる)。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsRawLayout<T>() where T : unmanaged
    {
        if (typeof(T) == typeof(DateTimeOffset)) return false;
        if (typeof(T) == typeof(nint) || typeof(T) == typeof(nuint)) return IntPtr.Size == 8;
        return true;
    }

    /// <summary>スカラー値を書く。<paramref name="p"/> から <see cref="SizeOf{T}"/> バイトが書き込み可能であること。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<T>(ref byte p, T value, bool le) where T : unmanaged
    {
        if (typeof(T) == typeof(bool))
        {
            p = Unsafe.As<T, bool>(ref value) ? (byte)1 : (byte)0;
            return;
        }
        if (le && IsRawLayout<T>())
        {
            Unsafe.WriteUnaligned(ref p, value);
            return;
        }
        WriteSlow(ref p, value, le);
    }

    /// <summary>スカラー値を読む。<paramref name="p"/> から <see cref="SizeOf{T}"/> バイトが読み取り可能であること。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Read<T>(ref readonly byte p, bool le) where T : unmanaged
    {
        if (typeof(T) == typeof(bool))
        {
            bool b = p != 0;
            return Unsafe.As<bool, T>(ref b);
        }
        if (le && IsRawLayout<T>())
        {
            return Unsafe.ReadUnaligned<T>(in p);
        }
        return ReadSlow<T>(in p, le);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteSlow<T>(ref byte p, T value, bool le) where T : unmanaged
    {
        if (typeof(T) == typeof(DateTimeOffset))
        {
            var dto = Unsafe.As<T, DateTimeOffset>(ref value);
            WriteUInt64(ref p, (ulong)dto.Ticks, le);
            WriteUInt16(ref Unsafe.Add(ref p, 8), (ushort)(short)dto.TotalOffsetMinutes, le);
            return;
        }
        if (typeof(T) == typeof(nint))
        {
            WriteUInt64(ref p, (ulong)(long)Unsafe.As<T, nint>(ref value), le);
            return;
        }
        if (typeof(T) == typeof(nuint))
        {
            WriteUInt64(ref p, (ulong)Unsafe.As<T, nuint>(ref value), le);
            return;
        }

        // 反転経路: 値をネイティブ表現で置いてから構成要素ごとにバイト順を反転する
        Unsafe.WriteUnaligned(ref p, value);
        if (!le)
        {
            ReverseComponents<T>(MemoryMarshal.CreateSpan(ref p, Unsafe.SizeOf<T>()));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T ReadSlow<T>(ref readonly byte p, bool le) where T : unmanaged
    {
        if (typeof(T) == typeof(DateTimeOffset))
        {
            long ticks = (long)ReadUInt64(in p, le);
            short offset = (short)ReadUInt16(in Unsafe.Add(ref Unsafe.AsRef(in p), 8), le);
            var dto = new DateTimeOffset(ticks, TimeSpan.FromMinutes(offset));
            return Unsafe.As<DateTimeOffset, T>(ref dto);
        }
        if (typeof(T) == typeof(nint))
        {
            nint v = (nint)(long)ReadUInt64(in p, le);
            return Unsafe.As<nint, T>(ref v);
        }
        if (typeof(T) == typeof(nuint))
        {
            nuint v = (nuint)ReadUInt64(in p, le);
            return Unsafe.As<nuint, T>(ref v);
        }

        T value = default;
        Span<byte> tmp = MemoryMarshal.AsBytes(new Span<T>(ref value));
        MemoryMarshal.CreateReadOnlySpan(in p, tmp.Length).CopyTo(tmp);
        if (!le)
        {
            ReverseComponents<T>(tmp);
        }
        return value;
    }

    /// <summary>
    /// 型の構成要素ごとにバイト順を反転する。LE 機の正規形と BE 機のメモリ表現を相互変換する。
    /// 未知の unmanaged 構造体は構成が分からないため反転しない(エンディアン非可搬)。
    /// </summary>
    private static void ReverseComponents<T>(Span<byte> bytes) where T : unmanaged
    {
        if (typeof(T) == typeof(Guid))
        {
            // Guid のメモリ表現は int, short, short, byte[8]
            bytes.Slice(0, 4).Reverse();
            bytes.Slice(4, 2).Reverse();
            bytes.Slice(6, 2).Reverse();
            return;
        }
        if (typeof(T) == typeof(decimal))
        {
            // decimal のメモリ表現は flags(int), hi32(uint), lo64(ulong)
            bytes.Slice(0, 4).Reverse();
            bytes.Slice(4, 4).Reverse();
            bytes.Slice(8, 8).Reverse();
            return;
        }

        int component = ComponentSize<T>();
        if (component <= 1) return;
        for (int i = 0; i + component <= bytes.Length; i += component)
        {
            bytes.Slice(i, component).Reverse();
        }
    }

    private static int ComponentSize<T>() where T : unmanaged
    {
        if (typeof(T) == typeof(Complex)) return 8;
        if (typeof(T) == typeof(Vector2) || typeof(T) == typeof(Vector3) || typeof(T) == typeof(Vector4)
            || typeof(T) == typeof(Quaternion) || typeof(T) == typeof(Plane)
            || typeof(T) == typeof(Matrix3x2) || typeof(T) == typeof(Matrix4x4))
        {
            return 4;
        }
        if (typeof(T).IsPrimitive || typeof(T).IsEnum
            || typeof(T) == typeof(DateTime) || typeof(T) == typeof(TimeSpan)
            || typeof(T) == typeof(DateOnly) || typeof(T) == typeof(TimeOnly)
            || typeof(T) == typeof(Half) || typeof(T) == typeof(Int128) || typeof(T) == typeof(UInt128)
            || typeof(T) == typeof(Rune))
        {
            return Unsafe.SizeOf<T>();
        }
        return 1;
    }

    /// <summary>メモリ表現の先頭 <paramref name="size"/> バイトだけを写して値を作る(残りの詰め物は 0)。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadPrefix<T>(ref readonly byte source, int size)
    {
        T value = default!;
        CopySmall(ref Unsafe.As<T, byte>(ref value), ref Unsafe.AsRef(in source), size);
        return value;
    }

    /// <summary>
    /// 短い固定長の写し。<paramref name="size"/> が JIT 時の定数なら、分岐が消えて 8 / 4 / 2 / 1 バイトの読み書きだけになる
    /// (長さが変数の CopyBlock は汎用の写し処理の呼び出しになり、数 ns かかる)。32 バイトを超えたら CopyBlock を使う。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CopySmall(ref byte destination, ref byte source, int size)
    {
        if (size > 32)
        {
            Unsafe.CopyBlockUnaligned(ref destination, ref source, (uint)size);
            return;
        }
        if (size >= 8)
        {
            Unsafe.WriteUnaligned(ref destination, Unsafe.ReadUnaligned<ulong>(ref source));
            if (size >= 16)
            {
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 8), Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref source, 8)));
                if (size >= 24)
                {
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 16), Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref source, 16)));
                    if (size >= 32)
                    {
                        Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 24), Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref source, 24)));
                    }
                }
            }
        }
        int offset = size & ~7;
        if ((size & 4) != 0)
        {
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, offset), Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref source, offset)));
            offset += 4;
        }
        if ((size & 2) != 0)
        {
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, offset), Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref source, offset)));
            offset += 2;
        }
        if ((size & 1) != 0)
        {
            Unsafe.Add(ref destination, offset) = Unsafe.Add(ref source, offset);
        }
    }

    /// <summary>
    /// 要素の間隔 <paramref name="stride"/> のメモリから、各要素の先頭 <paramref name="size"/> バイトを詰めて並べる(後ろの詰め物を落とす)。
    /// 16 / 32 バイト単位で読み書きし、書きすぎた分は次の要素で上書きされる。最後の要素だけ正確な長さで写す。
    /// </summary>
    public static void PackStrided(ref byte destination, ref byte source, int count, int stride, int size)
    {
        if (count <= 0) return;
        int i = 0;
        if (Vector128.IsHardwareAccelerated && size >= 8 && size <= 16 && stride >= 16)
        {
            for (; i < count - 1; i++)
            {
                Vector128.LoadUnsafe(ref source, (nuint)(i * stride)).StoreUnsafe(ref destination, (nuint)(i * size));
            }
        }
        else if (Vector256.IsHardwareAccelerated && size > 16 && size <= 32 && stride >= 32)
        {
            for (; i < count - 1; i++)
            {
                Vector256.LoadUnsafe(ref source, (nuint)(i * stride)).StoreUnsafe(ref destination, (nuint)(i * size));
            }
        }
        for (; i < count; i++)
        {
            Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref destination, i * size), ref Unsafe.Add(ref source, i * stride), (uint)size);
        }
    }

    /// <summary><see cref="PackStrided"/> の逆。各要素の詰め物の位置には何が入ってもよい。</summary>
    public static void UnpackStrided(ref byte destination, ref byte source, int count, int stride, int size)
    {
        if (count <= 0) return;
        int i = 0;
        if (Vector128.IsHardwareAccelerated && size >= 8 && size <= 16 && stride >= 16)
        {
            for (; i < count - 1; i++)
            {
                Vector128.LoadUnsafe(ref source, (nuint)(i * size)).StoreUnsafe(ref destination, (nuint)(i * stride));
            }
        }
        else if (Vector256.IsHardwareAccelerated && size > 16 && size <= 32 && stride >= 32)
        {
            for (; i < count - 1; i++)
            {
                Vector256.LoadUnsafe(ref source, (nuint)(i * size)).StoreUnsafe(ref destination, (nuint)(i * stride));
            }
        }
        for (; i < count; i++)
        {
            Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref destination, i * stride), ref Unsafe.Add(ref source, i * size), (uint)size);
        }
    }
}
