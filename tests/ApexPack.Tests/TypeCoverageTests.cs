using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using ApexPack;
using MemoryPack;
using Xunit;

namespace ApexPack.Tests;

/// <summary>
/// MemoryPack が扱う型を ApexPack でも往復できること。
/// ルート(制約なし API)とメンバー(生成コード)の両経路を確かめ、MemoryPack 自身の往復結果とも突き合わせる。
/// </summary>
public class TypeCoverageTests
{
    private static T Apex<T>(T value) => ApexPackSerializer.Deserialize<T>(ApexPackSerializer.Serialize(value));

    private static void Same<T>(T value, Func<T, T, bool>? equals = null)
    {
        equals ??= (a, b) => EqualityComparer<T>.Default.Equals(a, b);
        var restored = Apex(value);
        Assert.True(equals(value, restored), $"{typeof(T)}: ApexPack roundtrip mismatch");
    }

    private static void Seq<T>(IEnumerable<T>? value, Func<IEnumerable<T>?, IEnumerable<T>?>? normalize = null)
    {
        normalize ??= x => x;
        var restored = Apex(value);
        if (value is null) { Assert.Null(restored); return; }
        Assert.Equal(normalize(value)!.ToArray(), normalize(restored)!.ToArray());
    }

    [Fact]
    public void Scalars()
    {
        Same(true); Same((byte)200); Same((sbyte)-100); Same('漢'); Same((short)-30000); Same((ushort)60000);
        Same(-123456789); Same(4000000000u); Same(long.MinValue); Same(ulong.MaxValue); Same(3.5f); Same(double.NaN);
        Same(79228162514264337593543950335m); Same(Guid.NewGuid()); Same(new DateTime(2026, 11, 15, 1, 2, 3, DateTimeKind.Local));
        Same(TimeSpan.FromTicks(-1)); Same(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(9)));
        Same(new DateOnly(2026, 11, 15)); Same(new TimeOnly(23, 59, 58)); Same((Half)1.5); Same(Int128.MaxValue); Same(UInt128.MaxValue);
        Same(new Rune(0x1F600)); Same(new Complex(1, -2)); Same(new Vector2(1, 2)); Same(new Vector3(1, 2, 3)); Same(new Vector4(1, 2, 3, 4));
        Same(Quaternion.Identity); Same(new Plane(1, 2, 3, 4)); Same(Matrix3x2.CreateRotation(1)); Same(Matrix4x4.CreateScale(2));
        Same((nint)(-5)); Same((nuint)5); Same(TestRank.Gold); Same<int?>(5); Same<int?>(null);
    }

    [Fact]
    public void ReferenceScalars()
    {
        Same<string?>("text"); Same<string?>(null); Same<string?>("");
        Same(new Uri("https://example.com/a?b=c")); Same(new Uri("relative/path", UriKind.Relative));
        Same(new Version(1, 2)); Same(new Version(1, 2, 3, 4)); Same<Version?>(null);
        Same(BigInteger.Parse("-123456789012345678901234567890"));
        Same(new StringBuilder("sb テキスト"), (a, b) => a.ToString() == b.ToString());
        Same(CultureInfo.GetCultureInfo("ja-JP"));
        Same(TimeZoneInfo.Utc, (a, b) => a.Id == b.Id);
        Same(typeof(List<int>));
        var bits = new BitArray(new[] { true, false, true, true, false, false, false, true, true });
        Same(bits, (a, b) => a.Length == b.Length && a.Cast<bool>().SequenceEqual(b.Cast<bool>()));
    }

    [Fact]
    public void Wrappers()
    {
        Same(new KeyValuePair<int, string>(1, "a"));
        Same((1, "two", 3.0, 'c', 5L, (byte)6, 7f, "eight", 9));
        Same(Tuple.Create(1, "a"));
        Same(Tuple.Create(1, 2, 3, 4, 5, 6, 7, 8));
        Same(new Lazy<int>(() => 5), (a, b) => a.Value == b.Value);
    }

    [Fact]
    public void Arrays()
    {
        Seq(new[] { 1, 2, 3 }); Seq<int>(null); Seq(Array.Empty<string>()); Seq(new[] { "a", null, "c" });
        Seq(new[] { new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-3)) });
        Seq(new int?[] { 1, null, 3 });
        Same(new int[,] { { 1, 2 }, { 3, 4 } }, (a, b) => a.Cast<int>().SequenceEqual(b.Cast<int>()) && a.GetLength(1) == b.GetLength(1));
        Same(new string[2, 2, 2], (a, b) => a.Length == b.Length);
        Same(new byte[1, 2, 3, 4], (a, b) => a.Length == b.Length && a.GetLength(3) == 4);
        Same(new Memory<int>(new[] { 1, 2 }), (a, b) => a.Span.SequenceEqual(b.Span));
        Same(new ReadOnlyMemory<byte>(new byte[] { 1, 2 }), (a, b) => a.Span.SequenceEqual(b.Span));
        Same(new ArraySegment<int>(new[] { 0, 1, 2, 3 }, 1, 2), (a, b) => a.SequenceEqual(b));
        Same(new ReadOnlySequence<int>(new[] { 5, 6 }), (a, b) => a.ToArray().SequenceEqual(b.ToArray()));
    }

    [Fact]
    public void Collections()
    {
        Seq(new List<int> { 1, 2 }); Seq(new List<string?> { "a", null }); Seq(new List<int?> { 1, null });
        Seq(new LinkedList<int>(new[] { 1, 2 })); Seq(new Queue<int>(new[] { 1, 2 })); Seq(new Stack<int>(new[] { 1, 2, 3 }));
        // Stack / Queue は借りた配列を経由して一括で読み書きする: 空、参照型、多い件数、内部の環状バッファが折り返した Queue
        Seq(new Stack<int>()); Seq(new Queue<string?>()); Seq(new Stack<string?>(new[] { "a", null, "c" }));
        Seq(new Stack<int>(Enumerable.Range(0, 1000))); Seq(new Queue<string>(Enumerable.Range(0, 300).Select(i => i.ToString())));
        var wrapped = new Queue<int>(8);
        for (int i = 0; i < 6; i++) wrapped.Enqueue(i);
        for (int i = 0; i < 4; i++) wrapped.Dequeue();
        for (int i = 6; i < 11; i++) wrapped.Enqueue(i);
        Seq(wrapped);
        Seq(new HashSet<int> { 3, 1 }, x => x?.OrderBy(v => v)); Seq(new SortedSet<int> { 3, 1 });
        Seq(new Collection<int> { 1 }); Seq(new ReadOnlyCollection<int>(new[] { 1, 2 }));
        Seq(new ObservableCollection<int> { 1 }); Seq(new ReadOnlyObservableCollection<int>(new ObservableCollection<int> { 1 }));
        Seq(new ConcurrentQueue<int>(new[] { 1, 2 })); Seq(new ConcurrentStack<int>(new[] { 1, 2, 3 }));
        Seq(new ConcurrentBag<int>(new[] { 1, 2 }), x => x?.OrderBy(v => v)); Seq(new BlockingCollection<int> { 1, 2 });
        Seq<int>((IEnumerable<int>)new[] { 1 }); Seq((IReadOnlyCollection<int>)new[] { 1 }); Seq((IReadOnlyList<int>)new[] { 1 });
        Seq((ICollection<int>)new List<int> { 1 }); Seq((IList<int>)new List<int> { 1 });
        Seq((ISet<int>)new HashSet<int> { 1 }); Seq((IReadOnlySet<int>)new HashSet<int> { 1 });
        var pq = new PriorityQueue<string, int>();
        pq.Enqueue("b", 2); pq.Enqueue("a", 1);
        var rpq = Apex(pq);
        Assert.Equal("a", rpq!.Dequeue());
        Assert.Equal("b", rpq.Dequeue());
    }

    [Fact]
    public void Dictionaries()
    {
        var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        Assert.Equal(d, Apex(d));
        Assert.Equal(d, Apex(new SortedDictionary<string, int>(d)));
        Assert.Equal(d, Apex(new SortedList<string, int>(d)));
        Assert.Equal(d.OrderBy(x => x.Key), Apex(new ConcurrentDictionary<string, int>(d))!.OrderBy(x => x.Key));
        Assert.Equal(d, Apex(new ReadOnlyDictionary<string, int>(d)));
        Assert.Equal(d, Apex((IDictionary<string, int>)d));
        Assert.Equal(d, Apex((IReadOnlyDictionary<string, int>)d));
        var lookup = new[] { (1, "a"), (1, "b"), (2, "c") }.ToLookup(x => x.Item1, x => x.Item2);
        var rl = Apex(lookup);
        Assert.Equal(new[] { "a", "b" }, rl![1]);
        Assert.Equal(new[] { "c" }, rl[2]);
        var grouping = lookup.First();
        var rg = Apex(grouping);
        Assert.Equal(1, rg!.Key);
        Assert.Equal(new[] { "a", "b" }, rg);
    }

    [Fact]
    public void Immutables()
    {
        Seq(ImmutableArray.Create(1, 2)); Seq(ImmutableList.Create(1, 2)); Seq(ImmutableQueue.Create(1, 2)); Seq(ImmutableStack.Create(1, 2));
        Seq(ImmutableHashSet.Create(1), x => x); Seq(ImmutableSortedSet.Create(2, 1));
        Seq((IImmutableList<int>)ImmutableList.Create(1)); Seq((IImmutableQueue<int>)ImmutableQueue.Create(1));
        Seq((IImmutableStack<int>)ImmutableStack.Create(1, 2)); Seq((IImmutableSet<int>)ImmutableHashSet.Create(1));
        var d = ImmutableDictionary.Create<string, int>().Add("a", 1);
        Assert.Equal(d, Apex(d));
        Assert.Equal(d, Apex(ImmutableSortedDictionary.CreateRange(d)));
        Assert.Equal(d, Apex((IImmutableDictionary<string, int>)d));
        Assert.True(Apex(default(ImmutableArray<int>)).IsDefault);
    }

    [Fact]
    public void AsMembers_MatchMemoryPackValues()
    {
        var value = new BclMembers
        {
            Offset = new DateTimeOffset(2026, 11, 15, 9, 30, 0, TimeSpan.FromHours(9)),
            Date = new DateOnly(2026, 11, 15),
            Big = BigInteger.Pow(2, 100),
            Link = new Uri("https://example.com"),
            Ver = new Version(9, 8, 7),
            Set = new HashSet<string> { "x" },
            Queue = new Queue<int>(new[] { 1, 2 }),
            Sorted = new SortedDictionary<int, string> { [2] = "b", [1] = "a" },
            Immutable = ImmutableArray.Create(1.5, 2.5),
            Pair = new KeyValuePair<string, int>("k", 1),
            Values = (1, "v"),
            Half = (Half)0.5,
            Huge = Int128.MinValue,
        };

        var apex = ApexPackSerializer.Deserialize<BclMembers>(ApexPackSerializer.Serialize(value));
        var mp = MemoryPackSerializer.Deserialize<BclMembers>(MemoryPackSerializer.Serialize(value))!;
        // MemoryPack 1.21.4 は BigInteger メンバーを 0 として復元する(観測済み)ため、BigInteger は ApexPack だけで確かめる
        Assert.Equal(value.Big, apex.Big);
        foreach (var r in new[] { apex, mp })
        {
            Assert.Equal(value.Offset, r.Offset);
            Assert.Equal(value.Date, r.Date);
            Assert.Equal(value.Link, r.Link);
            Assert.Equal(value.Ver, r.Ver);
            Assert.Equal(value.Set, r.Set);
            Assert.Equal(value.Queue, r.Queue);
            Assert.Equal(value.Sorted, r.Sorted);
            Assert.Equal(value.Immutable.ToArray(), r.Immutable.ToArray());
            Assert.Equal(value.Pair, r.Pair);
            Assert.Equal(value.Values, r.Values);
            Assert.Equal(value.Half, r.Half);
            Assert.Equal(value.Huge, r.Huge);
        }
    }
}

[ApexPackable]
[MemoryPackable]
public partial class BclMembers
{
    public DateTimeOffset Offset { get; set; }
    public DateOnly Date { get; set; }
    public BigInteger Big { get; set; }
    public Uri? Link { get; set; }
    public Version? Ver { get; set; }
    public HashSet<string>? Set { get; set; }
    public Queue<int>? Queue { get; set; }
    public SortedDictionary<int, string>? Sorted { get; set; }
    public ImmutableArray<double> Immutable { get; set; }
    public KeyValuePair<string, int> Pair { get; set; }
    public (int, string) Values { get; set; }
    public Half Half { get; set; }
    public Int128 Huge { get; set; }
}
