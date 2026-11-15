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

/// <summary>記録した呼び出し 1 件。</summary>
public abstract class CallRecord
{
    public abstract string Op { get; }

    public abstract string TypeName { get; }

    /// <summary>直列化後のバイト列(重複除去に使う)。直列化できなければ null。直列化の記録では <see cref="Prepare"/> で作る。</summary>
    public byte[]? Bytes { get; protected set; }

    /// <summary>
    /// テストの後に呼ぶ。直列化の記録はここで初めて直列化する(テスト中に余計な直列化をすると、コールバックのログなどテストの観測が変わるため)。
    /// </summary>
    public virtual void Prepare()
    {
    }

    /// <summary>テスト後に呼び出しを再生して、時間・再現性・往復を測る。</summary>
    public CallResult Replay(bool timing)
    {
        var result = new CallResult { Op = Op, Type = TypeName };
        try
        {
            ReplayCore(result, timing);
        }
        catch (Exception e)
        {
            result.ReplayError = $"{e.GetType().Name}: {e.Message}";
        }
        return result;
    }

    protected abstract void ReplayCore(CallResult result, bool timing);

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

    protected override void ReplayCore(CallResult result, bool timing)
    {
        var first = Backend.Serialize(in _value, _options);
        var second = Backend.Serialize(in _value, _options);
        result.Bytes = first.Length;
        result.Deterministic = Same(first, second);
        var restored = Backend.Deserialize<T>(first, _options);
        result.RoundTrip = Same(first, Backend.Serialize(in restored, _options));
        if (!timing) return;

        result.SerializeNs = ReplayTimer.MeasureNs(n =>
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < n; i++) Backend.Serialize(in _value, _options);
            return Stopwatch.GetTimestamp() - start;
        });
        result.DeserializeNs = ReplayTimer.MeasureNs(n =>
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < n; i++) Backend.Deserialize<T>(first, _options);
            return Stopwatch.GetTimestamp() - start;
        });
    }
}

/// <summary>バイト列の逆直列化を記録した。再生では逆直列化と、復元した値の直列化を測る。</summary>
public sealed class DeserializeRecord<T> : CallRecord
{
    private readonly Options? _options;

    public DeserializeRecord(byte[] bytes, Options? options)
    {
        Bytes = bytes;
        _options = options;
    }

    public override string Op => "deserialize";

    public override string TypeName => Describe(typeof(T));

    protected override void ReplayCore(CallResult result, bool timing)
    {
        var bytes = Bytes!;
        result.Bytes = bytes.Length;
        var value = Backend.Deserialize<T>(bytes, _options);
        var first = Backend.Serialize(in value, _options);
        result.Deterministic = Same(first, Backend.Serialize(in value, _options));
        result.RoundTrip = Same(first, bytes);
        if (!timing) return;

        result.DeserializeNs = ReplayTimer.MeasureNs(n =>
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < n; i++) Backend.Deserialize<T>(bytes, _options);
            return Stopwatch.GetTimestamp() - start;
        });
        result.SerializeNs = ReplayTimer.MeasureNs(n =>
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < n; i++) Backend.Serialize(in value, _options);
            return Stopwatch.GetTimestamp() - start;
        });
    }
}

/// <summary>Type 指定(非ジェネリック)の呼び出しを記録した。</summary>
public sealed class ObjectRecord : CallRecord
{
    private readonly Type _type;
    private readonly object? _value;
    private readonly bool _isSerialize;
    private readonly Options? _options;

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

    protected override void ReplayCore(CallResult result, bool timing)
    {
        object? value = _isSerialize ? _value : Backend.Deserialize(_type, Bytes!, _options);
        var first = _isSerialize ? Backend.Serialize(_type, value, _options) : Bytes!;
        result.Bytes = first.Length;
        result.Deterministic = Same(Backend.Serialize(_type, value, _options), Backend.Serialize(_type, value, _options));
        var restored = Backend.Deserialize(_type, first, _options);
        result.RoundTrip = Same(first, Backend.Serialize(_type, restored, _options));
        if (!timing) return;

        result.SerializeNs = ReplayTimer.MeasureNs(n =>
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < n; i++) Backend.Serialize(_type, value, _options);
            return Stopwatch.GetTimestamp() - start;
        });
        result.DeserializeNs = ReplayTimer.MeasureNs(n =>
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < n; i++) Backend.Deserialize(_type, first, _options);
            return Stopwatch.GetTimestamp() - start;
        });
    }
}

/// <summary>繰り返し実行して 1 回あたりの時間を測る。</summary>
public static class ReplayTimer
{
    /// <summary>1 回の計測区間の目安(ミリ秒)。</summary>
    public static double BatchMilliseconds { get; set; } = 3;

    /// <summary>計測区間の数。最小値を採用する。</summary>
    public static int Batches { get; set; } = 5;

    /// <summary><paramref name="run"/> は n 回実行して経過した Stopwatch の刻みを返す。</summary>
    public static double MeasureNs(Func<int, long> run)
    {
        run(1); // 初回(JIT など)を除く
        run(4);

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
