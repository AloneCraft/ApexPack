using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ApexPack.Buffers;

namespace ApexPack;

/// <summary>
/// ApexPack の入口。
/// </summary>
/// <remarks>
/// [ApexPackable] 型は制約付きオーバーロード(仮想呼び出しなし)、それ以外の型(List&lt;int&gt;、string など)は
/// <see cref="ApexFormatterCache{T}"/> 経由で扱う。どちらも型をキーにした static な表を持たない。
/// </remarks>
public static class ApexPackSerializer
{
    // ================= serialize =================

    /// <summary>[ApexPackable] 型を byte 配列にする。固定長型は正確なサイズの配列へ直接書く。</summary>
    [OverloadResolutionPriority(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(scoped in T value) where T : ISerializePackable<T>
    {
        if (T.ApexPreserveReferences)
        {
            return SerializeWithReferencesCore(in value);
        }

        int fixedSize = T.ApexFixedSize;
        if (fixedSize >= 0)
        {
            var result = GC.AllocateUninitializedArray<byte>(fixedSize);
            if (fixedSize > 0)
            {
                T.WriteFixed(ref MemoryMarshal.GetArrayDataReference(result), in value, BitConverter.IsLittleEndian);
            }
            return result;
        }

        // 書くバイト数が値によらず決まる型は、正確な大きさの配列へ直接書く
        int exact = T.ApexExactSize(in value);
        if (exact >= 0)
        {
            var result = GC.AllocateUninitializedArray<byte>(exact);
            var direct = new ApexSpanWriter(result);
            var tracker = new NullReferenceTracker();
            T.SerializeWithTracker(ref direct, in value, ref tracker);
            return result;
        }

        return SerializeVariable(in value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] SerializeVariable<T>(scoped in T value) where T : ISerializePackable<T>
    {
        var buffer = ApexThreadBuffer.Rent();
        var writer = new ApexBufferWriter(buffer);
        try
        {
            var tracker = new NullReferenceTracker();
            T.SerializeWithTracker(ref writer, in value, ref tracker);
            return writer.ToArray();
        }
        finally
        {
            writer.Dispose();
            ApexThreadBuffer.Return(buffer);
        }
    }

    /// <summary>任意の型(コレクション、文字列、BCL 型など)を byte 配列にする。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(T value) => SerializeByRef(in value);

    /// <summary>任意の型を参照で受け取り、<see cref="Serialize{T}(T)"/> と同じ形式の byte 配列にする。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] SerializeByRef<T>(scoped in T value)
    {
        // 固定長型は仮想呼び出しを通さずに書く(条件は型ごとの static readonly で、Tier1 では定数になる)。
        // 参照型は固定長にならないので調べない(参照型の共有コードでは static の参照ごとに実行時の検索が要るため)
        int fixedSize = typeof(T).IsValueType ? ApexRootInfo<T>.FixedSize : -1;
        if (fixedSize >= 0)
        {
            var result = GC.AllocateUninitializedArray<byte>(fixedSize);
            if (fixedSize > 0)
            {
                ref byte destination = ref MemoryMarshal.GetArrayDataReference(result);
                if (ApexRootInfo<T>.IsRawLayout || ApexRootInfo<T>.RawPrefixSize == fixedSize)
                    ApexWire.CopySmall(ref destination, ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in value)), fixedSize);
                else ApexFormatterCache<T>.Instance.WriteFixedRoot(ref destination, in value, BitConverter.IsLittleEndian);
            }
            return result;
        }
        if (BitConverter.IsLittleEndian && typeof(T) == typeof(int[]) && Int32ArrayRoot.UsesDirectWriter)
        {
            return SerializeRawArray(Unsafe.As<T, int[]?>(ref Unsafe.AsRef(in value)), sizeof(int));
        }
        return ApexFormatterCache<T>.Instance.SerializeToArray(in value);
    }

    private static class Int32ArrayRoot
    {
        public static readonly bool UsesDirectWriter;

        static Int32ArrayRoot()
        {
            // int[] の経路に入るまで、既定または登録済みフォーマッタを確定しない。
            UsesDirectWriter =
                ApexFormatterCache<int[]?>.Instance is FormatterAdapter<int[]?, ArrayFormatter<int, ScalarFormatter<int>>>
                    { PreserveReferences: false };
        }
    }

    /// <summary>ArrayFormatter と同じ形式(CompactUInt(件数 + 1) + 要素、null は 0)を一度に書く。</summary>
    internal static byte[] SerializeRawArray(Array? array, int elementSize)
    {
        if (array is null) return new byte[] { 0 };
        int length = array.Length;
        uint header = (uint)length + 1;
        int headerSize = ApexWire.CompactSize(header);
        int body = checked(length * elementSize);
        var result = GC.AllocateUninitializedArray<byte>(checked(headerSize + body));
        ref byte destination = ref MemoryMarshal.GetArrayDataReference(result);
        ApexWire.WriteCompact(ref destination, header, le: true);
        if (body > 0)
        {
            Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref destination, headerSize), ref MemoryMarshal.GetArrayDataReference(array), (uint)body);
        }
        return result;
    }

    /// <summary>[ApexPackable] 型を <paramref name="destination"/> へ書き、書いたバイト数を返す(ヒープ割り当てなし)。</summary>
    [OverloadResolutionPriority(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Serialize<T>(Span<byte> destination, scoped in T value) where T : ISerializePackable<T>
    {
        var writer = new ApexSpanWriter(destination);
        if (T.ApexPreserveReferences)
        {
            var active = new ActiveReferenceTracker();
            try
            {
                T.SerializeWithTracker(ref writer, in value, ref active);
            }
            finally
            {
                active.Dispose();
            }
            return writer.BytesWritten;
        }
        var tracker = new NullReferenceTracker();
        T.SerializeWithTracker(ref writer, in value, ref tracker);
        return writer.BytesWritten;
    }

    /// <summary>任意の型を <paramref name="destination"/> へ書き、書いたバイト数を返す。</summary>
    public static int Serialize<T>(Span<byte> destination, T value) => ApexFormatterCache<T>.Instance.SerializeToSpan(destination, in value);

    /// <summary>任意の型を <see cref="IBufferWriter{T}"/> へ直接書く。</summary>
    public static void Serialize<T>(IBufferWriter<byte> destination, T value) => ApexFormatterCache<T>.Instance.SerializeToBufferWriter(destination, in value);

    /// <summary>
    /// 任意の型を <see cref="IBufferWriter{T}"/> へ書く。書き込み先は参照で受け取るので、構造体の書き込み先でも呼び出し元の変数に結果が残る。
    /// </summary>
    public static void Serialize<T, TBufferWriter>(ref TBufferWriter destination, T value)
        where TBufferWriter : IBufferWriter<byte>
    {
        var writer = new ApexBufferWriterAdapter<TBufferWriter>(ref destination);
        ApexFormatterCache<T>.Instance.WriteRoot(ref writer, in value);
        writer.Flush();
    }

    /// <summary>任意の型をストリームへ書く。</summary>
    public static async ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
    {
        byte[] bytes = Serialize(value);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 型を実行時に指定して書く(<typeparamref name="T"/> を <paramref name="type"/> とした <see cref="Serialize{T}(T)"/> と同じバイト列)。
    /// <paramref name="value"/> が null なら型の既定値を書く。型が合わなければ <see cref="InvalidCastException"/>。
    /// </summary>
    public static byte[] Serialize(Type type, object? value) => ApexObjectBridge.For(type).Serialize(value);

    // ================= deserialize =================

    /// <summary>任意の型を復元する(信頼済み入力)。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlySpan<byte> bytes)
    {
        // 固定長型は仮想呼び出しを通さずに読む(条件は Tier1 で定数になる)。参照型は固定長にならないので調べない
        if (typeof(T).IsValueType && ApexRootInfo<T>.FixedSize >= 0)
        {
            if (bytes.Length < ApexRootInfo<T>.FixedSize) ApexSpanReader.ThrowUnexpectedEndOfBuffer();
            if (ApexRootInfo<T>.IsRawLayout) return Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(bytes));
            if (ApexRootInfo<T>.RawPrefixSize == ApexRootInfo<T>.FixedSize) return ApexWire.ReadPrefix<T>(in MemoryMarshal.GetReference(bytes), ApexRootInfo<T>.FixedSize);
            // static readonly の中身は正確な型が分かるので、JIT が脱仮想化してインライン展開できる
            return ApexFormatterCache<T>.Instance.ReadFixedRoot(in MemoryMarshal.GetReference(bytes), BitConverter.IsLittleEndian);
        }
        return ApexFormatterCache<T>.Instance.Deserialize(bytes, out _);
    }

    /// <summary>任意の型を復元し、消費したバイト数を返す。</summary>
    public static T Deserialize<T>(ReadOnlySpan<byte> bytes, out int consumed) => ApexFormatterCache<T>.Instance.Deserialize(bytes, out consumed);

    /// <summary>複数セグメントの入力から復元する。</summary>
    public static T Deserialize<T>(in ReadOnlySequence<byte> bytes)
    {
        if (bytes.IsSingleSegment)
        {
            return Deserialize<T>(bytes.FirstSpan);
        }

        int length = checked((int)bytes.Length);
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            bytes.CopyTo(rented);
            return Deserialize<T>(rented.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>ストリームの残りすべてを読んで復元する。</summary>
    public static async ValueTask<T> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        // MemoryStream は残りのバッファから直接読み、読んだ分だけ位置を進める(同じストリームから続けて次の値を読める)
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment))
        {
            int position = (int)ms.Position;
            T value = Deserialize<T>(segment.AsSpan(position), out int consumed);
            ms.Position = position + consumed;
            return value;
        }
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Deserialize<T>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    /// <summary>型を実行時に指定して復元する(信頼済み入力)。値型は箱詰めして返す。</summary>
    public static object? Deserialize(Type type, ReadOnlySpan<byte> bytes) => ApexObjectBridge.For(type).Deserialize(bytes, out _);

    /// <summary>型を実行時に指定して復元し、消費したバイト数を返す。</summary>
    public static object? Deserialize(Type type, ReadOnlySpan<byte> bytes, out int consumed) => ApexObjectBridge.For(type).Deserialize(bytes, out consumed);

    /// <summary>信頼できない入力を上限付きで復元する(再帰爆弾・増幅攻撃・<see cref="Type"/> 名の無断解決を拒否)。</summary>
    public static T DeserializeSafe<T>(ReadOnlySpan<byte> bytes, in ApexSecurityOptions options)
        => ApexFormatterCache<T>.Instance.DeserializeSafe(bytes, in options, typeResolver: null);

    /// <summary>呼び出し単位の設定で復元する。</summary>
    public static T Deserialize<T>(ReadOnlySpan<byte> bytes, ApexSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Security is { } security)
        {
            return ApexFormatterCache<T>.Instance.DeserializeSafe(bytes, in security, options.TypeResolver);
        }
        if (options.TypeResolver is { } resolver)
        {
            return ApexFormatterCache<T>.Instance.DeserializeResolving(bytes, resolver);
        }
        return Deserialize<T>(bytes);
    }

    // ================= reference tracking =================

    /// <summary>循環参照と共有インスタンスを保ったまま書く。</summary>
    public static byte[] SerializeWithReferences<T>(scoped in T value) where T : ISerializePackable<T>
        => SerializeWithReferencesCore(in value);

    private static byte[] SerializeWithReferencesCore<T>(scoped in T value) where T : ISerializePackable<T>
    {
        var buffer = ApexThreadBuffer.Rent();
        var writer = new ApexBufferWriter(buffer);
        var tracker = new ActiveReferenceTracker();
        try
        {
            T.SerializeWithTracker(ref writer, in value, ref tracker);
            return writer.ToArray();
        }
        finally
        {
            tracker.Dispose();
            writer.Dispose();
            ApexThreadBuffer.Return(buffer);
        }
    }

    /// <summary><see cref="SerializeWithReferences{T}"/> の出力を復元する。</summary>
    public static T DeserializeWithReferences<T>(ReadOnlySpan<byte> bytes) where T : ISerializePackable<T>
    {
        var reader = new ApexSpanReader(bytes);
        var tracker = new ActiveReferenceTracker();
        var guard = new TrustedSecurityGuard();
        try
        {
            T.DeserializeGuarded(ref reader, out var value, ref tracker, ref guard);
            return value;
        }
        finally
        {
            tracker.Dispose();
        }
    }

    // ================= arrays =================

    /// <summary>[ApexPackable] 型の配列を書く(要素は null 可)。</summary>
    public static byte[] SerializeArray<T>(T[]? items) where T : ISerializePackable<T>
    {
        var buffer = ApexThreadBuffer.Rent();
        var writer = new ApexBufferWriter(buffer);
        try
        {
            var tracker = new NullReferenceTracker();
            ArrayFormatter<T, PackableFormatter<T>>.Write(ref writer, in items, ref tracker);
            return writer.ToArray();
        }
        finally
        {
            writer.Dispose();
            ApexThreadBuffer.Return(buffer);
        }
    }

    /// <summary><see cref="SerializeArray{T}"/> の出力を復元する。</summary>
    public static T[]? DeserializeArray<T>(ReadOnlySpan<byte> bytes) where T : ISerializePackable<T>
    {
        var reader = new ApexSpanReader(bytes);
        var tracker = new NullReferenceTracker();
        var guard = new TrustedSecurityGuard();
        return ArrayFormatter<T, PackableFormatter<T>>.Read(ref reader, ref tracker, ref guard);
    }

    /// <summary>スカラー要素の配列を書く(LE 機では一括コピー)。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] SerializeUnmanagedArray<T>(T[]? items) where T : unmanaged
    {
        if (items is null) return new byte[] { 0 };

        int body = checked(items.Length * ApexWire.SizeOf<T>());
        uint header = (uint)items.Length + 1;
        int headerSize = ApexWire.CompactSize(header);
        var result = GC.AllocateUninitializedArray<byte>(headerSize + body);
        ref byte dest = ref MemoryMarshal.GetArrayDataReference(result);
        ApexWire.WriteCompact(ref dest, header, BitConverter.IsLittleEndian);
        var writer = new ApexSpanWriter(result.AsSpan(headerSize));
        var tracker = new NullReferenceTracker();
        ApexSequence.WriteElements<ApexSpanWriter, NullReferenceTracker, T, ScalarFormatter<T>>(ref writer, items, ref tracker);
        return result;
    }

    /// <summary><see cref="SerializeUnmanagedArray{T}(T[])"/> の出力を復元する。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T[]? DeserializeUnmanagedArray<T>(ReadOnlySpan<byte> bytes) where T : unmanaged
    {
        var reader = new ApexSpanReader(bytes);
        var tracker = new NullReferenceTracker();
        var guard = new TrustedSecurityGuard();
        return ArrayFormatter<T, ScalarFormatter<T>>.Read(ref reader, ref tracker, ref guard);
    }
}
