using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using ApexPack.Buffers;

namespace ApexPack;

/// <summary>
/// 登録のない型のフォーマッタを実行時に組み立てる。結果は呼び出し元の <see cref="ApexFormatterCache{T}"/> にだけ置かれ、ここでは何もキャッシュしない。
/// </summary>
/// <remarks>
/// NativeAOT では生成コードのモジュール初期化子による登録を使う。ここはその登録がない型のための経路。
/// </remarks>
public static class ApexFormatterResolver
{
    private static readonly Dictionary<Type, Type> s_oneArg = new()
    {
        [typeof(List<>)] = typeof(ListFormatter<,>),
        [typeof(Memory<>)] = typeof(MemoryFormatter<,>),
        [typeof(ReadOnlyMemory<>)] = typeof(ReadOnlyMemoryFormatter<,>),
        [typeof(ArraySegment<>)] = typeof(ArraySegmentFormatter<,>),
        [typeof(ReadOnlySequence<>)] = typeof(ReadOnlySequenceFormatter<,>),
        [typeof(LinkedList<>)] = typeof(LinkedListFormatter<,>),
        [typeof(Queue<>)] = typeof(QueueFormatter<,>),
        [typeof(Stack<>)] = typeof(StackFormatter<,>),
        [typeof(HashSet<>)] = typeof(HashSetFormatter<,>),
        [typeof(SortedSet<>)] = typeof(SortedSetFormatter<,>),
        [typeof(Collection<>)] = typeof(CollectionFormatter<,>),
        [typeof(ReadOnlyCollection<>)] = typeof(ReadOnlyCollectionFormatter<,>),
        [typeof(ObservableCollection<>)] = typeof(ObservableCollectionFormatter<,>),
        [typeof(ReadOnlyObservableCollection<>)] = typeof(ReadOnlyObservableCollectionFormatter<,>),
        [typeof(ConcurrentQueue<>)] = typeof(ConcurrentQueueFormatter<,>),
        [typeof(ConcurrentStack<>)] = typeof(ConcurrentStackFormatter<,>),
        [typeof(ConcurrentBag<>)] = typeof(ConcurrentBagFormatter<,>),
        [typeof(BlockingCollection<>)] = typeof(BlockingCollectionFormatter<,>),
        [typeof(IEnumerable<>)] = typeof(EnumerableFormatter<,>),
        [typeof(IReadOnlyCollection<>)] = typeof(ReadOnlyCollectionInterfaceFormatter<,>),
        [typeof(IReadOnlyList<>)] = typeof(ReadOnlyListInterfaceFormatter<,>),
        [typeof(ICollection<>)] = typeof(CollectionInterfaceFormatter<,>),
        [typeof(IList<>)] = typeof(ListInterfaceFormatter<,>),
        [typeof(ISet<>)] = typeof(SetInterfaceFormatter<,>),
        [typeof(IReadOnlySet<>)] = typeof(ReadOnlySetInterfaceFormatter<,>),
        [typeof(ImmutableArray<>)] = typeof(ImmutableArrayFormatter<,>),
        [typeof(ImmutableList<>)] = typeof(ImmutableListFormatter<,>),
        [typeof(ImmutableQueue<>)] = typeof(ImmutableQueueFormatter<,>),
        [typeof(ImmutableStack<>)] = typeof(ImmutableStackFormatter<,>),
        [typeof(ImmutableHashSet<>)] = typeof(ImmutableHashSetFormatter<,>),
        [typeof(System.Collections.Frozen.FrozenSet<>)] = typeof(FrozenSetFormatter<,>),
        [typeof(ImmutableSortedSet<>)] = typeof(ImmutableSortedSetFormatter<,>),
        [typeof(IImmutableList<>)] = typeof(ImmutableListInterfaceFormatter<,>),
        [typeof(IImmutableQueue<>)] = typeof(ImmutableQueueInterfaceFormatter<,>),
        [typeof(IImmutableStack<>)] = typeof(ImmutableStackInterfaceFormatter<,>),
        [typeof(IImmutableSet<>)] = typeof(ImmutableSetInterfaceFormatter<,>),
        [typeof(Lazy<>)] = typeof(LazyFormatter<,>),
    };

    private static readonly Dictionary<Type, Type> s_twoArg = new()
    {
        [typeof(Dictionary<,>)] = typeof(DictionaryFormatter<,,,>),
        [typeof(SortedDictionary<,>)] = typeof(SortedDictionaryFormatter<,,,>),
        [typeof(SortedList<,>)] = typeof(SortedListFormatter<,,,>),
        [typeof(ConcurrentDictionary<,>)] = typeof(ConcurrentDictionaryFormatter<,,,>),
        [typeof(ReadOnlyDictionary<,>)] = typeof(ReadOnlyDictionaryFormatter<,,,>),
        [typeof(IDictionary<,>)] = typeof(DictionaryInterfaceFormatter<,,,>),
        [typeof(IReadOnlyDictionary<,>)] = typeof(ReadOnlyDictionaryInterfaceFormatter<,,,>),
        [typeof(ImmutableDictionary<,>)] = typeof(ImmutableDictionaryFormatter<,,,>),
        [typeof(System.Collections.Frozen.FrozenDictionary<,>)] = typeof(FrozenDictionaryFormatter<,,,>),
        [typeof(ImmutableSortedDictionary<,>)] = typeof(ImmutableSortedDictionaryFormatter<,,,>),
        [typeof(IImmutableDictionary<,>)] = typeof(ImmutableDictionaryInterfaceFormatter<,,,>),
        [typeof(PriorityQueue<,>)] = typeof(PriorityQueueFormatter<,,,>),
        [typeof(KeyValuePair<,>)] = typeof(KeyValuePairFormatter<,,,>),
        [typeof(ILookup<,>)] = typeof(LookupFormatter<,,,>),
        [typeof(IGrouping<,>)] = typeof(GroupingFormatter<,,,>),
    };

    private static readonly Dictionary<Type, Type> s_simple = new()
    {
        [typeof(string)] = typeof(StringFormatter),
        [typeof(Uri)] = typeof(UriFormatter),
        [typeof(Version)] = typeof(VersionFormatter),
        [typeof(BigInteger)] = typeof(BigIntegerFormatter),
        [typeof(StringBuilder)] = typeof(StringBuilderFormatter),
        [typeof(Type)] = typeof(TypeFormatter),
        [typeof(CultureInfo)] = typeof(CultureInfoFormatter),
        [typeof(TimeZoneInfo)] = typeof(TimeZoneInfoFormatter),
        [typeof(BitArray)] = typeof(BitArrayFormatter),
    };

    private static readonly HashSet<Type> s_scalars = new()
    {
        typeof(decimal), typeof(DateTime), typeof(TimeSpan), typeof(DateTimeOffset), typeof(Guid),
        typeof(DateOnly), typeof(TimeOnly), typeof(Half), typeof(Int128), typeof(UInt128), typeof(Rune),
        typeof(Complex), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion), typeof(Plane),
        typeof(Matrix3x2), typeof(Matrix4x4),
    };

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "AOT では生成コードのモジュール初期化子で登録される。ここは登録のない型の非 AOT 経路。")]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "同上")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "同上")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "同上")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "同上")]
    public static ApexFormatter<T> Create<T>()
    {
        var formatterType = GetFormatterType(typeof(T), root: true);
        if (formatterType is null)
        {
            return new UnsupportedFormatter<T>();
        }

        bool preserveReferences = NeedsReferences(typeof(T), 0);
        var adapterType = typeof(FormatterAdapter<,>).MakeGenericType(typeof(T), formatterType);
        // 引数は配列で渡す(bool 1 つだと CreateInstance(Type, bool nonPublic) が選ばれ、コンストラクタに渡らない)
        return (ApexFormatter<T>)Activator.CreateInstance(adapterType, new object[] { preserveReferences })!;
    }

    /// <summary>
    /// ルートで参照追跡を始めるか。[ApexPackable] 型は生成コードの static ApexPreserveReferences
    /// (自分かメンバーに参照保持の型を含む)で決め、配列・ジェネリック型(List&lt;Node&gt; など)は要素・型引数をたどる。
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "非 AOT 経路")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "非 AOT 経路")]
    private static bool NeedsReferences(Type type, int depth)
    {
        if (depth > 8) return false;
        if (type.GetCustomAttribute<ApexPackableAttribute>(inherit: false) is { PreserveReferences: true }) return true;
        if (IsPackable(type))
        {
            return type.GetProperty("ApexPreserveReferences", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is true;
        }
        if (type.IsArray) return NeedsReferences(type.GetElementType()!, depth + 1);
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (NeedsReferences(argument, depth + 1)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 型に対応する合成フォーマッタ型を返す。<paramref name="root"/> が false(要素位置)で未知の型なら
    /// <see cref="CachedFormatter{T}"/> を返し、利用者登録のフォーマッタへ委譲する。
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "非 AOT 経路")]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "非 AOT 経路")]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "非 AOT 経路")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "非 AOT 経路")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "非 AOT 経路")]
    [UnconditionalSuppressMessage("Trimming", "IL2071", Justification = "非 AOT 経路")]
    [UnconditionalSuppressMessage("Trimming", "IL2076", Justification = "非 AOT 経路")]
    public static Type? GetFormatterType(Type type, bool root = false)
    {
        if (IsPackable(type))
        {
            return typeof(PackableFormatter<>).MakeGenericType(type);
        }

        if (s_simple.TryGetValue(type, out var simple))
        {
            return simple;
        }

        if (type.IsPrimitive || type.IsEnum || s_scalars.Contains(type))
        {
            return typeof(ScalarFormatter<>).MakeGenericType(type);
        }

        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            int rank = type.GetArrayRank();
            if (rank == 1 && type.IsSZArray)
            {
                var inner = Nullable.GetUnderlyingType(element);
                if (inner is not null)
                {
                    return typeof(NullableArrayFormatter<,>).MakeGenericType(inner, Element(inner));
                }
                return typeof(ArrayFormatter<,>).MakeGenericType(element, Element(element));
            }
            var md = rank switch
            {
                2 => typeof(Array2DFormatter<,>),
                3 => typeof(Array3DFormatter<,>),
                4 => typeof(Array4DFormatter<,>),
                _ => null
            };
            return md?.MakeGenericType(element, Element(element));
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();

            if (definition == typeof(Nullable<>))
            {
                return typeof(NullableFormatter<,>).MakeGenericType(args[0], Element(args[0]));
            }
            if (definition == typeof(List<>) && Nullable.GetUnderlyingType(args[0]) is { } nullableInner)
            {
                return typeof(NullableListFormatter<,>).MakeGenericType(nullableInner, Element(nullableInner));
            }
            if (s_oneArg.TryGetValue(definition, out var one))
            {
                return one.MakeGenericType(args[0], Element(args[0]));
            }
            if (s_twoArg.TryGetValue(definition, out var two))
            {
                return two.MakeGenericType(args[0], args[1], Element(args[0]), Element(args[1]));
            }
            var tuple = TupleFormatterDefinition(definition);
            if (tuple is not null)
            {
                var all = new Type[args.Length * 2];
                for (int i = 0; i < args.Length; i++)
                {
                    all[i] = args[i];
                    all[args.Length + i] = Element(args[i]);
                }
                return tuple.MakeGenericType(all);
            }
        }

        // 参照を含まない未知の構造体はそのままのメモリ表現で扱う(エンディアン非可搬)
        if (type.IsValueType && !ContainsReferences(type))
        {
            return typeof(ScalarFormatter<>).MakeGenericType(type);
        }

        return root ? null : typeof(CachedFormatter<>).MakeGenericType(type);
    }

    private static Type Element(Type type) => GetFormatterType(type, root: false)!;

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "非 AOT 経路")]
    private static bool IsPackable(Type type)
    {
        foreach (var i in type.GetInterfaces())
        {
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISerializePackable<>) && i.GetGenericArguments()[0] == type)
            {
                return true;
            }
        }
        return false;
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "非 AOT 経路")]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "非 AOT 経路")]
    private static bool ContainsReferences(Type type)
    {
        var method = typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!.MakeGenericMethod(type);
        return (bool)method.Invoke(null, null)!;
    }

    private static Type? TupleFormatterDefinition(Type definition)
    {
        if (definition == typeof(ValueTuple<>)) return typeof(ValueTupleFormatter<,>);
        if (definition == typeof(ValueTuple<,>)) return typeof(ValueTupleFormatter<,,,>);
        if (definition == typeof(ValueTuple<,,>)) return typeof(ValueTupleFormatter<,,,,,>);
        if (definition == typeof(ValueTuple<,,,>)) return typeof(ValueTupleFormatter<,,,,,,,>);
        if (definition == typeof(ValueTuple<,,,,>)) return typeof(ValueTupleFormatter<,,,,,,,,,>);
        if (definition == typeof(ValueTuple<,,,,,>)) return typeof(ValueTupleFormatter<,,,,,,,,,,,>);
        if (definition == typeof(ValueTuple<,,,,,,>)) return typeof(ValueTupleFormatter<,,,,,,,,,,,,,>);
        if (definition == typeof(ValueTuple<,,,,,,,>)) return typeof(ValueTupleFormatter<,,,,,,,,,,,,,,,>);
        if (definition == typeof(Tuple<>)) return typeof(TupleFormatter<,>);
        if (definition == typeof(Tuple<,>)) return typeof(TupleFormatter<,,,>);
        if (definition == typeof(Tuple<,,>)) return typeof(TupleFormatter<,,,,,>);
        if (definition == typeof(Tuple<,,,>)) return typeof(TupleFormatter<,,,,,,,>);
        if (definition == typeof(Tuple<,,,,>)) return typeof(TupleFormatter<,,,,,,,,,>);
        if (definition == typeof(Tuple<,,,,,>)) return typeof(TupleFormatter<,,,,,,,,,,,>);
        if (definition == typeof(Tuple<,,,,,,>)) return typeof(TupleFormatter<,,,,,,,,,,,,,>);
        if (definition == typeof(Tuple<,,,,,,,>)) return typeof(TupleFormatter<,,,,,,,,,,,,,,,>);
        return null;
    }
}

/// <summary>対応していない型。使った時点で例外を投げる。</summary>
internal sealed class UnsupportedFormatter<T> : ApexFormatter<T>
{
    public override void Write<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        => ApexThrowHelper.ThrowNotSupported(typeof(T));

    public override T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
    {
        ApexThrowHelper.ThrowNotSupported(typeof(T));
        return default!;
    }
}
