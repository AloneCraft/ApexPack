using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Backend = global::MemoryPack.MemoryPackSerializer;
using Options = global::MemoryPack.MemoryPackSerializerOptions;

namespace Upstream.Harness;

/// <summary>
/// テスト中の呼び出しを記録する。記録は今実行中のテスト(<see cref="Begin"/> から <see cref="End"/> まで)に属する。
/// 時間はテスト中には測らず、テストの後に <see cref="CallRecord.Replay"/> で繰り返し呼んで測る(1 回だけの呼び出しは揺れが大きいため)。
/// </summary>
public static class Recorder
{
    private static readonly AsyncLocal<List<CallRecord>?> s_current = new();
    private static List<CallRecord>? s_fallback;

    public static void Begin()
    {
        var list = new List<CallRecord>();
        s_current.Value = list;
        s_fallback = list; // テストが別スレッドへ移っても記録できるように
    }

    public static List<CallRecord> End()
    {
        var list = s_current.Value ?? s_fallback ?? new List<CallRecord>();
        s_current.Value = null;
        s_fallback = null;
        return list;
    }

    private static void Add(CallRecord record)
    {
        var list = s_current.Value ?? s_fallback;
        if (list is null) return;
        lock (list) list.Add(record);
    }

    public static void Serialized<T>(T? value, Options? options)
        => Add(new SerializeRecord<T>(value, options));

    public static void Deserialized<T>(ReadOnlySpan<byte> bytes, Options? options)
        => Add(new DeserializeRecord<T>(bytes.ToArray(), options));

    public static void SerializedObject(Type type, object? value, Options? options)
        => Add(new ObjectRecord(type, value, null, options));

    public static void DeserializedObject(Type type, ReadOnlySpan<byte> bytes, Options? options)
        => Add(new ObjectRecord(type, null, bytes.ToArray(), options));

    /// <summary>MemoryStream の start から現在位置までのバイト列。</summary>
    public static byte[] Slice(MemoryStream stream, long start)
    {
        var all = stream.ToArray();
        int from = (int)Math.Min(start, all.Length);
        int to = (int)Math.Clamp(stream.Position, from, all.Length);
        return all.AsSpan(from, to - from).ToArray();
    }

    public static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }
}

/// <summary>
/// 記録した呼び出し 1 件。テストの後に、確認(<see cref="Check"/>)→ 暖機(<see cref="Warm"/>)→ 計測(<see cref="Measure"/>)の順に再生する。
/// </summary>
/// <remarks>
/// .NET は呼び出しの回数と時間がたまるまで最適化の弱いコードで動く(段階的コンパイル)。テストの直後にすぐ測ると、
/// その途中の状態を測ってしまい、ばらつきも大きい。そこで全記録をまとめて暖機し、最適化が終わるのを待ってから測る。
/// </remarks>
public abstract class CallRecord
{
    public abstract string Op { get; }

    public abstract string TypeName { get; }

    /// <summary>直列化後のバイト列(重複除去に使う)。直列化できなければ null。直列化の記録では <see cref="Prepare"/> で作る。</summary>
    public byte[]? Bytes { get; protected set; }

    /// <summary>確認が通り、計測できる状態か。</summary>
    protected bool Ready { get; private set; }

    /// <summary>
    /// テストの後に呼ぶ。直列化の記録はここで初めて直列化する(テスト中に余計な直列化をすると、コールバックのログなどテストの観測が変わるため)。
    /// </summary>
    public virtual void Prepare()
    {
    }

    /// <summary>再現性(同じ入力で同じバイト列)と往復(直列化 → 逆直列化 → 直列化で同じバイト列)を確かめ、計測の準備をする。</summary>
    public CallResult Check()
    {
        var result = new CallResult { Op = Op, Type = TypeName };
        try
        {
            CheckCore(result);
            Ready = true;
        }
        catch (Exception e)
        {
            result.ReplayError = $"{e.GetType().Name}: {e.Message}";
        }
        return result;
    }

    protected abstract void CheckCore(CallResult result);

    /// <summary>直列化を n 回実行し、経過した Stopwatch の刻みを返す。</summary>
    protected abstract long RunSerialize(int n);

    /// <summary>逆直列化を n 回実行し、経過した Stopwatch の刻みを返す。</summary>
    protected abstract long RunDeserialize(int n);

    /// <summary>最適化が進むよう、決まった回数か決まった時間だけ呼ぶ。</summary>
    public void Warm(int calls, double milliseconds)
    {
        if (!Ready) return;
        try
        {
            WarmOne(RunSerialize, calls, milliseconds);
            WarmOne(RunDeserialize, calls, milliseconds);
        }
        catch (Exception)
        {
            Ready = false;
        }
    }

    private static void WarmOne(Func<int, long> run, int calls, double milliseconds)
    {
        long limit = (long)(Stopwatch.Frequency * milliseconds / 1000.0);
        long spent = 0;
        for (int i = 0; i < calls && spent < limit; i++) spent += run(1);
    }

    /// <summary>1 回あたりの時間を測って <paramref name="result"/> に書く。</summary>
    public void Measure(CallResult result)
    {
        if (!Ready) return;
        try
        {
            result.SerializeNs = ReplayTimer.MeasureNs(RunSerialize);
            result.DeserializeNs = ReplayTimer.MeasureNs(RunDeserialize);
        }
        catch (Exception e)
        {
            result.SerializeNs = result.DeserializeNs = -1;
            result.ReplayError = $"{e.GetType().Name}: {e.Message}";
        }
    }

    protected static bool Same(byte[]? a, byte[]? b) => a is not null && b is not null && a.AsSpan().SequenceEqual(b);

    internal static string Describe(Type type)
    {
        if (!type.IsGenericType) return type.Name;
        var name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0) name = name.Substring(0, tick);
        var args = type.GetGenericArguments();
        var parts = new string[args.Length];
        for (int i = 0; i < args.Length; i++) parts[i] = Describe(args[i]);
        return $"{name}<{string.Join(", ", parts)}>";
    }
}

/// <summary>値の直列化を記録した。再生では同じ値の直列化と、その出力の逆直列化を測る。</summary>
public sealed class SerializeRecord<T> : CallRecord
{
    private readonly T? _value;
    private readonly Options? _options;
    private byte[] _serialized = Array.Empty<byte>();

    public SerializeRecord(T? value, Options? options)
    {
        _value = value;
        _options = options;
    }

    public override void Prepare()
    {
        try
        {
            Bytes = Backend.Serialize(in _value, _options);
        }
        catch
        {
            Bytes = null;
        }
    }

    public override string Op => "serialize";

    public override string TypeName => Describe(typeof(T));

    protected override void CheckCore(CallResult result)
    {
        var first = Backend.Serialize(in _value, _options);
        var second = Backend.Serialize(in _value, _options);
        result.Bytes = first.Length;
        result.Deterministic = Same(first, second);
        var restored = Backend.Deserialize<T>(first, _options);
        result.RoundTrip = Same(first, Backend.Serialize(in restored, _options));
        _serialized = first;
    }

    protected override long RunSerialize(int n)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i++) Direct.SinkLength = Direct.Serialize(in _value, _options).Length;
        return Stopwatch.GetTimestamp() - start;
    }

    protected override long RunDeserialize(int n)
    {
        var bytes = _serialized;
        long start = Stopwatch.GetTimestamp();
        T? last = default;
        for (int i = 0; i < n; i++) last = Direct.Deserialize<T>(bytes, _options);
        Direct.Sink = last;
        return Stopwatch.GetTimestamp() - start;
    }
}

/// <summary>バイト列の逆直列化を記録した。再生では逆直列化と、復元した値の直列化を測る。</summary>
public sealed class DeserializeRecord<T> : CallRecord
{
    private readonly Options? _options;
    private T? _value;

    public DeserializeRecord(byte[] bytes, Options? options)
    {
        Bytes = bytes;
        _options = options;
    }

    public override string Op => "deserialize";

    public override string TypeName => Describe(typeof(T));

    protected override void CheckCore(CallResult result)
    {
        var bytes = Bytes!;
        result.Bytes = bytes.Length;
        _value = Backend.Deserialize<T>(bytes, _options);
        var first = Backend.Serialize(in _value, _options);
        result.Deterministic = Same(first, Backend.Serialize(in _value, _options));
        result.RoundTrip = Same(first, bytes);
    }

    protected override long RunSerialize(int n)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i++) Direct.SinkLength = Direct.Serialize(in _value, _options).Length;
        return Stopwatch.GetTimestamp() - start;
    }

    protected override long RunDeserialize(int n)
    {
        var bytes = Bytes!;
        long start = Stopwatch.GetTimestamp();
        T? last = default;
        for (int i = 0; i < n; i++) last = Direct.Deserialize<T>(bytes, _options);
        Direct.Sink = last;
        return Stopwatch.GetTimestamp() - start;
    }
}

/// <summary>Type 指定(非ジェネリック)の呼び出しを記録した。</summary>
public sealed class ObjectRecord : CallRecord
{
    private readonly Type _type;
    private readonly bool _isSerialize;
    private readonly Options? _options;
    private object? _value;
    private byte[] _serialized = Array.Empty<byte>();

    public ObjectRecord(Type type, object? value, byte[]? bytes, Options? options)
    {
        _type = type;
        _value = value;
        _options = options;
        _isSerialize = bytes is null;
        Bytes = bytes;
    }

    public override void Prepare()
    {
        if (!_isSerialize) return;
        try
        {
            Bytes = Backend.Serialize(_type, _value, _options);
        }
        catch
        {
            Bytes = null;
        }
    }

    public override string Op => _isSerialize ? "serialize" : "deserialize";

    public override string TypeName => Describe(_type);

    protected override void CheckCore(CallResult result)
    {
        if (!_isSerialize) _value = Backend.Deserialize(_type, Bytes!, _options);
        var first = _isSerialize ? Backend.Serialize(_type, _value, _options) : Bytes!;
        result.Bytes = first.Length;
        result.Deterministic = Same(Backend.Serialize(_type, _value, _options), Backend.Serialize(_type, _value, _options));
        var restored = Backend.Deserialize(_type, first, _options);
        result.RoundTrip = Same(first, Backend.Serialize(_type, restored, _options));
        _serialized = first;
    }

    protected override long RunSerialize(int n)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i++) Direct.SinkLength = Direct.Serialize(_type, _value, _options).Length;
        return Stopwatch.GetTimestamp() - start;
    }

    protected override long RunDeserialize(int n)
    {
        var bytes = _serialized;
        long start = Stopwatch.GetTimestamp();
        object? last = null;
        for (int i = 0; i < n; i++) last = Direct.Deserialize(_type, bytes, _options);
        Direct.Sink = last;
        return Stopwatch.GetTimestamp() - start;
    }
}

/// <summary>
/// 時間の再生で呼ぶ入口。各ライブラリの公開 API を直接呼ぶ: MemoryPack 側は MemoryPackSerializer、
/// ApexPack 側は互換層を通さず ApexPackSerializer(互換層の、例外を MemoryPack の型に包む処理の分を含めないため)。
/// テストそのものと、再現性・往復の確認は互換層を通す。
/// </summary>
internal static class Direct
{
    // 結果を捨てると、最適化で処理ごと消されることがある(固定長の値の読み取りなど)。必ずここへ書く
    public static object? Sink;
    public static int SinkLength;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static byte[] Serialize<T>(in T? value, Options? options)
#if APEXPACK_BACKEND
        => global::ApexPack.ApexPackSerializer.Serialize<T>(value!);
#else
        => Backend.Serialize(in value, options);
#endif

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static T? Deserialize<T>(byte[] bytes, Options? options)
#if APEXPACK_BACKEND
        => global::ApexPack.ApexPackSerializer.Deserialize<T>(bytes);
#else
        => Backend.Deserialize<T>(bytes, options);
#endif

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static byte[] Serialize(Type type, object? value, Options? options)
#if APEXPACK_BACKEND
        => global::ApexPack.ApexPackSerializer.Serialize(type, value);
#else
        => Backend.Serialize(type, value, options);
#endif

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static object? Deserialize(Type type, byte[] bytes, Options? options)
#if APEXPACK_BACKEND
        => global::ApexPack.ApexPackSerializer.Deserialize(type, bytes);
#else
        => Backend.Deserialize(type, bytes, options);
#endif
}

/// <summary>繰り返し実行して 1 回あたりの時間を測る。</summary>
public static class ReplayTimer
{
    /// <summary>1 回の計測区間の目安(ミリ秒)。</summary>
    public static double BatchMilliseconds { get; set; } = 3;

    /// <summary>計測区間の数。最小値を採用する。</summary>
    public static int Batches { get; set; } = 7;

    /// <summary><paramref name="run"/> は n 回実行して経過した Stopwatch の刻みを返す。</summary>
    public static double MeasureNs(Func<int, long> run)
    {
        // 1 区間がおよそ BatchMilliseconds になる回数を決める
        int n = 1;
        long target = (long)(Stopwatch.Frequency * BatchMilliseconds / 1000.0);
        while (n < 1 << 24)
        {
            long ticks = run(n);
            if (ticks >= target / 4) break;
            n *= 4;
        }
        long probe = Math.Max(run(n), 1);
        n = (int)Math.Clamp((long)((double)n * target / probe), 1, 1 << 24);

        double best = double.MaxValue;
        for (int b = 0; b < Batches; b++)
        {
            long ticks = run(n);
            best = Math.Min(best, ticks * 1e9 / Stopwatch.Frequency / n);
        }
        return best;
    }
}
