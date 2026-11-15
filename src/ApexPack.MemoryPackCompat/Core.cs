using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using ApexPack;

namespace MemoryPack;

public record MemoryPackSerializerOptions
{
    // MemoryPack の既定は UTF-8。ApexPack の文字列形式は 1 つ(UTF-16 か Latin-1 を自動で選ぶ)なので、既定・Utf8 は同じ扱いにする
    public static readonly MemoryPackSerializerOptions Default = new() { StringEncoding = StringEncoding.Utf8 };
    public static readonly MemoryPackSerializerOptions Utf8 = Default with { StringEncoding = StringEncoding.Utf8 };
    public static readonly MemoryPackSerializerOptions Utf16 = Default with { StringEncoding = StringEncoding.Utf16 };

    public StringEncoding StringEncoding { get; init; }
    public IServiceProvider? ServiceProvider { get; init; }
}

public enum StringEncoding : byte
{
    Utf16,
    Utf8,
}

/// <summary>
/// 低レベル API の符号。値は ApexPack のワイヤ形式に合わせる(null は 0、オブジェクトヘッダーは「メンバー数 + 1」)。
/// MemoryPack の値(null = 255)とは違う。
/// </summary>
public static class MemoryPackCode
{
    public const int NullCollection = -1;

    /// <summary>オブジェクトヘッダーに書けない値の下限(ApexPack は 255 を書けない)。</summary>
    public const byte Reserved1 = 255;

    public const byte NullObject = 0;
}

public class MemoryPackSerializationException : Exception
{
    public MemoryPackSerializationException(string message) : base(message) { }

    public MemoryPackSerializationException(string message, Exception innerException) : base(message, innerException) { }

    [DoesNotReturn]
    public static void ThrowMessage(string message) => throw new MemoryPackSerializationException(message);

    [DoesNotReturn]
    public static void ThrowInvalidPropertyCount(byte expected, byte actual)
        => throw new MemoryPackSerializationException($"Current object's property count is {expected} but binary's header maked as {actual}, can't deserialize about versioning.");

    [DoesNotReturn]
    public static void ThrowInvalidPropertyCount(Type type, byte expected, byte actual)
        => throw new MemoryPackSerializationException($"{type.FullName} property count is {expected} but binary's header maked as {actual}, can't deserialize about versioning.");

    [DoesNotReturn]
    public static void ThrowInvalidCollection() => throw new MemoryPackSerializationException("Current read to collection, the buffer header is not collection.");

    [DoesNotReturn]
    public static void ThrowInvalidRange(int expected, int actual)
        => throw new MemoryPackSerializationException($"Requires size is {expected} but buffer length is {actual}.");

    [DoesNotReturn]
    public static void ThrowInvalidAdvance() => throw new MemoryPackSerializationException("Cannot advance past the end of the buffer.");

    [DoesNotReturn]
    public static void ThrowSequenceReachedEnd() => throw new MemoryPackSerializationException("Sequence reached end, reader can not provide more buffer.");

    [DoesNotReturn]
    public static void ThrowWriteInvalidMemberCount(byte memberCount)
        => throw new MemoryPackSerializationException($"MemberCount/Tag allows < 255 but try to write {memberCount}.");

    [DoesNotReturn]
    public static void ThrowInsufficientBufferUnless(int length)
        => throw new MemoryPackSerializationException($"Length header size is larger than buffer size, length: {length}.");

    [DoesNotReturn]
    public static void ThrowNotRegisteredInProvider(Type type)
        => throw new MemoryPackSerializationException($"{type.FullName} is not registered in this provider.");

    [DoesNotReturn]
    public static void ThrowNotFoundInUnionType(Type actualType, Type baseType)
        => throw new MemoryPackSerializationException($"Type {actualType.FullName} is not annotated in {baseType.FullName} MemoryPackUnion.");

    [DoesNotReturn]
    public static void ThrowInvalidTag(ushort tag, Type baseType)
        => throw new MemoryPackSerializationException($"Data read tag: {tag} but not found in {baseType.FullName} MemoryPackUnion annotations.");

    [DoesNotReturn]
    public static void ThrowReachedDepthLimit(Type type)
        => throw new MemoryPackSerializationException($"Serializing Type '{type}' reached depth limit, maybe detect circular reference.");

    [DoesNotReturn]
    public static void ThrowDeserializeObjectIsNull(string target)
        => throw new MemoryPackSerializationException($"Deserialized {target} is null.");

    [DoesNotReturn]
    public static void ThrowFailedEncoding(OperationStatus status)
        => throw new MemoryPackSerializationException($"Failed in Utf8 encoding/decoding process, status: {status}.");
}

#pragma warning disable CA1040 // 空のインターフェース(MemoryPack と同じ形)

public interface IFixedSizeMemoryPackable
{
    static abstract int Size { get; }
}

public interface IMemoryPackFormatterRegister
{
    static abstract void RegisterFormatter();
}

public interface IMemoryPackable<T> : IMemoryPackFormatterRegister
{
    static abstract void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;

    static abstract void Deserialize(ref MemoryPackReader reader, scoped ref T? value);
}

public interface IMemoryPackFormatter
{
    void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref object? value)
        where TBufferWriter : IBufferWriter<byte>;

    void Deserialize(ref MemoryPackReader reader, scoped ref object? value);
}

public interface IMemoryPackFormatter<T>
{
    void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;

    void Deserialize(ref MemoryPackReader reader, scoped ref T? value);
}

/// <summary>
/// MemoryPack 形式の手書きフォーマッタ。<see cref="MemoryPackFormatterProvider.Register{T}(MemoryPackFormatter{T})"/> で登録すると、
/// ApexPack の <see cref="ApexFormatterCache{T}"/> に橋渡しして使われる(書き込みは ApexPack の writer の上の <see cref="MemoryPackWriter{TBufferWriter}"/>)。
/// </summary>
public abstract class MemoryPackFormatter<T> : IMemoryPackFormatter<T>, IMemoryPackFormatter
{
    public abstract void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
        where TBufferWriter : IBufferWriter<byte>;

    public abstract void Deserialize(ref MemoryPackReader reader, scoped ref T? value);

    void IMemoryPackFormatter.Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref object? value)
    {
        var v = value is null ? default : (T?)value;
        Serialize(ref writer, ref v);
    }

    void IMemoryPackFormatter.Deserialize(ref MemoryPackReader reader, scoped ref object? value)
    {
        var v = value is null ? default : (T?)value;
        Deserialize(ref reader, ref v);
        value = v;
    }
}
