using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace ApexPack.Buffers;

/// <summary>
/// 文字列の符号化。
/// ヘッダーは <c>CompactUInt(((length &lt;&lt; 1) | wide) + 1)</c>、0 は null。
/// 全文字が U+00FF 以下なら 1 バイト/文字(Latin1)、それ以外は UTF-16LE。
/// </summary>
public static class ApexStringCodec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<TWriter>(ref TWriter writer, string? value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
    {
        if (value is null)
        {
            writer.GetSpanRef(1) = 0;
            return;
        }
        WriteChars(ref writer, value.AsSpan());
    }

    public static void WriteChars<TWriter>(ref TWriter writer, ReadOnlySpan<char> chars)
        where TWriter : struct, IApexBufferWriter, allows ref struct
    {
        int length = chars.Length;
        if (length == 0)
        {
            writer.GetSpanRef(1) = 1;
            return;
        }

        bool le = TWriter.IsHostLittleEndian;
        uint narrowHeader = ((uint)length << 1) + 1;
        int narrowHeaderSize = ApexWire.CompactSize(narrowHeader);
        int wideHeaderSize = ApexWire.CompactSize(narrowHeader + 1);
        ref byte dest = ref writer.Reserve(wideHeaderSize + length * 2);

        if (narrowHeaderSize == wideHeaderSize)
        {
            // 縮小しながら非 Latin1 を検出する 1 パス。失敗したら UTF-16 で書き直す。
            if (TryNarrow(chars, ref Unsafe.Add(ref dest, narrowHeaderSize)))
            {
                ApexWire.WriteCompact(ref dest, narrowHeader, le);
                writer.Advance(narrowHeaderSize + length);
                return;
            }
        }
        else if (!chars.ContainsAnyExceptInRange('\0', 'ÿ'))
        {
            // ヘッダー長が縮小/非縮小で変わる境界の長さだけ事前判定する(長さ 32767 などまれな場合)
            TryNarrow(chars, ref Unsafe.Add(ref dest, narrowHeaderSize));
            ApexWire.WriteCompact(ref dest, narrowHeader, le);
            writer.Advance(narrowHeaderSize + length);
            return;
        }

        ApexWire.WriteCompact(ref dest, narrowHeader + 1, le);
        ref byte body = ref Unsafe.Add(ref dest, wideHeaderSize);
        var target = MemoryMarshal.CreateSpan(ref Unsafe.As<byte, char>(ref body), length);
        if (le)
        {
            chars.CopyTo(target);
        }
        else
        {
            ApexEndianHelper.ReverseEndiannessChars(chars, target);
        }
        writer.Advance(wideHeaderSize + length * 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string? Read<TReader>(ref TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
    {
        uint header = reader.ReadCompactUInt();
        if (header == 0) return null;
        if (header == 1) return string.Empty;
        return ReadBody(ref reader, header - 1);
    }

    private static string ReadBody<TReader>(ref TReader reader, uint payload)
        where TReader : struct, IApexBufferReader, allows ref struct
    {
        uint length = payload >> 1;
        if ((payload & 1) == 0)
        {
            if (length > (uint)reader.Remaining) ApexThrowHelper.ThrowInvalidLength((int)Math.Min(length, int.MaxValue));
            ref readonly byte src = ref reader.GetSpanRef((int)length);
            return Encoding.Latin1.GetString(MemoryMarshal.CreateReadOnlySpan(in src, (int)length));
        }

        if (length > (uint)(reader.Remaining >> 1)) ApexThrowHelper.ThrowInvalidLength((int)Math.Min(length, int.MaxValue));
        ref readonly byte wide = ref reader.GetSpanRef((int)length * 2);
        var chars = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<byte, char>(ref Unsafe.AsRef(in wide)), (int)length);
        if (TReader.IsHostLittleEndian)
        {
            return new string(chars);
        }
        return FillReversed(new string('\0', (int)length), chars);
    }

    private static string FillReversed(string target, ReadOnlySpan<char> source)
    {
        // 生成直後で未公開の文字列なので書き換えてよい
        var dest = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(target.AsSpan()), target.Length);
        ApexEndianHelper.ReverseEndiannessChars(source, dest);
        return target;
    }

    /// <summary>
    /// UTF-16 を 1 バイト/文字へ縮小する。U+00FF を超える文字があれば false(出力は途中まで書かれている)。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryNarrow(ReadOnlySpan<char> source, ref byte dest)
    {
        int length = source.Length;
        ref ushort src = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(source));
        int i = 0;

        if (Vector256.IsHardwareAccelerated && length >= 32)
        {
            var highMask = Vector256.Create((ushort)0xFF00);
            for (; i <= length - 32; i += 32)
            {
                var a = Vector256.LoadUnsafe(ref src, (nuint)i);
                var b = Vector256.LoadUnsafe(ref src, (nuint)(i + 16));
                if (((a | b) & highMask) != Vector256<ushort>.Zero) return false;
                Vector256.Narrow(a, b).StoreUnsafe(ref dest, (nuint)i);
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            var highMask = Vector128.Create((ushort)0xFF00);
            for (; i <= length - 16; i += 16)
            {
                var a = Vector128.LoadUnsafe(ref src, (nuint)i);
                var b = Vector128.LoadUnsafe(ref src, (nuint)(i + 8));
                if (((a | b) & highMask) != Vector128<ushort>.Zero) return false;
                Vector128.Narrow(a, b).StoreUnsafe(ref dest, (nuint)i);
            }
        }

        for (; i < length; i++)
        {
            ushort c = Unsafe.Add(ref src, i);
            if (c > 0xFF) return false;
            Unsafe.Add(ref dest, i) = (byte)c;
        }
        return true;
    }
}
