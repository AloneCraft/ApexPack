using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using ApexPack;
using ApexPack.Buffers;

namespace MemoryPack
{
    /// <summary>互換層で対象外とした機能の例外。機能名は Runner の分類と画面の表示に使う。</summary>
    internal static class ApexCompatThrow
    {
        public const string OverwriteFeature = "既存インスタンスへの上書き(Deserialize の ref 引数)";
        public const string StringEncodingFeature = "文字列の符号化オプション(MemoryPackSerializerOptions.Utf16)";
        public const string BrotliFeature = "Brotli 圧縮";
        public const string LateReplaceFeature = "使った後のフォーマッタ差し替え";

        [DoesNotReturn]
        public static void Overwrite() => throw new ApexFeatureNotSupportedException(OverwriteFeature);

        [DoesNotReturn]
        public static void Feature(string feature) => throw new ApexFeatureNotSupportedException(feature);
    }
}

namespace MemoryPack.Internal
{
    /// <summary>
    /// 生成コード用。MemoryPack 互換の、writer / reader を受け取るコールバックが読み書きする塊(長さ付きのバイト列)。
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ApexCompatBlob
    {
        // 呼び出しごとに一時バッファを作らないよう、スレッドごとに 1 つ使い回す。
        // 使用中(コールバックの中から入れ子で直列化した場合など)は空なので、そのときだけ新しく作る
        [ThreadStatic]
        private static ArrayBufferWriter<byte>? t_writer;

        // これより大きく伸びたバッファは使い回さない(一度だけの大きな値でメモリを握り続けないため)
        private const int MaxRetainedCapacity = 64 * 1024;

        public static ArrayBufferWriter<byte> RentWriter()
        {
            var writer = t_writer;
            if (writer is null) return new ArrayBufferWriter<byte>();
            t_writer = null;
            return writer;
        }

        public static void ReturnWriter(ArrayBufferWriter<byte> writer)
        {
            if (writer.Capacity > MaxRetainedCapacity) return;
            writer.ResetWrittenCount();
            t_writer = writer;
        }

        public static void Write<TWriter>(ref TWriter writer, ArrayBufferWriter<byte> buffer)
            where TWriter : struct, IApexBufferWriter, allows ref struct
        {
            var span = buffer.WrittenSpan;
            writer.WriteCompactUInt((uint)span.Length);
            writer.WriteRaw(span);
        }

        /// <summary>塊を読む。返す範囲は入力そのもの(写さない)なので、続けて reader を読む前に使い終えること。</summary>
        public static ReadOnlySpan<byte> Read<TReader>(ref TReader reader)
            where TReader : struct, IApexBufferReader, allows ref struct
        {
            int length = checked((int)reader.ReadCompactUInt());
            return reader.ReadRaw(length);
        }

        [DoesNotReturn]
        public static void ThrowOverwrite() => ApexCompatThrow.Overwrite();
    }

    /// <summary>
    /// <see cref="MemoryPackFormatter{T}"/> を ApexPack のフォーマッタとして使う橋渡し。
    /// 書き込みは一時バッファの <see cref="MemoryPackWriter{TBufferWriter}"/> に書いてから写す。
    /// 読み取りは残りの入力の上に <see cref="MemoryPackReader"/> を作り、読んだ分だけ元の reader を進める。
    /// </summary>
    internal sealed class MemoryPackFormatterBridge<T> : ApexFormatter<T>
    {
        private readonly MemoryPackFormatter<T> _formatter;

        public MemoryPackFormatterBridge(MemoryPackFormatter<T> formatter) => _formatter = formatter;

        public MemoryPackFormatter<T> Formatter => _formatter;

        public override void Write<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        {
            var buffer = ApexCompatBlob.RentWriter();
            var mp = new MemoryPackWriter<ArrayBufferWriter<byte>>(ref buffer, null);
            T? v = value;
            _formatter.Serialize(ref mp, ref v);
            mp.Flush();
            writer.WriteRaw(buffer.WrittenSpan);
            ApexCompatBlob.ReturnWriter(buffer);
        }

        // ルートの入口は ApexPack の writer / reader を通さず、MemoryPack の writer / reader で直接読み書きする
        // (基底の経路はジェネリックな仮想呼び出しと、一時バッファからの二重の写しが入る)

        public override byte[] SerializeToArray(scoped in T value)
        {
            var buffer = ApexCompatBlob.RentWriter();
            var mp = new MemoryPackWriter<ArrayBufferWriter<byte>>(ref buffer, null);
            T? v = value;
            _formatter.Serialize(ref mp, ref v);
            mp.Flush();
            var result = buffer.WrittenSpan.ToArray();
            ApexCompatBlob.ReturnWriter(buffer);
            return result;
        }

        public override T Deserialize(ReadOnlySpan<byte> source, out int consumed)
        {
            var mp = new MemoryPackReader(source, null);
            T? value = default;
            _formatter.Deserialize(ref mp, ref value);
            consumed = mp.Consumed;
            return value!;
        }

        // sealed なので、ここからの Write / Read は仮想呼び出しにならない(入れ子で実行時に解決される位置の入口)
        public override void WriteTo(ref ApexBufferWriter writer, scoped in T value)
        {
            var tracker = new NullReferenceTracker();
            Write(ref writer, in value, ref tracker);
        }

        public override T ReadFrom(ref ApexSpanReader reader)
        {
            var tracker = new NullReferenceTracker();
            var guard = new TrustedSecurityGuard();
            return Read(ref reader, ref tracker, ref guard);
        }

        public override T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        {
            // GetSpanRef(0) は位置を進めずに現在位置の参照を返す
            var rest = MemoryMarshal.CreateReadOnlySpan(in reader.GetSpanRef(0), reader.Remaining);
            var mp = new MemoryPackReader(rest, null);
            T? value = default;
            _formatter.Deserialize(ref mp, ref value);
            _ = reader.GetSpanRef(mp.Consumed);
            return value!;
        }
    }
}
