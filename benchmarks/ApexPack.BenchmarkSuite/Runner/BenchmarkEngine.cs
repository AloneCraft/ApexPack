using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using ApexPack.BenchmarkSuite.Models;
using ApexPack.BenchmarkSuite.Verification;
using MemoryPack;

namespace ApexPack.BenchmarkSuite.Runner;

public class BenchmarkScenarioResult
{
    public string ScenarioId { get; set; } = "";
    public string Tier { get; set; } = "";
    public string Description { get; set; } = "";

    public double ApexSerializeNs { get; set; }
    public double ApexDeserializeNs { get; set; }
    public int ApexSizeBytes { get; set; }
    public long ApexAllocBytes { get; set; }
    public long ApexDeserializeAllocBytes { get; set; }

    public double MpSerializeNs { get; set; }
    public double MpDeserializeNs { get; set; }
    public int MpSizeBytes { get; set; }
    public long MpAllocBytes { get; set; }
    public long MpDeserializeAllocBytes { get; set; }

    public double SpeedupSerialize => ApexSerializeNs > 0 ? MpSerializeNs / ApexSerializeNs : 1.0;
    public double SpeedupDeserialize => ApexDeserializeNs > 0 ? MpDeserializeNs / ApexDeserializeNs : 1.0;
    public double SizeRatioVsMp => MpSizeBytes > 0 ? (double)ApexSizeBytes / MpSizeBytes : 1.0;

    /// <summary>
    /// 総合倍率 = 速度倍率 × サイズ倍率(MemoryPack のサイズ ÷ ApexPack のサイズ)。1 より大きければ ApexPack の勝ち。
    /// 「1 バイトで 1 秒」と「2 バイトで 2 秒」なら前者を 4 倍よいと評価する。
    /// </summary>
    public double OverallSerialize => SpeedupSerialize / SizeRatioVsMp;

    /// <summary>逆直列化の総合倍率(速度倍率 × サイズ倍率)。</summary>
    public double OverallDeserialize => SpeedupDeserialize / SizeRatioVsMp;

    public bool RoundtripVerified { get; set; }

    /// <summary>
    /// 速度は serialize / deserialize の遅い方で判定する。3% 未満の差は計測ノイズと区別できないため拮抗とする。
    /// </summary>
    public string StatusBadge
    {
        get
        {
            double worst = Math.Min(SpeedupSerialize, SpeedupDeserialize);
            bool sizeOk = SizeRatioVsMp <= 1.0;
            if (!RoundtripVerified) return "❌ 検証失敗 (Mismatch)";
            if (worst >= 1.25 && sizeOk) return "🏆 圧勝 (Dominant)";
            if (worst >= 1.03 && sizeOk) return "✅ 優勢 (Win)";
            if (worst >= 0.97 && sizeOk) return "⚖️ 拮抗 (Parity)";
            return "⚠️ 弱点 (Weakness)";
        }
    }

    public string BottleneckHint { get; set; } = "None";
}

public class BenchmarkSuiteReport
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<BenchmarkScenarioResult> Scenarios { get; set; } = new();

    public double GeometricMeanSerializeSpeedup { get; set; }
    public double GeometricMeanDeserializeSpeedup { get; set; }
    public double TotalSizeRatioVsMp { get; set; }
    public bool AllVerified { get; set; }
}

/// <summary>
/// 1 シナリオ分の操作。static abstract にして計測ループを型ごとに特殊化し、デリゲート呼び出しを計測に混ぜない。
/// </summary>
public interface IScenarioOps<T>
{
    static abstract byte[] ApexSerialize(T value);
    static abstract T ApexDeserialize(byte[] bytes);
    static abstract byte[] MpSerialize(T value);
    static abstract T MpDeserialize(byte[] bytes);
}

/// <summary>
/// 両ライブラリの既定 API を呼ぶ標準操作。
/// </summary>
public readonly struct StandardOps<T> : IScenarioOps<T> where T : ISerializePackable<T>
{
    public static byte[] ApexSerialize(T value) => ApexPackSerializer.Serialize(value);
    public static T ApexDeserialize(byte[] bytes) => ApexPackSerializer.Deserialize<T>(bytes);
    public static byte[] MpSerialize(T value) => MemoryPackSerializer.Serialize(value);
    public static T MpDeserialize(byte[] bytes) => MemoryPackSerializer.Deserialize<T>(bytes)!;
}

public readonly struct Vector3ArrayOps : IScenarioOps<BenchmarkVector3[]>
{
    public static byte[] ApexSerialize(BenchmarkVector3[] value) => ApexPackSerializer.SerializeUnmanagedArray(value);
    public static BenchmarkVector3[] ApexDeserialize(byte[] bytes) => ApexPackSerializer.DeserializeUnmanagedArray<BenchmarkVector3>(bytes)!;
    public static byte[] MpSerialize(BenchmarkVector3[] value) => MemoryPackSerializer.Serialize(value);
    public static BenchmarkVector3[] MpDeserialize(byte[] bytes) => MemoryPackSerializer.Deserialize<BenchmarkVector3[]>(bytes)!;
}

public readonly struct GraphOps : IScenarioOps<BenchGraphNode>
{
    public static byte[] ApexSerialize(BenchGraphNode value) => ApexPackSerializer.SerializeWithReferences(value);
    public static BenchGraphNode ApexDeserialize(byte[] bytes) => ApexPackSerializer.DeserializeWithReferences<BenchGraphNode>(bytes);
    public static byte[] MpSerialize(BenchGraphNode value) => MemoryPackSerializer.Serialize(value);
    public static BenchGraphNode MpDeserialize(byte[] bytes) => MemoryPackSerializer.Deserialize<BenchGraphNode>(bytes)!;
}

#if NET11_0_OR_GREATER
internal readonly struct NumericsArrayOps<T> : IScenarioOps<T[]> where T : unmanaged
{
    public static byte[] ApexSerialize(T[] value) => ApexPackSerializer.Serialize(value);
    public static T[] ApexDeserialize(byte[] bytes) => ApexPackSerializer.Deserialize<T[]>(bytes);
    public static byte[] MpSerialize(T[] value) => MemoryPackSerializer.Serialize(value);
    public static T[] MpDeserialize(byte[] bytes) => MemoryPackSerializer.Deserialize<T[]>(bytes)!;
}

internal readonly struct FullJoinOps : IScenarioOps<IEnumerable<(string? Outer, string? Inner)>>
{
    public static byte[] ApexSerialize(IEnumerable<(string? Outer, string? Inner)> value) => ApexPackSerializer.Serialize(value);
    public static IEnumerable<(string? Outer, string? Inner)> ApexDeserialize(byte[] bytes)
        => ApexPackSerializer.Deserialize<IEnumerable<(string? Outer, string? Inner)>>(bytes);
    public static byte[] MpSerialize(IEnumerable<(string? Outer, string? Inner)> value) => MemoryPackSerializer.Serialize(value);
    public static IEnumerable<(string? Outer, string? Inner)> MpDeserialize(byte[] bytes)
        => MemoryPackSerializer.Deserialize<IEnumerable<(string? Outer, string? Inner)>>(bytes)!;
}
#endif

internal interface IScenario
{
    string Name { get; }
    BenchmarkScenarioResult Run();
}

internal sealed class Scenario<T, TOps> : IScenario where TOps : IScenarioOps<T>
{
    private readonly string _id;
    private readonly string _tier;
    private readonly string _description;
    private readonly Func<T> _factory;
    private readonly Func<T, T, bool> _verify;

    public Scenario(string id, string tier, string description, Func<T> factory, Func<T, T, bool>? verify = null)
    {
        _id = id;
        _tier = tier;
        _description = description;
        _factory = factory;
        _verify = verify ?? (static (expected, actual) => DeepGraphVerifier.AreEqual(expected, actual));
    }

    public string Name => $"{_tier}: {_description}";

    public BenchmarkScenarioResult Run()
    {
        T item = _factory();
        byte[] apexBytes = TOps.ApexSerialize(item);
        byte[] mpBytes = TOps.MpSerialize(item);
        bool verified = _verify(item, TOps.ApexDeserialize(apexBytes))
                        && _verify(item, TOps.MpDeserialize(mpBytes));

        // 計測関数を段階コンパイルの昇格条件(30 回の呼び出し + 起動直後の遅延)を十分超えて呼び、
        // OSR 版ではなく完全最適化版(static readonly の定数化・脱仮想化を含む)で測る。両ライブラリに同じ条件を適用する。
        var warmup = Stopwatch.StartNew();
        while (warmup.ElapsedMilliseconds < 500)
        {
            TimeApexSer(item, 4);
            TimeMpSer(item, 4);
            TimeApexDe(apexBytes, 4);
            TimeMpDe(mpBytes, 4);
        }
        Thread.Sleep(300); // 背景の再コンパイルを待つ

        // 1 計測あたり約 BatchTargetMs になるよう反復数を決める
        int iters = Calibrate(item, apexBytes, mpBytes);

        var apexSer = new double[Rounds];
        var apexDe = new double[Rounds];
        var mpSer = new double[Rounds];
        var mpDe = new double[Rounds];

        for (int r = 0; r < Rounds; r++)
        {
            // 実行順の偏り(周波数変動・キャッシュ)を打ち消すため、ラウンドごとに先後を入れ替える
            if ((r & 1) == 0)
            {
                apexSer[r] = TimeApexSer(item, iters);
                mpSer[r] = TimeMpSer(item, iters);
                apexDe[r] = TimeApexDe(apexBytes, iters);
                mpDe[r] = TimeMpDe(mpBytes, iters);
            }
            else
            {
                mpSer[r] = TimeMpSer(item, iters);
                apexSer[r] = TimeApexSer(item, iters);
                mpDe[r] = TimeMpDe(mpBytes, iters);
                apexDe[r] = TimeApexDe(apexBytes, iters);
            }
        }

        return new BenchmarkScenarioResult
        {
            ScenarioId = _id,
            Tier = _tier,
            Description = _description,
            ApexSerializeNs = Median(apexSer),
            ApexDeserializeNs = Median(apexDe),
            ApexSizeBytes = apexBytes.Length,
            ApexAllocBytes = MeasureAlloc(() => TOps.ApexSerialize(item)),
            ApexDeserializeAllocBytes = MeasureAlloc(() => TOps.ApexDeserialize(apexBytes)),
            MpSerializeNs = Median(mpSer),
            MpDeserializeNs = Median(mpDe),
            MpSizeBytes = mpBytes.Length,
            MpAllocBytes = MeasureAlloc(() => TOps.MpSerialize(item)),
            MpDeserializeAllocBytes = MeasureAlloc(() => TOps.MpDeserialize(mpBytes)),
            RoundtripVerified = verified
        };
    }

    private const int Rounds = 11;
    private const double BatchTargetMs = 15;

    private static int Calibrate(T item, byte[] apexBytes, byte[] mpBytes)
    {
        int iters = 16;
        while (true)
        {
            var sw = Stopwatch.StartNew();
            TimeApexSer(item, iters);
            TimeMpSer(item, iters);
            TimeApexDe(apexBytes, iters);
            TimeMpDe(mpBytes, iters);
            double perOpMs = sw.Elapsed.TotalMilliseconds / 4;
            if (perOpMs >= BatchTargetMs || iters >= 50_000_000)
            {
                return Math.Max(1, (int)(iters * (BatchTargetMs / Math.Max(perOpMs, 1e-6))));
            }
            iters *= 4;
        }
    }

    private static object? s_sink;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double TimeApexSer(T item, int iters)
    {
        byte[]? last = null;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iters; i++) last = TOps.ApexSerialize(item);
        long end = Stopwatch.GetTimestamp();
        s_sink = last;
        return ToNs(end - start, iters);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double TimeMpSer(T item, int iters)
    {
        byte[]? last = null;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iters; i++) last = TOps.MpSerialize(item);
        long end = Stopwatch.GetTimestamp();
        s_sink = last;
        return ToNs(end - start, iters);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double TimeApexDe(byte[] bytes, int iters)
    {
        T last = default!;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iters; i++) last = TOps.ApexDeserialize(bytes);
        long end = Stopwatch.GetTimestamp();
        s_sink = last;
        return ToNs(end - start, iters);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double TimeMpDe(byte[] bytes, int iters)
    {
        T last = default!;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iters; i++) last = TOps.MpDeserialize(bytes);
        long end = Stopwatch.GetTimestamp();
        s_sink = last;
        return ToNs(end - start, iters);
    }

    private static double ToNs(long ticks, int iters) => ticks * (1e9 / Stopwatch.Frequency) / iters;

    private static double Median(double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    private static long MeasureAlloc(Func<object?> action)
    {
        const int n = 64;
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < n; i++) s_sink = action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / n;
    }
}

public static class BenchmarkEngine
{
    private static List<IScenario> CreateScenarios() => new()
    {
        new Scenario<BenchmarkPrimitives, StandardOps<BenchmarkPrimitives>>(
            "T1_01_Primitives", "Tier 1", "Basic Primitives (int, long, double, guid)",
            () => new BenchmarkPrimitives
            {
                IntVal = 42, LongVal = 999999999L, DoubleVal = 3.14159265, FloatVal = 1.414f,
                BoolVal = true, GuidVal = new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"), DateTimeVal = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc)
            }),
        new Scenario<BenchmarkVector3[], Vector3ArrayOps>(
            "T1_02_Vector3Array", "Tier 1", "Unmanaged Struct Array (10,000 items)",
            () =>
            {
                var a = new BenchmarkVector3[10_000];
                for (int i = 0; i < a.Length; i++) a[i] = new BenchmarkVector3 { X = i, Y = i * 2, Z = i * 3 };
                return a;
            }),
        new Scenario<BenchmarkPerson, StandardOps<BenchmarkPerson>>(
            "T1_03_Person", "Tier 1", "Class with Strings & Primitives",
            () => new BenchmarkPerson { Id = 101, Name = "Alice Johnson", Age = 30, Salary = 85000.50 }),
        new Scenario<BenchStrings, StandardOps<BenchStrings>>(
            "T1_04_Strings", "Tier 1", "Strings (ASCII / Japanese / Mixed)",
            () => new BenchStrings
            {
                Ascii = "The quick brown fox jumps over the lazy dog. 0123456789",
                Japanese = "吾輩は猫である。名前はまだ無い。どこで生れたかとんと見当がつかぬ。",
                Mixed = "Order #12345 — 東京都千代田区 (shipping)"
            }),
        new Scenario<BenchPadded, StandardOps<BenchPadded>>(
            "T1_05_PaddedStruct", "Tier 1", "Unmanaged Struct with Padding",
            () => new BenchPadded { A = 7, B = 1234567890123L, C = -12, D = 99 }),
        new Scenario<BenchShape, StandardOps<BenchShape>>(
            "T2_01_PolymorphicShape", "Tier 2", "Abstract Class Polymorphism (Circle/Rect)",
            () => new BenchCircle { Label = "Orb", Opacity = 0.95, Radius = 120.0 }),
        new Scenario<BenchCombinatorialContainer, StandardOps<BenchCombinatorialContainer>>(
            "T2_02_Combinatorial", "Tier 2", "Arrays, Lists, Dictionaries & Shapes",
            () => new BenchCombinatorialContainer
            {
                Numbers = new[] { 1, 2, 3, 4, 5 },
                Tags = new[] { "Red", "Green", "Blue", null },
                Names = new List<string?> { "Alpha", "Beta" },
                Scores = new Dictionary<string, int> { ["A"] = 10, ["B"] = 20 },
                Shapes = new BenchShape[] { new BenchCircle { Label = "C1", Radius = 5 }, new BenchRect { Label = "R1", Width = 10, Height = 20 } }
            }),
        new Scenario<BenchOrder, StandardOps<BenchOrder>>(
            "T2_03_NestedObject", "Tier 2", "Nested Objects (Order > Customer > Address)",
            () => new BenchOrder
            {
                OrderId = 900_001, Total = 1234.56m, Quantity = 3, CreatedAt = new DateTime(2026, 11, 15, 9, 30, 0, DateTimeKind.Utc),
                Customer = new BenchCustomer { Id = 77, Name = "Bob Smith", Address = new BenchAddress { Street = "1 Main St", City = "Springfield", Zip = 12345 } }
            }),
        new Scenario<BenchPersonList, StandardOps<BenchPersonList>>(
            "T2_04_ObjectList", "Tier 2", "List of Objects (100 items)",
            () => CreatePersonList(100)),
        new Scenario<BenchSparseDto, StandardOps<BenchSparseDto>>(
            "T3_01_SparseDto", "Tier 3", "Sparse Nullable & Bool DTO",
            () => new BenchSparseDto { V1 = 123, B1 = true, S1 = "Active", D1 = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc) }),
        new Scenario<BenchFlags, StandardOps<BenchFlags>>(
            "T3_02_ManyBools", "Tier 3", "16 Bool Flags DTO",
            () => new BenchFlags { Id = 5, F0 = true, F3 = true, F7 = true, F8 = true, F15 = true }),
        new Scenario<BenchNullableArray, StandardOps<BenchNullableArray>>(
            "T3_03_NullableArray", "Tier 3", "int?[] (1,000 items, 1/3 null)",
            () =>
            {
                var v = new int?[1000];
                for (int i = 0; i < v.Length; i++) v[i] = i % 3 == 0 ? null : i * 7;
                return new BenchNullableArray { Values = v };
            }),
        new Scenario<BenchGraphNode, GraphOps>(
            "T3_04_CircularGraph", "Tier 3", "Circular Reference Graph (100 nodes)",
            () =>
            {
                var nodes = new BenchGraphNode[100];
                for (int i = 0; i < nodes.Length; i++) nodes[i] = new BenchGraphNode { Id = i, Name = "N" + i };
                for (int i = 0; i < nodes.Length; i++)
                {
                    nodes[i].Next = nodes[(i + 1) % nodes.Length];
                    nodes[i].Other = nodes[(i * 37) % nodes.Length];
                }
                return nodes[0];
            }),
        new Scenario<BenchPersonList, StandardOps<BenchPersonList>>(
            "T3_05_LargePayload", "Tier 3", "Large Payload > 64KB (5,000 objects)",
            () => CreatePersonList(5000)),
#if NET11_0_OR_GREATER
        new Scenario<IEnumerable<(string? Outer, string? Inner)>, FullJoinOps>(
            "T3_06_FullJoin", "Tier 3", "FullJoin (.NET 11, 1,500 rows)",
            static () =>
            {
                var outer = Enumerable.Range(0, 1000).Select(static id => $"Item {id}").ToArray();
                var inner = Enumerable.Range(500, 1000).Select(static id => $"Item {id}").ToArray();
                return outer.FullJoin(inner, static value => value, static value => value);
            },
            static (expected, actual) => expected.SequenceEqual(actual)),
        new Scenario<BFloat16[], NumericsArrayOps<BFloat16>>(
            "T3_07_BFloat16Array", "Tier 3", "BFloat16[] (.NET 11, 4,096 items)",
            static () => Enumerable.Range(0, 4096).Select(static i => (BFloat16)(i * 0.125f)).ToArray(),
            static (expected, actual) => expected.AsSpan().SequenceEqual(actual)),
        new Scenario<Complex<Decimal128>[], NumericsArrayOps<Complex<Decimal128>>>(
            "T3_08_ComplexDecimal128Array", "Tier 3", "Complex<Decimal128>[] (1,024 items)",
            static () => Enumerable.Range(0, 1024)
                .Select(static i => new Complex<Decimal128>((Decimal128)(i + 1), (Decimal128)(-i)))
                .ToArray(),
            static (expected, actual) => expected.AsSpan().SequenceEqual(actual)),
#endif
    };

    private static BenchPersonList CreatePersonList(int count)
    {
        var list = new BenchPersonList();
        for (int i = 0; i < count; i++)
        {
            list.People.Add(new BenchmarkPerson { Id = i, Name = "Person_" + i, Age = 20 + i % 50, Salary = 1000.0 + i });
        }
        return list;
    }

    public static BenchmarkSuiteReport RunAll(Action<string, int, int>? progressCallback = null)
    {
        var runners = CreateScenarios();
        var scenarios = new List<BenchmarkScenarioResult>();
        int total = runners.Count;

        for (int i = 0; i < total; i++)
        {
            progressCallback?.Invoke(runners[i].Name, i + 1, total);
            scenarios.Add(runners[i].Run());
        }

        double logSumSer = 0;
        double logSumDeser = 0;
        long totalApexBytes = 0;
        long totalMpBytes = 0;
        bool allVerified = true;

        foreach (var s in scenarios)
        {
            logSumSer += Math.Log(s.SpeedupSerialize);
            logSumDeser += Math.Log(s.SpeedupDeserialize);
            totalApexBytes += s.ApexSizeBytes;
            totalMpBytes += s.MpSizeBytes;
            if (!s.RoundtripVerified) allVerified = false;
        }

        return new BenchmarkSuiteReport
        {
            Scenarios = scenarios,
            GeometricMeanSerializeSpeedup = Math.Exp(logSumSer / scenarios.Count),
            GeometricMeanDeserializeSpeedup = Math.Exp(logSumDeser / scenarios.Count),
            TotalSizeRatioVsMp = totalMpBytes > 0 ? (double)totalApexBytes / totalMpBytes : 1.0,
            AllVerified = allVerified
        };
    }
}
