"""コレクション系フォーマッタ (src/ApexPack.Core/Formatters/CollectionFormatters.g.cs) を生成する。

使い方: python tools/gen_collections.py src/ApexPack.Core/Formatters/CollectionFormatters.g.cs
ワイヤ形式: 件数ヘッダー CompactUInt(count + 1)、0 = null。続けて要素。辞書は key, value の順。
"""
import sys

W = """        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct"""
R = """        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct"""

NULL_W = "if (value is null) { writer.WriteNullCollectionHeader(); return; }"
SEQ = "ApexSequence"


def span_w(expr):
    return f"{SEQ}.WriteSpan<TWriter, TTracker, T, TF>(ref writer, {expr}, ref tracker);"


ENUM_W = f"{SEQ}.WriteEnumerable<TWriter, TTracker, T, TF>(ref writer, value, ref tracker);"
COLL_W = [NULL_W, f"{SEQ}.WriteCollection<TWriter, TTracker, T, TF>(ref writer, value, ref tracker);"]
READ_ARR = f"{SEQ}.ReadArray<TReader, TTracker, TGuard, T, TF>(ref reader, ref tracker, ref guard)"
READ_LIST = f"{SEQ}.ReadList<TReader, TTracker, TGuard, T, TF>(ref reader, ref tracker, ref guard)"
COUNT_R = f"int count = {SEQ}.ReadCount<TReader, TGuard, T, TF>(ref reader, ref guard);"
ITEM_R = "TF.Read(ref reader, ref tracker, ref guard)"


def foreach_w(enumerable, count_expr):
    return [
        NULL_W,
        f"writer.WriteCollectionHeader({count_expr});",
        f"foreach (var element in {enumerable}) {{ T item = element; TF.Write(ref writer, in item, ref tracker); }}",
    ]


def copy_w(count_expr):
    # 借りた配列へ CopyTo で一度に写してから WriteSpan で書く(固定長の要素は一括コピー)。並びは列挙の順(Stack は上から)
    return [
        NULL_W,
        f"int count = {count_expr};",
        "T[] rented = global::System.Buffers.ArrayPool<T>.Shared.Rent(count);",
        "try",
        "{",
        "    value.CopyTo(rented, 0);",
        f"    {span_w('new global::System.ReadOnlySpan<T>(rented, 0, count)')}",
        "}",
        "finally",
        "{",
        "    global::System.Buffers.ArrayPool<T>.Shared.Return(rented, global::System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>());",
        "}",
    ]


def pooled_r(build, reverse=False):
    # 借りた配列へ要素を読み、ArraySegment(ICollection<T>)から一括で作る。中間の配列を割り当てない
    lines = [
        COUNT_R,
        "if (count < 0) return null;",
        "T[] rented = global::System.Buffers.ArrayPool<T>.Shared.Rent(count);",
        "try",
        "{",
        f"    {SEQ}.ReadElements<TReader, TTracker, TGuard, T, TF>(ref reader, new global::System.Span<T>(rented, 0, count), ref tracker, ref guard);",
    ]
    if reverse:
        lines.append("    global::System.Array.Reverse(rented, 0, count);")
    lines += [
        f"    return {build.format(items='new global::System.ArraySegment<T>(rented, 0, count)')};",
        "}",
        "finally",
        "{",
        "    global::System.Buffers.ArrayPool<T>.Shared.Return(rented, global::System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>());",
        "}",
    ]
    return lines


def add_r(create, add, result="collection"):
    return [
        COUNT_R,
        "if (count < 0) return null;",
        f"var collection = {create};",
        f"for (int i = 0; i < count; i++) {{ {add.format(item=ITEM_R)}; }}",
        f"return {result};",
    ]


def arr_r(expr, null="null"):
    return [f"var array = {READ_ARR};", f"return array is null ? {null} : {expr};"]


E = ["T", "TF"]
EC = ["where TF : IApexFormatter<T>"]
DK = ["TKey", "TValue", "FK", "FV"]
DC = ["where TKey : notnull", "where FK : IApexFormatter<TKey>", "where FV : IApexFormatter<TValue>"]
DCN = ["where FK : IApexFormatter<TKey>", "where FV : IApexFormatter<TValue>"]

DICT_COUNT_R = "uint header = reader.ReadCompactUInt(); if (header == 0) return null; int count = DictionaryHelper.CheckCount(ref reader, ref guard, header - 1, FK.MinWireSize + FV.MinWireSize);"


def dict_w(count_expr="value.Count", source="value"):
    return [
        NULL_W,
        f"writer.WriteCollectionHeader({count_expr});",
        f"foreach (var pair in {source}) {{ TKey key = pair.Key; TValue item = pair.Value; FK.Write(ref writer, in key, ref tracker); FV.Write(ref writer, in item, ref tracker); }}",
    ]


def dict_r(create, add, result="dictionary"):
    return [
        DICT_COUNT_R,
        f"var dictionary = {create};",
        "for (int i = 0; i < count; i++)",
        "{",
        "    TKey key = FK.Read(ref reader, ref tracker, ref guard);",
        "    TValue item = FV.Read(ref reader, ref tracker, ref guard);",
        f"    {add};",
        "}",
        f"return {result};",
    ]


SPECS = [
    # name, gparams, constraints, target, doc, write, read
    ("ArrayFormatter", E, EC, "T[]?", "T[]", [NULL_W, span_w("value")], [f"return {READ_ARR};"]),
    ("ListFormatter", E, EC, "List<T>?", "List&lt;T&gt;", [NULL_W, span_w("CollectionsMarshal.AsSpan(value)")], [f"return {READ_LIST};"]),
    ("MemoryFormatter", E, EC, "Memory<T>", "Memory&lt;T&gt;(null は空として扱う)", [span_w("value.Span")], arr_r("array", "Memory<T>.Empty")),
    ("ReadOnlyMemoryFormatter", E, EC, "ReadOnlyMemory<T>", "ReadOnlyMemory&lt;T&gt;", [span_w("value.Span")], arr_r("array", "ReadOnlyMemory<T>.Empty")),
    ("ArraySegmentFormatter", E, EC, "ArraySegment<T>", "ArraySegment&lt;T&gt;(Array が null なら null)",
     ["if (value.Array is null) { writer.WriteNullCollectionHeader(); return; }", span_w("value.AsSpan()")],
     arr_r("new ArraySegment<T>(array)", "default")),
    ("ReadOnlySequenceFormatter", E, EC, "ReadOnlySequence<T>", "ReadOnlySequence&lt;T&gt;",
     ["if (value.IsSingleSegment) { " + span_w("value.FirstSpan") + " return; }", span_w("value.ToArray()")],
     arr_r("new ReadOnlySequence<T>(array)", "ReadOnlySequence<T>.Empty")),
    ("LinkedListFormatter", E, EC, "LinkedList<T>?", "LinkedList&lt;T&gt;", foreach_w("value", "value.Count"), add_r("new LinkedList<T>()", "collection.AddLast({item})")),
    ("QueueFormatter", E, EC, "Queue<T>?", "Queue&lt;T&gt;", copy_w("value.Count"),
     pooled_r("new Queue<T>({items})")),
    ("StackFormatter", E, EC, "Stack<T>?", "Stack&lt;T&gt;(上から順に書き、逆順に積み直す)", copy_w("value.Count"),
     pooled_r("new Stack<T>({items})", reverse=True)),
    ("HashSetFormatter", E, EC, "HashSet<T>?", "HashSet&lt;T&gt;", foreach_w("value", "value.Count"), add_r("new HashSet<T>(count)", "collection.Add({item})")),
    ("SortedSetFormatter", E, EC, "SortedSet<T>?", "SortedSet&lt;T&gt;", foreach_w("value", "value.Count"), add_r("new SortedSet<T>()", "collection.Add({item})")),
    ("CollectionFormatter", E, EC, "Collection<T>?", "Collection&lt;T&gt;", COLL_W, [f"var list = {READ_LIST};", "return list is null ? null : new Collection<T>(list);"]),
    ("ReadOnlyCollectionFormatter", E, EC, "ReadOnlyCollection<T>?", "ReadOnlyCollection&lt;T&gt;", COLL_W, arr_r("new ReadOnlyCollection<T>(array)")),
    ("ObservableCollectionFormatter", E, EC, "ObservableCollection<T>?", "ObservableCollection&lt;T&gt;", COLL_W, [f"var list = {READ_LIST};", "return list is null ? null : new ObservableCollection<T>(list);"]),
    ("ReadOnlyObservableCollectionFormatter", E, EC, "ReadOnlyObservableCollection<T>?", "ReadOnlyObservableCollection&lt;T&gt;", COLL_W,
     [f"var list = {READ_LIST};", "return list is null ? null : new ReadOnlyObservableCollection<T>(new ObservableCollection<T>(list));"]),
    ("ConcurrentQueueFormatter", E, EC, "ConcurrentQueue<T>?", "ConcurrentQueue&lt;T&gt;(書き込み時点のスナップショット)", [NULL_W, span_w("value.ToArray()")], arr_r("new ConcurrentQueue<T>(array)")),
    ("ConcurrentStackFormatter", E, EC, "ConcurrentStack<T>?", "ConcurrentStack&lt;T&gt;(先頭から順に書き、逆順に積み直す)", [NULL_W, span_w("value.ToArray()")],
     [f"var array = {READ_ARR};", "if (array is null) return null;", "Array.Reverse(array);", "return new ConcurrentStack<T>(array);"]),
    ("ConcurrentBagFormatter", E, EC, "ConcurrentBag<T>?", "ConcurrentBag&lt;T&gt;", [NULL_W, span_w("value.ToArray()")], arr_r("new ConcurrentBag<T>(array)")),
    ("BlockingCollectionFormatter", E, EC, "BlockingCollection<T>?", "BlockingCollection&lt;T&gt;", [NULL_W, span_w("value.ToArray()")],
     [f"var array = {READ_ARR};", "if (array is null) return null;", "var collection = new BlockingCollection<T>();", "foreach (var item in array) collection.Add(item);", "return collection;"]),
    ("EnumerableFormatter", E, EC, "IEnumerable<T>?", "IEnumerable&lt;T&gt;(配列として復元)", [ENUM_W], [f"return {READ_ARR};"]),
    ("ReadOnlyCollectionInterfaceFormatter", E, EC, "IReadOnlyCollection<T>?", "IReadOnlyCollection&lt;T&gt;(配列として復元)", [ENUM_W], [f"return {READ_ARR};"]),
    ("ReadOnlyListInterfaceFormatter", E, EC, "IReadOnlyList<T>?", "IReadOnlyList&lt;T&gt;(配列として復元)", [ENUM_W], [f"return {READ_ARR};"]),
    ("CollectionInterfaceFormatter", E, EC, "ICollection<T>?", "ICollection&lt;T&gt;(List として復元)", [ENUM_W], [f"return {READ_LIST};"]),
    ("ListInterfaceFormatter", E, EC, "IList<T>?", "IList&lt;T&gt;(List として復元)", [ENUM_W], [f"return {READ_LIST};"]),
    ("SetInterfaceFormatter", E, EC, "ISet<T>?", "ISet&lt;T&gt;(HashSet として復元)", [ENUM_W], add_r("new HashSet<T>(count)", "collection.Add({item})")),
    ("ReadOnlySetInterfaceFormatter", E, EC, "IReadOnlySet<T>?", "IReadOnlySet&lt;T&gt;(HashSet として復元)", [ENUM_W], add_r("new HashSet<T>(count)", "collection.Add({item})")),
    # ---- immutable ----
    ("ImmutableArrayFormatter", E, EC, "ImmutableArray<T>", "ImmutableArray&lt;T&gt;(default は null)",
     ["if (value.IsDefault) { writer.WriteNullCollectionHeader(); return; }", span_w("value.AsSpan()")],
     arr_r("ImmutableCollectionsMarshal.AsImmutableArray(array)", "default")),
    ("ImmutableListFormatter", E, EC, "ImmutableList<T>?", "ImmutableList&lt;T&gt;", foreach_w("value", "value.Count"), arr_r("ImmutableList.Create<T>(array)")),
    ("ImmutableQueueFormatter", E, EC, "ImmutableQueue<T>?", "ImmutableQueue&lt;T&gt;", [ENUM_W], arr_r("ImmutableQueue.Create<T>(array)")),
    ("ImmutableStackFormatter", E, EC, "ImmutableStack<T>?", "ImmutableStack&lt;T&gt;(先頭から順に書き、逆順に積み直す)", [ENUM_W],
     [f"var array = {READ_ARR};", "if (array is null) return null;", "Array.Reverse(array);", "return ImmutableStack.Create<T>(array);"]),
    ("ImmutableHashSetFormatter", E, EC, "ImmutableHashSet<T>?", "ImmutableHashSet&lt;T&gt;", foreach_w("value", "value.Count"), arr_r("ImmutableHashSet.Create<T>(array)")),
    ("ImmutableSortedSetFormatter", E, EC, "ImmutableSortedSet<T>?", "ImmutableSortedSet&lt;T&gt;", foreach_w("value", "value.Count"), arr_r("ImmutableSortedSet.Create<T>(array)")),
    ("ImmutableListInterfaceFormatter", E, EC, "IImmutableList<T>?", "IImmutableList&lt;T&gt;", [ENUM_W], arr_r("ImmutableList.Create<T>(array)")),
    ("ImmutableQueueInterfaceFormatter", E, EC, "IImmutableQueue<T>?", "IImmutableQueue&lt;T&gt;", [ENUM_W], arr_r("ImmutableQueue.Create<T>(array)")),
    ("ImmutableStackInterfaceFormatter", E, EC, "IImmutableStack<T>?", "IImmutableStack&lt;T&gt;", [ENUM_W],
     [f"var array = {READ_ARR};", "if (array is null) return null;", "Array.Reverse(array);", "return ImmutableStack.Create<T>(array);"]),
    ("ImmutableSetInterfaceFormatter", E, EC, "IImmutableSet<T>?", "IImmutableSet&lt;T&gt;(ImmutableHashSet として復元)", [ENUM_W], arr_r("ImmutableHashSet.Create<T>(array)")),
    # ---- frozen ----
    ("FrozenSetFormatter", E, EC, "FrozenSet<T>?", "FrozenSet&lt;T&gt;", [NULL_W, span_w("value.Items.AsSpan()")], arr_r("FrozenSet.ToFrozenSet<T>(array)")),
    # ---- dictionaries ----
    ("DictionaryFormatter", DK, DC, "Dictionary<TKey, TValue>?", "Dictionary&lt;TKey, TValue&gt;", dict_w(), dict_r("new Dictionary<TKey, TValue>(count)", "dictionary.Add(key, item)")),
    ("SortedDictionaryFormatter", DK, DC, "SortedDictionary<TKey, TValue>?", "SortedDictionary&lt;TKey, TValue&gt;", dict_w(), dict_r("new SortedDictionary<TKey, TValue>()", "dictionary.Add(key, item)")),
    ("SortedListFormatter", DK, DC, "SortedList<TKey, TValue>?", "SortedList&lt;TKey, TValue&gt;", dict_w(), dict_r("new SortedList<TKey, TValue>(count)", "dictionary.Add(key, item)")),
    ("ConcurrentDictionaryFormatter", DK, DC, "ConcurrentDictionary<TKey, TValue>?", "ConcurrentDictionary&lt;TKey, TValue&gt;(書き込み時点のスナップショット)",
     [NULL_W, "var snapshot = value.ToArray();", "writer.WriteCollectionHeader(snapshot.Length);",
      "foreach (var pair in snapshot) { TKey key = pair.Key; TValue item = pair.Value; FK.Write(ref writer, in key, ref tracker); FV.Write(ref writer, in item, ref tracker); }"],
     dict_r("new ConcurrentDictionary<TKey, TValue>(Environment.ProcessorCount, count)", "dictionary.TryAdd(key, item)")),
    ("ReadOnlyDictionaryFormatter", DK, DC, "ReadOnlyDictionary<TKey, TValue>?", "ReadOnlyDictionary&lt;TKey, TValue&gt;", dict_w(),
     dict_r("new Dictionary<TKey, TValue>(count)", "dictionary.Add(key, item)", "new ReadOnlyDictionary<TKey, TValue>(dictionary)")),
    ("DictionaryInterfaceFormatter", DK, DC, "IDictionary<TKey, TValue>?", "IDictionary&lt;TKey, TValue&gt;(Dictionary として復元)", dict_w(), dict_r("new Dictionary<TKey, TValue>(count)", "dictionary.Add(key, item)")),
    ("ReadOnlyDictionaryInterfaceFormatter", DK, DC, "IReadOnlyDictionary<TKey, TValue>?", "IReadOnlyDictionary&lt;TKey, TValue&gt;(Dictionary として復元)", dict_w(), dict_r("new Dictionary<TKey, TValue>(count)", "dictionary.Add(key, item)")),
    ("ImmutableDictionaryFormatter", DK, DC, "ImmutableDictionary<TKey, TValue>?", "ImmutableDictionary&lt;TKey, TValue&gt;", dict_w(),
     dict_r("ImmutableDictionary.CreateBuilder<TKey, TValue>()", "dictionary.Add(key, item)", "dictionary.ToImmutable()")),
    ("ImmutableSortedDictionaryFormatter", DK, DC, "ImmutableSortedDictionary<TKey, TValue>?", "ImmutableSortedDictionary&lt;TKey, TValue&gt;", dict_w(),
     dict_r("ImmutableSortedDictionary.CreateBuilder<TKey, TValue>()", "dictionary.Add(key, item)", "dictionary.ToImmutable()")),
    ("ImmutableDictionaryInterfaceFormatter", DK, DC, "IImmutableDictionary<TKey, TValue>?", "IImmutableDictionary&lt;TKey, TValue&gt;(ImmutableDictionary として復元)", dict_w(),
     dict_r("ImmutableDictionary.CreateBuilder<TKey, TValue>()", "dictionary.Add(key, item)", "dictionary.ToImmutable()")),
    ("FrozenDictionaryFormatter", DK, DC, "FrozenDictionary<TKey, TValue>?", "FrozenDictionary&lt;TKey, TValue&gt;", dict_w(),
     dict_r("new Dictionary<TKey, TValue>(count)", "dictionary.Add(key, item)", "FrozenDictionary.ToFrozenDictionary(dictionary)")),
    ("PriorityQueueFormatter", DK, DCN, "PriorityQueue<TKey, TValue>?", "PriorityQueue&lt;TElement, TPriority&gt;(要素と優先度の組。順序は保存しない)",
     [NULL_W, "writer.WriteCollectionHeader(value.UnorderedItems.Count);",
      "foreach (var (element, priority) in value.UnorderedItems) { TKey key = element; TValue item = priority; FK.Write(ref writer, in key, ref tracker); FV.Write(ref writer, in item, ref tracker); }"],
     dict_r("new PriorityQueue<TKey, TValue>(count)", "dictionary.Enqueue(key, item)")),
]


EXACT = {
    "ArrayFormatter": "value is null ? 1 : TF.FixedSize >= 0 ? ApexWire.CompactSize((uint)value.Length + 1) + value.Length * TF.FixedSize : -1",
    "ListFormatter": "value is null ? 1 : TF.FixedSize >= 0 ? ApexWire.CompactSize((uint)value.Count + 1) + value.Count * TF.FixedSize : -1",
}


def emit(spec, out):
    name, gparams, cons, target, doc, write, read = spec
    out.append(f"/// <summary>{doc}。</summary>")
    out.append(f"public readonly struct {name}<{', '.join(gparams)}> : IApexFormatter<{target}>")
    for c in cons:
        out.append(f"    {c}")
    out.append("{")
    out.append(f"    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in {target} value, ref TTracker tracker)")
    out.append(W)
    out.append("    {")
    for line in write:
        out.append("        " + line)
    out.append("    }")
    out.append("")
    out.append(f"    public static {target} Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)")
    out.append(R)
    out.append("    {")
    for line in read:
        out.append("        " + line)
    out.append("    }")
    if name == "ArrayFormatter":
        out.append("")
        out.append("    /// <summary>要素のメモリ表現(LE)がワイヤ形式と一致するなら要素のバイト数。</summary>")
        out.append("    public static int RawElementSize => TF.FixedSize > 0 && TF.IsRawLayout ? TF.FixedSize : -1;")
    if name in EXACT:
        out.append("")
        out.append("    /// <summary>要素が固定長なら、件数ヘッダー + 件数 × 固定長。</summary>")
        out.append(f"    public static int ExactSize(scoped in {target} value)")
        out.append(f"        => {EXACT[name]};")
    out.append("}")
    out.append("")


def main(path):
    out = [
        "// <auto-generated> tools/gen_collections.py で生成。手で編集しない </auto-generated>",
        "#nullable enable",
        "using System;",
        "using System.Buffers;",
        "using System.Collections.Concurrent;",
        "using System.Collections.Frozen;",
        "using System.Collections.Generic;",
        "using System.Collections.Immutable;",
        "using System.Collections.ObjectModel;",
        "using System.Runtime.InteropServices;",
        "using ApexPack.Buffers;",
        "",
        "namespace ApexPack;",
        "",
    ]
    for spec in SPECS:
        emit(spec, out)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out))


if __name__ == "__main__":
    main(sys.argv[1])
