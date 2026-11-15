using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using ApexPack.Buffers;

namespace ApexPack;

/// <summary>
/// 型引数で合成するフォーマッタ(構造体)の契約。<c>ListFormatter&lt;T, ScalarFormatter&lt;int&gt;&gt;</c> のように組み合わせ、
/// 仮想呼び出しなしで任意のネストを表現する。インスタンスを持たないので型を握らない。
/// </summary>
public interface IApexFormatter<T>
{
    static abstract void Write<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct;

    static abstract T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct;

    /// <summary>常に同じバイト数なら値、そうでなければ -1。</summary>
    static virtual int FixedSize => -1;

    /// <summary>LE 機のメモリ表現がワイヤ形式と一致するなら true。</summary>
    static virtual bool IsRawLayout => false;

    /// <summary>1 要素の最小バイト数(件数ヘッダー検査用)。</summary>
    static virtual int MinWireSize => 1;

    static virtual void WriteFixed(ref byte destination, scoped in T value, bool littleEndian)
        => throw new NotSupportedException();

    static virtual T ReadFixed(ref readonly byte source, bool littleEndian)
        => throw new NotSupportedException();
}

/// <summary>
/// 実行時に解決されるフォーマッタ(ルート API と、ジェネリック型引数のメンバー用)。
/// 利用者が独自フォーマッタを作るときはこれを継承して <see cref="ApexFormatterCache{T}.Register"/> に渡す。
/// </summary>
public abstract class ApexFormatter<T>
{
    public abstract void Write<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct;

    public abstract T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct;

    /// <summary>ルートで自動的に参照追跡を有効にするなら true。</summary>
    public virtual bool PreserveReferences => false;

    /// <summary>常に同じバイト数で表される型ならそのサイズ、そうでなければ -1(ルートの高速経路用)。</summary>
    public virtual int RootFixedSize => -1;

    /// <summary>LE 機でメモリ表現がそのままワイヤ形式になる固定長型なら true。</summary>
    public virtual bool RootIsRawLayout => false;

    /// <summary>固定長型を読む(<see cref="RootFixedSize"/> が 0 以上のときだけ呼ばれる)。</summary>
    public virtual T ReadFixedRoot(ref readonly byte source, bool littleEndian) => throw new NotSupportedException();

    // ---- ルート API(非ジェネリックな仮想メソッド。static readonly 経由で呼ばれ、JIT が脱仮想化できる)----

    public virtual byte[] SerializeToArray(scoped in T value)
    {
        var buffer = ApexThreadBuffer.Rent();
        var writer = new ApexBufferWriter(buffer);
        try
        {
            WriteRoot(ref writer, in value);
            return writer.ToArray();
        }
        finally
        {
            writer.Dispose();
            ApexThreadBuffer.Return(buffer);
        }
    }

    public virtual int SerializeToSpan(Span<byte> destination, scoped in T value)
    {
        var writer = new ApexSpanWriter(destination);
        WriteRoot(ref writer, in value);
        return writer.BytesWritten;
    }

    public virtual void SerializeToBufferWriter(IBufferWriter<byte> destination, scoped in T value)
    {
        var writer = new ApexBufferWriterAdapter<IBufferWriter<byte>>(ref destination);
        WriteRoot(ref writer, in value);
        writer.Flush();
    }

    public virtual T Deserialize(ReadOnlySpan<byte> source, out int consumed)
    {
        var reader = new ApexSpanReader(source);
        var guard = new TrustedSecurityGuard();
        T value = ReadRoot(ref reader, ref guard);
        consumed = reader.Consumed;
        return value;
    }

    public virtual T DeserializeSafe(ReadOnlySpan<byte> source, in ApexSecurityOptions options, Func<string, Type?>? typeResolver)
    {
        var reader = new ApexSpanReader(source);
        var guard = new UntrustedSecurityGuard(options, typeResolver);
        return ReadRoot(ref reader, ref guard);
    }

    public virtual T DeserializeResolving(ReadOnlySpan<byte> source, Func<string, Type?> typeResolver)
    {
        var reader = new ApexSpanReader(source);
        var guard = new TrustedResolvingGuard(typeResolver);
        return ReadRoot(ref reader, ref guard);
    }

    public virtual byte[] SerializeWithReferences(scoped in T value)
    {
        var buffer = ApexThreadBuffer.Rent();
        var writer = new ApexBufferWriter(buffer);
        var tracker = new ActiveReferenceTracker();
        try
        {
            Write(ref writer, in value, ref tracker);
            return writer.ToArray();
        }
        finally
        {
            tracker.Dispose();
            writer.Dispose();
            ApexThreadBuffer.Return(buffer);
        }
    }

    public virtual T DeserializeWithReferences(ReadOnlySpan<byte> source)
    {
        var reader = new ApexSpanReader(source);
        var tracker = new ActiveReferenceTracker();
        var guard = new TrustedSecurityGuard();
        try
        {
            return Read(ref reader, ref tracker, ref guard);
        }
        finally
        {
            tracker.Dispose();
        }
    }

    /// <summary>ルートとして書く(参照追跡が必要な型なら追跡を有効にする)。任意の writer で使える。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRoot<TWriter>(ref TWriter writer, scoped in T value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
    {
        if (PreserveReferences)
        {
            var active = new ActiveReferenceTracker();
            try
            {
                Write(ref writer, in value, ref active);
            }
            finally
            {
                active.Dispose();
            }
            return;
        }
        var tracker = new NullReferenceTracker();
        Write(ref writer, in value, ref tracker);
    }

    /// <summary>ルートとして読む(参照追跡が必要な型なら追跡を有効にする)。任意の reader で使える。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T ReadRoot<TReader, TGuard>(ref TReader reader, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        if (PreserveReferences)
        {
            var active = new ActiveReferenceTracker();
            try
            {
                return Read(ref reader, ref active, ref guard);
            }
            finally
            {
                active.Dispose();
            }
        }
        var tracker = new NullReferenceTracker();
        return Read(ref reader, ref tracker, ref guard);
    }
}

/// <summary>
/// 合成フォーマッタを <see cref="ApexFormatter{T}"/> として包む。ルート API を具象型で上書きし、汎用仮想呼び出しを避ける。
/// </summary>
public sealed class FormatterAdapter<T, TFormatter> : ApexFormatter<T>
    where TFormatter : IApexFormatter<T>
{
    private readonly bool _preserveReferences;

    public FormatterAdapter() : this(false) { }

    public FormatterAdapter(bool preserveReferences) => _preserveReferences = preserveReferences;

    public override bool PreserveReferences => _preserveReferences;

    public override int RootFixedSize => _preserveReferences ? -1 : TFormatter.FixedSize;

    public override bool RootIsRawLayout
        => RootFixedSize >= 0 && TFormatter.IsRawLayout && BitConverter.IsLittleEndian && RootFixedSize == Unsafe.SizeOf<T>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override T ReadFixedRoot(ref readonly byte source, bool littleEndian) => TFormatter.ReadFixed(in source, littleEndian);

    public override void Write<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        => TFormatter.Write(ref writer, in value, ref tracker);

    public override T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        => TFormatter.Read(ref reader, ref tracker, ref guard);

    public override byte[] SerializeToArray(scoped in T value)
    {
        if (_preserveReferences) return SerializeWithReferences(in value);

        int fixedSize = TFormatter.FixedSize;
        if (fixedSize >= 0)
        {
            var result = GC.AllocateUninitializedArray<byte>(fixedSize);
            if (fixedSize > 0)
            {
                TFormatter.WriteFixed(ref result[0], in value, BitConverter.IsLittleEndian);
            }
            return result;
        }

        var buffer = ApexThreadBuffer.Rent();
        var writer = new ApexBufferWriter(buffer);
        try
        {
            var tracker = new NullReferenceTracker();
            TFormatter.Write(ref writer, in value, ref tracker);
            return writer.ToArray();
        }
        finally
        {
            writer.Dispose();
            ApexThreadBuffer.Return(buffer);
        }
    }

    public override T Deserialize(ReadOnlySpan<byte> source, out int consumed)
    {
        if (_preserveReferences)
        {
            var r = new ApexSpanReader(source);
            var active = new ActiveReferenceTracker();
            var g = new TrustedSecurityGuard();
            try
            {
                T v = TFormatter.Read(ref r, ref active, ref g);
                consumed = r.Consumed;
                return v;
            }
            finally
            {
                active.Dispose();
            }
        }

        int fixedSize = TFormatter.FixedSize;
        if (fixedSize >= 0)
        {
            if (source.Length < fixedSize) ApexSpanReader.ThrowUnexpectedEndOfBuffer();
            consumed = fixedSize;
            return TFormatter.ReadFixed(in MemoryMarshal.GetReference(source), BitConverter.IsLittleEndian);
        }

        var reader = new ApexSpanReader(source);
        var tracker = new NullReferenceTracker();
        var guard = new TrustedSecurityGuard();
        T value = TFormatter.Read(ref reader, ref tracker, ref guard);
        consumed = reader.Consumed;
        return value;
    }
}

/// <summary>
/// 型ごとの実行時フォーマッタ。保持先はこのジェネリック型の static だけで、<see cref="Type"/> をキーにした表は作らない。
/// collectible な型 T で閉じたこのクラスの static は T と同じローダーに属し、T と一緒に解放される。
/// </summary>
public static class ApexFormatterCache<T>
{
    private static ApexFormatter<T>? s_registered;
    private static int s_created;

    /// <summary>
    /// フォーマッタを登録する。最初の使用より前(生成コードのモジュール初期化子など)に呼ぶこと。既に使用済みなら false。
    /// </summary>
    public static bool Register(ApexFormatter<T> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (Volatile.Read(ref s_created) != 0) return false;
        s_registered = formatter;
        return true;
    }

    /// <summary>この型のフォーマッタ。未登録なら既知の型から組み立てる。</summary>
    public static ApexFormatter<T> Instance
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Holder.Value;
    }

    private static class Holder
    {
        public static readonly ApexFormatter<T> Value = Create();

        private static ApexFormatter<T> Create()
        {
            Volatile.Write(ref s_created, 1);
            return s_registered ?? ApexFormatterRegistry.TryCreate<T>() ?? ApexFormatterResolver.Create<T>();
        }
    }
}

/// <summary>
/// ルート API 用の型ごとの定数。static readonly なので Tier1 の JIT では定数として畳み込まれ、
/// 固定長型は仮想呼び出しなしで読める。保持先は型ごとのジェネリック static だけ(型と一緒に解放される)。
/// </summary>
internal static class ApexRootInfo<T>
{
    public static readonly int FixedSize = ApexFormatterCache<T>.Instance.RootFixedSize;
    public static readonly bool IsRawLayout = ApexFormatterCache<T>.Instance.RootIsRawLayout;
}

/// <summary>
/// <see cref="ApexFormatterCache{T}"/> へ委譲する合成フォーマッタ(型引数メンバーや独自登録型用)。
/// </summary>
public readonly struct CachedFormatter<T> : IApexFormatter<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => ApexFormatterCache<T>.Instance.Write(ref writer, in value, ref tracker);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        => ApexFormatterCache<T>.Instance.Read(ref reader, ref tracker, ref guard);

    public static int MinWireSize => 0;
}

/// <summary>
/// スレッドごとに 1 つの作業バッファを使い回す。使用中は取り出して null にするので再入しても共有しない。
/// 中身はバイトだけで、利用者のオブジェクトや型は保持しない。
/// </summary>
internal static class ApexThreadBuffer
{
    private const int Size = 65536;

    [ThreadStatic]
    private static byte[]? t_buffer;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Rent()
    {
        var buffer = t_buffer;
        if (buffer is null) return new byte[Size];
        t_buffer = null;
        return buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(byte[] buffer) => t_buffer = buffer;
}
