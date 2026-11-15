using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    private readonly object _gate = new();
    private Dictionary<Type, UnionCase> _byType = new();
    private Dictionary<ushort, UnionCase> _byTag = new();

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
        var unionCase = (UnionCase)Activator.CreateInstance(typeof(UnionCase<>).MakeGenericType(typeof(T), type), tag)!;
        lock (_gate)
        {
            if (_byTag.ContainsKey(tag)) throw new ArgumentException($"Union tag {tag} is already registered.");
            // 読み取り中の辞書を変えないよう、書き換えた複製に差し替える
            var byType = new Dictionary<Type, UnionCase>(_byType) { [type] = unionCase };
            var byTag = new Dictionary<ushort, UnionCase>(_byTag) { [tag] = unionCase };
            _byType = byType;
            _byTag = byTag;
        }
    }

    /// <summary>具象型の対応を外す。</summary>
    public bool Remove(Type type)
    {
        lock (_gate)
        {
            if (!_byType.TryGetValue(type, out var unionCase)) return false;
            var byType = new Dictionary<Type, UnionCase>(_byType);
            byType.Remove(type);
            var byTag = new Dictionary<ushort, UnionCase>(_byTag);
            byTag.Remove(unionCase.Tag);
            _byType = byType;
            _byTag = byTag;
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
        if (_byType.TryGetValue(value.GetType(), out var unionCase))
        {
            writer.WriteCompactUInt((uint)unionCase.Tag + 1);
            unionCase.Write(ref writer, value, ref tracker);
            return;
        }
        ApexThrowHelper.ThrowUnregisteredUnionType(value.GetType(), typeof(T));
    }

    public override T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
    {
        uint header = reader.ReadCompactUInt();
        if (header == 0) return null!;
        if (header - 1 <= ushort.MaxValue && _byTag.TryGetValue((ushort)(header - 1), out var unionCase))
        {
            return unionCase.Read(ref reader, ref tracker, ref guard);
        }
        ApexThrowHelper.ThrowInvalidUnionTag(header - 1, typeof(T));
        return null!;
    }

    private abstract class UnionCase
    {
        protected UnionCase(ushort tag) => Tag = tag;

        public ushort Tag { get; }

        public abstract void Write<TWriter, TTracker>(ref TWriter writer, T value, ref TTracker tracker)
            where TWriter : struct, IApexBufferWriter, allows ref struct
            where TTracker : IReferenceTracker, allows ref struct;

        public abstract T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
            where TReader : struct, IApexBufferReader, allows ref struct
            where TTracker : IReferenceTracker, allows ref struct
            where TGuard : ISecurityGuard, allows ref struct;
    }

    private sealed class UnionCase<TDerived> : UnionCase where TDerived : T
    {
        public UnionCase(ushort tag) : base(tag) { }

        public override void Write<TWriter, TTracker>(ref TWriter writer, T value, ref TTracker tracker)
        {
            var derived = (TDerived)value;
            ApexFormatterCache<TDerived>.Instance.Write(ref writer, in derived, ref tracker);
        }

        public override T Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
            => ApexFormatterCache<TDerived>.Instance.Read(ref reader, ref tracker, ref guard);
    }
}
