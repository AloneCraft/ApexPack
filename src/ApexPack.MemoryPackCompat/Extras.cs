using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ApexPack;

namespace MemoryPack.Streaming
{
    /// <summary>
    /// MemoryPack と同じ署名の逐次直列化。出力は「件数 + 要素」で、<c>T[]</c> として読める(ApexPack の配列形式)。
    /// </summary>
    /// <remarks>
    /// ApexPack の配列は要素型によって形が変わる(null 許容値の配列はビット列など)ため、要素を集めてから配列として書く。
    /// 逐次に書く(メモリを抑える)性質は持たない。
    /// </remarks>
    public static class MemoryPackStreamingSerializer
    {
        public static async ValueTask SerializeAsync<T>(PipeWriter pipeWriter, int count, IEnumerable<T> source, int flushRate = 4096, MemoryPackSerializerOptions? options = default, CancellationToken cancellationToken = default)
        {
            var bytes = MemoryPackSerializer.Serialize(Collect(count, source), options);
            await pipeWriter.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        public static async ValueTask SerializeAsync<T>(Stream stream, int count, IEnumerable<T> source, int flushRate = 4096, MemoryPackSerializerOptions? options = default, CancellationToken cancellationToken = default)
        {
            var bytes = MemoryPackSerializer.Serialize(Collect(count, source), options);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        public static async IAsyncEnumerable<T?> DeserializeAsync<T>(PipeReader pipeReader, int bufferAtLeast = 4096, int readMinimumSize = 8192, MemoryPackSerializerOptions? options = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await pipeReader.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var items = MemoryPackSerializer.Deserialize<T?[]>(buffer.ToArray(), options) ?? Array.Empty<T?>();
            foreach (var item in items) yield return item;
        }

        public static async IAsyncEnumerable<T?> DeserializeAsync<T>(Stream stream, int bufferAtLeast = 4096, int readMinimumSize = 8192, MemoryPackSerializerOptions? options = default, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var items = MemoryPackSerializer.Deserialize<T?[]>(buffer.ToArray(), options) ?? Array.Empty<T?>();
            foreach (var item in items) yield return item;
        }

        private static T[] Collect<T>(int count, IEnumerable<T> source)
        {
            var items = source as T[] ?? source.ToArray();
            if (items.Length != count) throw new ArgumentException($"count {count} does not match the number of items {items.Length}.", nameof(count));
            return items;
        }
    }
}

namespace MemoryPack.Compression
{
    // Brotli 圧縮は対象外。型と署名だけ用意し、使うと機能名つきの例外を投げる。

    public struct BrotliCompressor : IBufferWriter<byte>, IDisposable
    {
        public BrotliCompressor() => ApexCompatThrow.Feature(ApexCompatThrow.BrotliFeature);

        public BrotliCompressor(CompressionLevel compressionLevel) : this() { }

        public BrotliCompressor(CompressionLevel compressionLevel, int window) : this() { }

        public BrotliCompressor(int quality = 1, int window = 22) : this() { }

        public readonly void Advance(int count) => ApexCompatThrow.Feature(ApexCompatThrow.BrotliFeature);

        public readonly Memory<byte> GetMemory(int sizeHint = 0) => throw new ApexFeatureNotSupportedException(ApexCompatThrow.BrotliFeature);

        public readonly Span<byte> GetSpan(int sizeHint = 0) => throw new ApexFeatureNotSupportedException(ApexCompatThrow.BrotliFeature);

        public readonly byte[] ToArray() => throw new ApexFeatureNotSupportedException(ApexCompatThrow.BrotliFeature);

        public readonly void CopyTo<TBufferWriter>(in TBufferWriter destBufferWriter)
            where TBufferWriter : IBufferWriter<byte>
            => ApexCompatThrow.Feature(ApexCompatThrow.BrotliFeature);

        public readonly void CopyTo<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> memoryPackWriter)
            where TBufferWriter : IBufferWriter<byte>
            => ApexCompatThrow.Feature(ApexCompatThrow.BrotliFeature);

        public readonly ValueTask CopyToAsync(Stream stream, int bufferSize = 65535, CancellationToken cancellationToken = default)
            => throw new ApexFeatureNotSupportedException(ApexCompatThrow.BrotliFeature);

        public readonly void Dispose()
        {
        }
    }

    public struct BrotliDecompressor : IDisposable
    {
        public readonly ReadOnlySequence<byte> Decompress(ReadOnlySpan<byte> compressedSpan)
            => throw new ApexFeatureNotSupportedException(ApexCompatThrow.BrotliFeature);

        public readonly ReadOnlySequence<byte> Decompress(ReadOnlySpan<byte> compressedSpan, out int consumed)
            => throw new ApexFeatureNotSupportedException(ApexCompatThrow.BrotliFeature);

        public readonly ReadOnlySequence<byte> Decompress(ReadOnlySequence<byte> compressedSequence)
            => throw new ApexFeatureNotSupportedException(ApexCompatThrow.BrotliFeature);

        public readonly void Dispose()
        {
        }
    }
}

namespace MemoryPack
{
    // MemoryPack 独自の型(対象外)。値の型としては使えるが、ApexPack は専用の形式を持たない。
    // 比較の分類は Runner が型名(テストクラス)で行う。

    /// <summary>MemoryPack 独自: アンマネージ型の Nullable を安全に包む型。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SafeNullable<T> : IEquatable<SafeNullable<T>>
        where T : unmanaged
    {
        private readonly byte _hasValue;
        private readonly T _value;

        public SafeNullable(T value)
        {
            _hasValue = 1;
            _value = value;
        }

        public SafeNullable(T? value)
        {
            _hasValue = value.HasValue ? (byte)1 : (byte)0;
            _value = value.GetValueOrDefault();
        }

        public bool HasValue => _hasValue != 0;

        public T Value => HasValue ? _value : throw new InvalidOperationException("Nullable object must have a value.");

        public T GetValueOrDefault() => HasValue ? _value : default;

        public T GetValueOrDefault(T defaultValue) => HasValue ? _value : defaultValue;

        public T? ToNullable() => HasValue ? _value : null;

        public static implicit operator SafeNullable<T>(T? value) => new(value);

        public static implicit operator T?(SafeNullable<T> value) => value.ToNullable();

        public bool Equals(SafeNullable<T> other)
            => HasValue == other.HasValue && (!HasValue || EqualityComparer<T>.Default.Equals(_value, other._value));

        public override bool Equals(object? obj) => obj is SafeNullable<T> other && Equals(other);

        public override int GetHashCode() => HasValue ? _value.GetHashCode() : 0;

        public override string ToString() => HasValue ? _value.ToString() ?? "" : "";
    }

    /// <summary>MemoryPack 独自: .NET 10 の DateTimeOffset の内部表現に合わせた型。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public readonly struct Net10DateTimeOffset
    {
        [FieldOffset(0)]
        private readonly short _offsetMinutes;

        [FieldOffset(8)]
        private readonly long _utcTicks;

        public Net10DateTimeOffset(DateTimeOffset value)
        {
            _offsetMinutes = (short)value.Offset.TotalMinutes;
            _utcTicks = value.UtcTicks;
        }

        public DateTimeOffset ToDateTimeOffset() => new DateTimeOffset(_utcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(_offsetMinutes));

        public static implicit operator Net10DateTimeOffset(DateTimeOffset value) => new(value);

        public static implicit operator DateTimeOffset(Net10DateTimeOffset value) => value.ToDateTimeOffset();

        public override string ToString() => ToDateTimeOffset().ToString();
    }

    /// <summary>MemoryPack 独自: Unity の DateTimeOffset の内部表現に合わせた型。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public readonly struct UnityDateTimeOffset
    {
        [FieldOffset(0)]
        private readonly long _dateTimeTicks;

        [FieldOffset(8)]
        private readonly short _offsetMinutes;

        public UnityDateTimeOffset(DateTimeOffset value)
        {
            _dateTimeTicks = value.UtcTicks;
            _offsetMinutes = (short)value.Offset.TotalMinutes;
        }

        public DateTimeOffset ToDateTimeOffset() => new DateTimeOffset(_dateTimeTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(_offsetMinutes));

        public static implicit operator UnityDateTimeOffset(DateTimeOffset value) => new(value);

        public static implicit operator DateTimeOffset(UnityDateTimeOffset value) => value.ToDateTimeOffset();

        public override string ToString() => ToDateTimeOffset().ToString();
    }
}
