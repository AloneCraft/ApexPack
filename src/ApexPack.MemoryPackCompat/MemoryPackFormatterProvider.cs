using System.Diagnostics.CodeAnalysis;
using ApexPack;
using MemoryPack.Internal;

namespace MemoryPack
{
    /// <summary>
    /// MemoryPack のフォーマッタ登録。登録先は ApexPack の <see cref="ApexFormatterCache{T}"/> と <see cref="ApexFormatterRegistry"/>
    /// (どちらも型を強く握らない。アンロード可能性の不変条件)。
    /// </summary>
    public static class MemoryPackFormatterProvider
    {
        /// <summary>
        /// フォーマッタを登録する。ApexPack はその型を一度使うとフォーマッタを固定するので、使った後の差し替えは対象外。
        /// </summary>
        public static void Register<T>(MemoryPackFormatter<T> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            ApexFormatter<T> apex = formatter is IApexFormatterSource<T> source
                ? source.CreateApexFormatter()
                : new MemoryPackFormatterBridge<T>(formatter);
            if (!ApexFormatterCache<T>.Register(apex))
            {
                ApexCompatThrow.Feature(ApexCompatThrow.LateReplaceFeature);
            }
        }

        public static void Register<T>()
            where T : IMemoryPackFormatterRegister
            => T.RegisterFormatter();

        /// <summary>開いたジェネリック型に、開いたフォーマッタ型(<see cref="MemoryPackFormatter{T}"/> の派生)を登録する。</summary>
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "ジェネリック型の一括登録は実行時に型を閉じる")]
        [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "同上")]
        [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "同上")]
        [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "同上")]
        public static void RegisterGenericType(Type genericType, Type genericFormatterType)
        {
            ArgumentNullException.ThrowIfNull(genericType);
            ArgumentNullException.ThrowIfNull(genericFormatterType);
            ApexFormatterRegistry.RegisterGenericFactory(genericType, closed =>
            {
                var formatterType = genericFormatterType.MakeGenericType(closed.GetGenericArguments());
                var formatter = Activator.CreateInstance(formatterType)!;
                // フォーマッタが扱う型は closed と注釈(?)だけ違うことがある。実行時の型は同じ
                var bridgeType = typeof(MemoryPackFormatterBridge<>).MakeGenericType(closed);
                return Activator.CreateInstance(bridgeType, formatter)!;
            });
        }

        /// <summary>独自のコレクション型を登録する(件数 + 要素の形式)。</summary>
        public static void RegisterCollection<TCollection, TElement>()
            where TCollection : ICollection<TElement?>, new()
            => ApexFormatterCache<TCollection>.Register(new ApexCollectionFormatter<TCollection, TElement>());
    }

    /// <summary>ApexPack のフォーマッタを直接作れる互換フォーマッタ(橋渡しを通さない)。</summary>
    internal interface IApexFormatterSource<T>
    {
        ApexFormatter<T> CreateApexFormatter();
    }

    internal sealed class ApexCollectionFormatter<TCollection, TElement> : ApexFormatter<TCollection>
        where TCollection : ICollection<TElement?>, new()
    {
        public override void Write<TWriter, TTracker>(ref TWriter writer, scoped in TCollection value, ref TTracker tracker)
        {
            if (value is null)
            {
                ApexPack.Buffers.ApexBufferExtensions.WriteNullCollectionHeader(ref writer);
                return;
            }
            ApexPack.Buffers.ApexBufferExtensions.WriteCollectionHeader(ref writer, value.Count);
            var element = ApexFormatterCache<TElement?>.Instance;
            foreach (var item in value)
            {
                element.Write(ref writer, in item, ref tracker);
            }
        }

        public override TCollection Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        {
            int count = ApexPack.Buffers.ApexBufferExtensions.ReadCollectionHeader(ref reader, ref guard);
            if (count < 0) return default!;
            var collection = new TCollection();
            var element = ApexFormatterCache<TElement?>.Instance;
            for (int i = 0; i < count; i++)
            {
                collection.Add(element.Read(ref reader, ref tracker, ref guard));
            }
            return collection;
        }
    }
}

namespace MemoryPack.Formatters
{
    /// <summary>
    /// 実行時 Union(MemoryPack と同じ使い方)。中身は ApexPack の <see cref="ApexDynamicUnionFormatter{T}"/>。
    /// </summary>
    public sealed class DynamicUnionFormatter<T> : MemoryPackFormatter<T>, IApexFormatterSource<T>
        where T : class
    {
        private readonly ApexDynamicUnionFormatter<T> _inner;

        public DynamicUnionFormatter(params (ushort Tag, Type Type)[] memoryPackUnions)
            => _inner = new ApexDynamicUnionFormatter<T>(memoryPackUnions);

        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
        {
            var tracker = new NullReferenceTracker();
            _inner.Write(ref writer, in value!, ref tracker);
        }

        public override void Deserialize(ref MemoryPackReader reader, scoped ref T? value)
        {
            var tracker = new NullReferenceTracker();
            var guard = new TrustedSecurityGuard();
            value = _inner.Read(ref reader, ref tracker, ref guard);
        }

        ApexFormatter<T> IApexFormatterSource<T>.CreateApexFormatter() => _inner;
    }
}
