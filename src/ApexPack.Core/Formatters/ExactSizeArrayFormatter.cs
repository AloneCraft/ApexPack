using System;
using System.Runtime.CompilerServices;
using ApexPack.Buffers;

namespace ApexPack;

/// <summary>サイズを取得できるクラス要素を持つ、参照追跡なしのルート配列。</summary>
internal sealed class ExactSizeArrayFormatter<T> : ApexFormatter<T[]?> where T : class, ISerializePackable<T>
{
    private static readonly bool s_canWriteDirect = T.ApexFixedSize < 0 && !T.ApexPreserveReferences;
    private static readonly FormatterAdapter<T[]?, ArrayFormatter<T, PackableFormatter<T>>> s_general = new();

    public override int RootRawArrayElementSize => s_general.RootRawArrayElementSize;

    public override void Write<TWriter, TTracker>(ref TWriter writer, scoped in T[]? value, ref TTracker tracker)
        => ArrayFormatter<T, PackableFormatter<T>>.Write(ref writer, in value, ref tracker);

    public override T[]? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        => ArrayFormatter<T, PackableFormatter<T>>.Read(ref reader, ref tracker, ref guard);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override T[]? ReadFrom(ref ApexSpanReader reader)
    {
        var tracker = new NullReferenceTracker();
        var guard = new TrustedSecurityGuard();
        return ArrayFormatter<T, PackableFormatter<T>>.Read(ref reader, ref tracker, ref guard);
    }

    public override T[]? Deserialize(ReadOnlySpan<byte> source, out int consumed)
    {
        var reader = new ApexSpanReader(source);
        var value = ReadFrom(ref reader);
        consumed = reader.Consumed;
        return value;
    }

    public override byte[] SerializeToArray(scoped in T[]? value)
    {
        if (!s_canWriteDirect || value is not { Length: 1 } items)
            return s_general.SerializeToArray(in value);

        // 配列の要素は一度だけ取り出し、サイズの算出と書き込みに同じ値を使う。
        var item = items[0];
        int size = T.ApexExactSize(in item);
        if ((uint)size >= Array.MaxLength)
            return s_general.SerializeToArray(in value);

        var result = new byte[1 + size];
        var writer = new ApexSpanWriter(result);
        var tracker = new NullReferenceTracker();
        writer.WriteCollectionHeader(1);
        T.SerializeWithTracker(ref writer, in item, ref tracker);
        return result;
    }
}
