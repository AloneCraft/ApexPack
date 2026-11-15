using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ApexPack.Buffers;

namespace ApexPack;

/// <summary>
/// 既知のスカラー型(プリミティブ、enum、日時、Guid、decimal、Half、Int128、数値ベクトルなど)。固定長・LE 正規形。
/// </summary>
public readonly struct ScalarFormatter<T> : IApexFormatter<T> where T : unmanaged
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => ApexWire.Write(ref writer.GetSpanRef(ApexWire.SizeOf<T>()), value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        => ApexWire.Read<T>(in reader.GetSpanRef(ApexWire.SizeOf<T>()), TReader.IsHostLittleEndian);

    public static int FixedSize
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ApexWire.SizeOf<T>();
    }

    public static bool IsRawLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ApexWire.IsRawLayout<T>();
    }

    public static int MinWireSize
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ApexWire.SizeOf<T>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteFixed(ref byte destination, scoped in T value, bool littleEndian)
        => ApexWire.Write(ref destination, value, littleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadFixed(ref readonly byte source, bool littleEndian)
        => ApexWire.Read<T>(in source, littleEndian);
}

/// <summary>文字列(null はヘッダー 0 で表すので追加コストなし)。</summary>
public readonly struct StringFormatter : IApexFormatter<string?>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in string? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => ApexStringCodec.Write(ref writer, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        => ApexStringCodec.Read(ref reader);
}

/// <summary>[ApexPackable] 型(null を取り得る位置)。</summary>
public readonly struct PackableFormatter<T> : IApexFormatter<T> where T : ISerializePackable<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => T.SerializeWithTracker(ref writer, in value, ref tracker);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        T.DeserializeGuarded(ref reader, out T value, ref tracker, ref guard);
        return value;
    }

    public static int FixedSize => T.ApexFixedSize;
    public static bool IsRawLayout => T.ApexIsRawLayout;
    /// <summary>
    /// 構造体は本体の最小サイズ。クラスは null(1 バイト)や参照追跡の後方参照があり得るので 1 バイト。
    /// </summary>
    public static int MinWireSize => typeof(T).IsValueType ? T.ApexMinBodySize : 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteFixed(ref byte destination, scoped in T value, bool littleEndian) => T.WriteFixed(ref destination, in value, littleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadFixed(ref readonly byte source, bool littleEndian) => T.ReadFixed(in source, littleEndian);
}

/// <summary>[ApexPackable] 型(null を取らない位置。クラスでも null 表現を書かない)。</summary>
public readonly struct PackableNonNullFormatter<T> : IApexFormatter<T> where T : ISerializePackable<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => T.SerializeNonNull(ref writer, in value, ref tracker);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        T.DeserializeNonNull(ref reader, out T value, ref tracker, ref guard);
        return value;
    }

    public static int FixedSize => T.ApexFixedSize;
    public static bool IsRawLayout => T.ApexIsRawLayout;
    /// <summary>
    /// 構造体は本体の最小サイズ。クラスは参照追跡の後方参照(1 バイトから)があり得るので最大 1 バイト(本体が空の型は 0)。
    /// </summary>
    public static int MinWireSize => typeof(T).IsValueType ? T.ApexMinBodySize : Math.Min(1, T.ApexMinBodySize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteFixed(ref byte destination, scoped in T value, bool littleEndian) => T.WriteFixed(ref destination, in value, littleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadFixed(ref readonly byte source, bool littleEndian) => T.ReadFixed(in source, littleEndian);
}

/// <summary>Nullable&lt;T&gt;(コレクション要素などの単独位置用。1 バイトの有無 + 値)。</summary>
public readonly struct NullableFormatter<T, TFormatter> : IApexFormatter<T?>
    where T : struct
    where TFormatter : IApexFormatter<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in T? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        if (!value.HasValue)
        {
            writer.GetSpanRef(1) = 0;
            return;
        }
        writer.GetSpanRef(1) = 1;
        T inner = value.GetValueOrDefault();
        TFormatter.Write(ref writer, in inner, ref tracker);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        if (reader.GetSpanRef(1) == 0) return null;
        return TFormatter.Read(ref reader, ref tracker, ref guard);
    }
}

/// <summary>KeyValuePair&lt;TKey, TValue&gt;。</summary>
public readonly struct KeyValuePairFormatter<TKey, TValue, TKeyFormatter, TValueFormatter> : IApexFormatter<KeyValuePair<TKey, TValue>>
    where TKeyFormatter : IApexFormatter<TKey>
    where TValueFormatter : IApexFormatter<TValue>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in KeyValuePair<TKey, TValue> value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        TKey key = value.Key;
        TValue item = value.Value;
        TKeyFormatter.Write(ref writer, in key, ref tracker);
        TValueFormatter.Write(ref writer, in item, ref tracker);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static KeyValuePair<TKey, TValue> Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        TKey key = TKeyFormatter.Read(ref reader, ref tracker, ref guard);
        TValue item = TValueFormatter.Read(ref reader, ref tracker, ref guard);
        return new KeyValuePair<TKey, TValue>(key, item);
    }

    public static int FixedSize => TKeyFormatter.FixedSize >= 0 && TValueFormatter.FixedSize >= 0 ? TKeyFormatter.FixedSize + TValueFormatter.FixedSize : -1;
    public static int MinWireSize => TKeyFormatter.MinWireSize + TValueFormatter.MinWireSize;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteFixed(ref byte destination, scoped in KeyValuePair<TKey, TValue> value, bool littleEndian)
    {
        TKeyFormatter.WriteFixed(ref destination, value.Key, littleEndian);
        TValueFormatter.WriteFixed(ref Unsafe.Add(ref destination, TKeyFormatter.FixedSize), value.Value, littleEndian);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static KeyValuePair<TKey, TValue> ReadFixed(ref readonly byte source, bool littleEndian)
        => new(TKeyFormatter.ReadFixed(in source, littleEndian),
               TValueFormatter.ReadFixed(in Unsafe.Add(ref Unsafe.AsRef(in source), TKeyFormatter.FixedSize), littleEndian));
}

/// <summary>Lazy&lt;T&gt;(書き込み時に値を評価する)。</summary>
public readonly struct LazyFormatter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T, TFormatter> : IApexFormatter<Lazy<T>?>
    where TFormatter : IApexFormatter<T>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in Lazy<T>? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        if (value is null)
        {
            writer.GetSpanRef(1) = 0;
            return;
        }
        writer.GetSpanRef(1) = 1;
        T inner = value.Value;
        TFormatter.Write(ref writer, in inner, ref tracker);
    }

    public static Lazy<T>? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        if (reader.GetSpanRef(1) == 0) return null;
        T inner = TFormatter.Read(ref reader, ref tracker, ref guard);
        return new Lazy<T>(inner);
    }
}
