using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Linq;
using ApexPack.Buffers;

namespace ApexPack;

/// <summary>実行時 Union の登録の補助。</summary>
public static class ApexDynamicUnion
{
    /// <summary>
    /// 開いたジェネリックの基底型(例: <c>IUnion&lt;&gt;</c>)に、開いたジェネリックの具象型(例: <c>CaseA&lt;&gt;</c>)を対応づける。
    /// 閉じた型(<c>IUnion&lt;int&gt;</c>)を最初に使うとき、同じ型引数で閉じた具象型の <see cref="ApexDynamicUnionFormatter{T}"/> を作る。
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "実行時 Union は対応する型を実行時に受け取るため")]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "同上")]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "同上")]
    public static void RegisterGeneric(Type genericUnionDefinition, params (ushort Tag, Type GenericCaseDefinition)[] cases)
    {
        ArgumentNullException.ThrowIfNull(genericUnionDefinition);
        ArgumentNullException.ThrowIfNull(cases);
        var copy = ((ushort Tag, Type GenericCaseDefinition)[])cases.Clone();
        ApexFormatterRegistry.RegisterGenericFactory(genericUnionDefinition, closed =>
        {
            var arguments = closed.GetGenericArguments();
            var closedCases = new (ushort Tag, Type Type)[copy.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                closedCases[i] = (copy[i].Tag, copy[i].GenericCaseDefinition.MakeGenericType(arguments));
            }
            return Activator.CreateInstance(typeof(ApexDynamicUnionFormatter<>).MakeGenericType(closed), new object[] { closedCases })!;
        });
    }
}

/// <summary>
/// 実行時 Union。基底型に [ApexUnion] を付けられない場合(別アセンブリの型、実行時に読み込むプラグインの型)に、
/// タグと具象型の対応表を実行中に作る。<see cref="ApexFormatterCache{T}.Register"/> で登録して使う。
/// </summary>
/// <remarks>
/// ワイヤ形式は [ApexUnion] と同じ: <c>CompactUInt(タグ + 1)</c>、0 は null、続けて具象型の本体。
/// プラグインを外すときは <see cref="Remove"/> で対応を外す。対応表だけが具象型を参照するので、外せば型を解放できる。
/// 対応表の変更はスレッドセーフだが、直列化と並行して変更した場合の結果は、どちらの状態で処理されたかに依存する。
/// </remarks>
public sealed class ApexDynamicUnionFormatter<T> : ApexFormatter<T> where T : class
{
    /// <summary>タグを配列で引く上限(これより大きいタグは辞書で引く)。</summary>
    private const int DenseTagLimit = 1024;

    private readonly object _gate = new();

    /// <summary>対応表。変更時は複製を作って差し替える(読み取り側はロックなし)。</summary>
    private Table _table = Table.Empty;

    public ApexDynamicUnionFormatter(params (ushort Tag, Type Type)[] cases)
    {
        foreach (var (tag, type) in cases) Add(tag, type);
    }

    /// <summary>タグと具象型の対応を足す。</summary>
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "実行時 Union は対応する型を実行時に受け取るため")]
    public void Add(ushort tag, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!typeof(T).IsAssignableFrom(type)) throw new ArgumentException($"{type} is not assignable to {typeof(T)}.");
        var unionCase = (UnionCase)Activator.CreateInstance(typeof(UnionCase<>).MakeGenericType(typeof(T), type), tag, type)!;
        lock (_gate)
        {
            if (_table.FindTag(tag) is not null) throw new ArgumentException($"Union tag {tag} is already registered.");
            _table = new Table(_table.Cases.Where(c => c.Type != type).Append(unionCase).ToArray());
        }
    }

    /// <summary>具象型の対応を外す。</summary>
    public bool Remove(Type type)
    {
        lock (_gate)
        {
            if (_table.FindType(type) is null) return false;
            _table = new Table(_table.Cases.Where(c => c.Type != type).ToArray());
            return true;
        }
    }

    public override void Write<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
    {
        if (value is null)
        {
            writer.WriteByte(0);
            return;
        }
        var unionCase = _table.FindType(value.GetType());
        if (unionCase is null)
        {
            ApexThrowHelper.ThrowUnregisteredUnionType(value.GetType(), typeof(T));
            return;
        }
        writer.WriteCompactUInt((uint)unionCase.Tag + 1);
        unionCase.Write(ref writer, value, ref tracker);
    }

    public override T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
    {
        uint header = reader.ReadCompactUInt();
        if (header == 0) return null!;
        var unionCase = _table.FindTag(header - 1);
        if (unionCase is null)
        {
            ApexThrowHelper.ThrowInvalidUnionTag(header - 1, typeof(T));
            return null!;
        }
        return unionCase.Read(ref reader, ref tracker, ref guard);
    }

    // ---- ルート: ジェネリックな仮想メソッド(呼び出しのたびに実行時の検索が要る)を通さず、具象型の非ジェネリックな入口を使う ----

    public override byte[] SerializeToArray(scoped in T value)
    {
        if (value is null) return new byte[] { 0 };
        var unionCase = _table.FindType(value.GetType());
        if (unionCase is null)
        {
            ApexThrowHelper.ThrowUnregisteredUnionType(value.GetType(), typeof(T));
            return null!;
        }
        byte[] body = unionCase.SerializeBody(value);
        uint header = (uint)unionCase.Tag + 1;
        int headerSize = ApexWire.CompactSize(header);
        var result = GC.AllocateUninitializedArray<byte>(headerSize + body.Length);
        ApexWire.WriteCompact(ref MemoryMarshal.GetArrayDataReference(result), header, BitConverter.IsLittleEndian);
        body.CopyTo(result.AsSpan(headerSize));
        return result;
    }

    public override T Deserialize(ReadOnlySpan<byte> source, out int consumed)
    {
        var reader = new ApexSpanReader(source);
        uint header = reader.ReadCompactUInt();
        int headerSize = reader.Consumed;
        if (header == 0)
        {
            consumed = headerSize;
            return null!;
        }
        var unionCase = _table.FindTag(header - 1);
        if (unionCase is null)
        {
            ApexThrowHelper.ThrowInvalidUnionTag(header - 1, typeof(T));
            consumed = 0;
            return null!;
        }
        T value = unionCase.DeserializeBody(source.Slice(headerSize), out int bodySize);
        consumed = headerSize + bodySize;
        return value;
    }

    /// <summary>対応表(不変)。具象型は少数なので線形に探し、タグは小さければ配列で引く。</summary>
    private sealed class Table
    {
        public static readonly Table Empty = new(Array.Empty<UnionCase>());

        public readonly UnionCase[] Cases;
        private readonly UnionCase?[] _dense;
        private readonly Dictionary<uint, UnionCase>? _sparse;

        public Table(UnionCase[] cases)
        {
            Cases = cases;
            int max = cases.Length == 0 ? -1 : cases.Max(c => (int)c.Tag);
            if (max < DenseTagLimit)
            {
                _dense = new UnionCase?[max + 1];
                foreach (var c in cases) _dense[c.Tag] = c;
            }
            else
            {
                _dense = Array.Empty<UnionCase?>();
                _sparse = cases.ToDictionary(c => (uint)c.Tag);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UnionCase? FindType(Type type)
        {
            var cases = Cases;
            for (int i = 0; i < cases.Length; i++)
            {
                if (ReferenceEquals(cases[i].Type, type)) return cases[i];
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UnionCase? FindTag(uint tag)
        {
            var dense = _dense;
            if (tag < (uint)dense.Length) return dense[tag];
            return _sparse is not null && _sparse.TryGetValue(tag, out var c) ? c : null;
        }
    }

    private abstract class UnionCase
    {
        protected UnionCase(ushort tag, Type type)
        {
            Tag = tag;
            Type = type;
        }

        public ushort Tag { get; }

        public Type Type { get; }

        public abstract void Write<TWriter, TTracker>(ref TWriter writer, T value, ref TTracker tracker)
            where TWriter : struct, IApexBufferWriter, allows ref struct
            where TTracker : IReferenceTracker, allows ref struct;

        public abstract T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
            where TReader : struct, IApexBufferReader, allows ref struct
            where TTracker : IReferenceTracker, allows ref struct
            where TGuard : ISecurityGuard, allows ref struct;

        /// <summary>本体だけを byte 配列にする(ルート用)。</summary>
        public abstract byte[] SerializeBody(T value);

        /// <summary>本体だけを読む(ルート用)。</summary>
        public abstract T DeserializeBody(ReadOnlySpan<byte> source, out int consumed);
    }

    private sealed class UnionCase<TDerived> : UnionCase where TDerived : T
    {
        public UnionCase(ushort tag, Type type) : base(tag, type) { }

        public override void Write<TWriter, TTracker>(ref TWriter writer, T value, ref TTracker tracker)
        {
            var derived = (TDerived)value;
            ApexFormatterCache<TDerived>.Instance.Write(ref writer, in derived, ref tracker);
        }

        public override T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
            => ApexFormatterCache<TDerived>.Instance.Read(ref reader, ref tracker, ref guard);

        public override byte[] SerializeBody(T value)
        {
            var derived = (TDerived)value;
            return ApexFormatterCache<TDerived>.Instance.SerializeToArray(in derived);
        }

        public override T DeserializeBody(ReadOnlySpan<byte> source, out int consumed)
            => ApexFormatterCache<TDerived>.Instance.Deserialize(source, out consumed);
    }
}
