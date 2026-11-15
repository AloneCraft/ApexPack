// 計測用の仲介クラス。上流テストの MemoryPackSerializer 呼び出しを横取りして記録し、本物へ渡す。
//
// テストファイルは namespace MemoryPack.Tests の中にあり、MemoryPackSerializer という名前は
// 「MemoryPack.Tests の型 → 外側の MemoryPack の型 → using」の順で探される。
// そのため global using の別名では横取りできず(外側の名前空間の MemoryPack.MemoryPackSerializer が先に見つかる)、
// 同じ名前の型を MemoryPack.Tests に置いている。呼び先 global::MemoryPack.MemoryPackSerializer は、
// Upstream.OnMemoryPack では本物の MemoryPack、Upstream.OnApexPack では互換層(ApexPack)になる。

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Upstream.Harness;
using Backend = global::MemoryPack.MemoryPackSerializer;
using Options = global::MemoryPack.MemoryPackSerializerOptions;

namespace MemoryPack.Tests;

public static class MemoryPackSerializer
{
    // ================= serialize =================

    public static byte[] Serialize<T>(in T? value, Options? options = default)
    {
        var bytes = Backend.Serialize(in value, options);
        Recorder.Serialized(value, options);
        return bytes;
    }

    public static void Serialize<T, TBufferWriter>(in TBufferWriter bufferWriter, in T? value, Options? options = default)
        where TBufferWriter : IBufferWriter<byte>
    {
        Backend.Serialize(in bufferWriter, in value, options);
        Recorder.Serialized(value, options);
    }

    public static void Serialize<T, TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, in T? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        Backend.Serialize(ref writer, in value);
        Recorder.Serialized(value, null);
    }

    public static async ValueTask SerializeAsync<T>(Stream stream, T? value, Options? options = default, CancellationToken cancellationToken = default)
    {
        await Backend.SerializeAsync(stream, value, options, cancellationToken).ConfigureAwait(false);
        Recorder.Serialized(value, options);
    }

    public static byte[] Serialize(Type type, object? value, Options? options = default)
    {
        var bytes = Backend.Serialize(type, value, options);
        Recorder.SerializedObject(type, value, options);
        return bytes;
    }

    public static void Serialize<TBufferWriter>(Type type, in TBufferWriter bufferWriter, object? value, Options? options = default)
        where TBufferWriter : IBufferWriter<byte>
    {
        Backend.Serialize(type, in bufferWriter, value, options);
        Recorder.SerializedObject(type, value, options);
    }

    public static async ValueTask SerializeAsync(Type type, Stream stream, object? value, Options? options = default, CancellationToken cancellationToken = default)
    {
        await Backend.SerializeAsync(type, stream, value, options, cancellationToken).ConfigureAwait(false);
        Recorder.SerializedObject(type, value, options);
    }

    // ================= deserialize =================

    public static T? Deserialize<T>(ReadOnlySpan<byte> buffer, Options? options = default)
    {
        var value = Backend.Deserialize<T>(buffer, options);
        Recorder.Deserialized<T>(buffer, options);
        return value;
    }

    public static int Deserialize<T>(ReadOnlySpan<byte> buffer, ref T? value, Options? options = default)
    {
        int consumed = Backend.Deserialize(buffer, ref value, options);
        Recorder.Deserialized<T>(buffer.Slice(0, consumed), options);
        return consumed;
    }

    public static T? Deserialize<T>(in ReadOnlySequence<byte> buffer, Options? options = default)
    {
        var value = Backend.Deserialize<T>(in buffer, options);
        Recorder.Deserialized<T>(buffer.ToArray(), options);
        return value;
    }

    public static int Deserialize<T>(in ReadOnlySequence<byte> buffer, ref T? value, Options? options = default)
    {
        int consumed = Backend.Deserialize(in buffer, ref value, options);
        Recorder.Deserialized<T>(buffer.Slice(0, consumed).ToArray(), options);
        return consumed;
    }

    public static async ValueTask<T?> DeserializeAsync<T>(Stream stream, Options? options = default, CancellationToken cancellationToken = default)
    {
        // MemoryStream は読んだ分だけ位置が進む(続けて次の値を読むテストがある)ので、そのまま渡して読んだ範囲を記録する
        if (stream is MemoryStream ms)
        {
            long start = ms.Position;
            var result = await Backend.DeserializeAsync<T>(ms, options, cancellationToken).ConfigureAwait(false);
            Recorder.Deserialized<T>(Recorder.Slice(ms, start), options);
            return result;
        }
        // それ以外のストリームは中身を記録するため、先に読み切ってから渡す
        var bytes = await Recorder.ReadAllAsync(stream, cancellationToken).ConfigureAwait(false);
        var value = await Backend.DeserializeAsync<T>(new MemoryStream(bytes), options, cancellationToken).ConfigureAwait(false);
        Recorder.Deserialized<T>(bytes, options);
        return value;
    }

    public static object? Deserialize(Type type, ReadOnlySpan<byte> buffer, Options? options = default)
    {
        var value = Backend.Deserialize(type, buffer, options);
        Recorder.DeserializedObject(type, buffer, options);
        return value;
    }

    public static int Deserialize(Type type, ReadOnlySpan<byte> buffer, ref object? value, Options? options = default)
    {
        int consumed = Backend.Deserialize(type, buffer, ref value, options);
        Recorder.DeserializedObject(type, buffer.Slice(0, consumed), options);
        return consumed;
    }

    public static object? Deserialize(Type type, in ReadOnlySequence<byte> buffer, Options? options = default)
    {
        var value = Backend.Deserialize(type, in buffer, options);
        Recorder.DeserializedObject(type, buffer.ToArray(), options);
        return value;
    }

    public static int Deserialize(Type type, in ReadOnlySequence<byte> buffer, ref object? value, Options? options = default)
    {
        int consumed = Backend.Deserialize(type, in buffer, ref value, options);
        Recorder.DeserializedObject(type, buffer.Slice(0, consumed).ToArray(), options);
        return consumed;
    }

    public static async ValueTask<object?> DeserializeAsync(Type type, Stream stream, Options? options = default, CancellationToken cancellationToken = default)
    {
        if (stream is MemoryStream ms)
        {
            long start = ms.Position;
            var result = await Backend.DeserializeAsync(type, ms, options, cancellationToken).ConfigureAwait(false);
            Recorder.DeserializedObject(type, Recorder.Slice(ms, start), options);
            return result;
        }
        var bytes = await Recorder.ReadAllAsync(stream, cancellationToken).ConfigureAwait(false);
        var value = await Backend.DeserializeAsync(type, new MemoryStream(bytes), options, cancellationToken).ConfigureAwait(false);
        Recorder.DeserializedObject(type, bytes, options);
        return value;
    }
}
