using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using ApexPack;
using ApexPack.Buffers;

namespace MemoryPack;

/// <summary>
/// MemoryPack と同じ署名の入口。中身は ApexPack(ワイヤ形式も ApexPack)。
/// </summary>
/// <remarks>
/// 対象外の機能は <see cref="ApexFeatureNotSupportedException"/>(機能名つき)を投げる:
/// 既存インスタンスへの上書き(参照型の ref 引数に値が入っている)、<see cref="MemoryPackSerializerOptions.Utf16"/>。
/// 壊れた入力など ApexPack の例外は <see cref="MemoryPackSerializationException"/> に包む(MemoryPack と同じ例外で受けられるように)。
/// </remarks>
public static class MemoryPackSerializer
{
    // ================= serialize =================

    public static byte[] Serialize<T>(in T? value, MemoryPackSerializerOptions? options = default)
    {
        CheckOptions(options);
        try
        {
            return ApexPackSerializer.Serialize<T>(value!);
        }
        catch (Exception e) when (ShouldWrap(e))
        {
            throw Wrap(e);
        }
    }

    /// <summary>書き込み先は in で受け取るが、構造体の書き込み先でも呼び出し元の変数に結果が残る(MemoryPack と同じ)。</summary>
    public static void Serialize<T, TBufferWriter>(in TBufferWriter bufferWriter, in T? value, MemoryPackSerializerOptions? options = default)
        where TBufferWriter : IBufferWriter<byte>
    {
        CheckOptions(options);
        try
        {
            var writer = new ApexBufferWriterAdapter<TBufferWriter>(ref Unsafe.AsRef(in bufferWriter));
            ApexFormatterCache<T>.Instance.WriteRoot(ref writer, in value!);
            writer.Flush();
        }
        catch (Exception e) when (ShouldWrap(e))
        {
            throw Wrap(e);
        }
    }

    public static void Serialize<T, TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, in T? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        CheckOptions(writer.Options);
        try
        {
            ApexFormatterCache<T>.Instance.WriteRoot(ref writer, in value!);
        }
        catch (Exception e) when (ShouldWrap(e))
        {
            throw Wrap(e);
        }
    }

    public static async ValueTask SerializeAsync<T>(Stream stream, T? value, MemoryPackSerializerOptions? options = default, CancellationToken cancellationToken = default)
    {
        var bytes = Serialize(in value, options);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    // ---- Type 指定 ----

    public static byte[] Serialize(Type type, object? value, MemoryPackSerializerOptions? options = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        CheckOptions(options);
        try
        {
            return ApexPackSerializer.Serialize(type, value);
        }
        catch (Exception e) when (ShouldWrap(e))
        {
            throw Wrap(e);
        }
    }

    public static void Serialize<TBufferWriter>(Type type, in TBufferWriter bufferWriter, object? value, MemoryPackSerializerOptions? options = default)
        where TBufferWriter : IBufferWriter<byte>
    {
        var bytes = Serialize(type, value, options);
        ref var destination = ref Unsafe.AsRef(in bufferWriter);
        destination.Write(bytes);
    }

    public static async ValueTask SerializeAsync(Type type, Stream stream, object? value, MemoryPackSerializerOptions? options = default, CancellationToken cancellationToken = default)
    {
        var bytes = Serialize(type, value, options);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    // ================= deserialize =================

    public static T? Deserialize<T>(ReadOnlySpan<byte> buffer, MemoryPackSerializerOptions? options = default)
    {
        try
        {
            return ApexPackSerializer.Deserialize<T>(buffer);
        }
        catch (Exception e) when (ShouldWrap(e))
        {
            throw Wrap(e);
        }
    }

    public static int Deserialize<T>(ReadOnlySpan<byte> buffer, ref T? value, MemoryPackSerializerOptions? options = default)
    {
        CheckOverwrite(value);
        try
        {
            value = ApexFormatterCache<T>.Instance.Deserialize(buffer, out int consumed);
            return consumed;
        }
        catch (Exception e) when (ShouldWrap(e))
        {
            throw Wrap(e);
        }
    }

    public static T? Deserialize<T>(in ReadOnlySequence<byte> buffer, MemoryPackSerializerOptions? options = default)
    {
        T? value = default;
        Deserialize(buffer, ref value, options);
        return value;
    }

    public static int Deserialize<T>(in ReadOnlySequence<byte> buffer, ref T? value, MemoryPackSerializerOptions? options = default)
    {
        if (buffer.IsSingleSegment) return Deserialize(buffer.FirstSpan, ref value, options);
        CheckOverwrite(value);
        int length = checked((int)buffer.Length);
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            buffer.CopyTo(rented);
            return Deserialize(rented.AsSpan(0, length), ref value, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public static async ValueTask<T?> DeserializeAsync<T>(Stream stream, MemoryPackSerializerOptions? options = default, CancellationToken cancellationToken = default)
    {
        // MemoryStream は残りのバッファから直接読み、読んだ分だけ位置を進める(続けて次の値を読める。MemoryPack と同じ)
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment))
        {
            T? value = default;
            int position = (int)ms.Position;
            int consumed = Deserialize(segment.AsSpan(position), ref value, options);
            ms.Position = position + consumed;
            return value;
        }
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Deserialize<T>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), options);
    }

    // ---- Type 指定 ----

    public static object? Deserialize(Type type, ReadOnlySpan<byte> buffer, MemoryPackSerializerOptions? options = default)
    {
        object? value = null;
        Deserialize(type, buffer, ref value, options);
        return value;
    }

    public static int Deserialize(Type type, ReadOnlySpan<byte> buffer, ref object? value, MemoryPackSerializerOptions? options = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        // 参照型の既存インスタンスへの上書きは対象外(値型は値が入っていても新しく作って返す。型引数つきの入口と同じ)
        if (!type.IsValueType && value is not null) ApexCompatThrow.Overwrite();
        try
        {
            value = ApexPackSerializer.Deserialize(type, buffer, out int consumed);
            return consumed;
        }
        catch (Exception e) when (ShouldWrap(e))
        {
            throw Wrap(e);
        }
    }

    public static object? Deserialize(Type type, in ReadOnlySequence<byte> buffer, MemoryPackSerializerOptions? options = default)
        => Deserialize(type, buffer.ToArray(), options);

    public static int Deserialize(Type type, in ReadOnlySequence<byte> buffer, ref object? value, MemoryPackSerializerOptions? options = default)
        => Deserialize(type, buffer.ToArray(), ref value, options);

    public static async ValueTask<object?> DeserializeAsync(Type type, Stream stream, MemoryPackSerializerOptions? options = default, CancellationToken cancellationToken = default)
    {
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment))
        {
            object? value = null;
            int position = (int)ms.Position;
            int consumed = Deserialize(type, segment.AsSpan(position), ref value, options);
            ms.Position = position + consumed;
            return value;
        }
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Deserialize(type, buffer.ToArray(), options);
    }

    // ================= 共通 =================

    private static void CheckOptions(MemoryPackSerializerOptions? options)
    {
        if (options is { StringEncoding: StringEncoding.Utf16 })
        {
            ApexCompatThrow.Feature(ApexCompatThrow.StringEncodingFeature);
        }
    }

    private static void CheckOverwrite<T>(T? value)
    {
        if (!typeof(T).IsValueType && value is not null) ApexCompatThrow.Overwrite();
    }

    /// <summary>ApexPack の入力エラーを MemoryPack の例外に包む。対象外の機能と利用者コードの例外はそのまま通す。</summary>
    private static bool ShouldWrap(Exception e)
        => e is not ApexFeatureNotSupportedException and not MemoryPackSerializationException
           && e is ApexSecurityException or InvalidOperationException or NotSupportedException or IndexOutOfRangeException or ArgumentOutOfRangeException;

    private static MemoryPackSerializationException Wrap(Exception e) => new(e.Message, e);
}
