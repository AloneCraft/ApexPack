using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ApexPack.Buffers;

/// <summary>
/// バイト順反転の一括処理(ベクトル化は <see cref="BinaryPrimitives"/> に任せる)。BE 機と反転経路の検証でだけ使われる。
/// </summary>
public static class ApexEndianHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReverseEndiannessChars(ReadOnlySpan<char> source, Span<char> destination)
    {
        BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<char, ushort>(source), MemoryMarshal.Cast<char, ushort>(destination));
    }
}
