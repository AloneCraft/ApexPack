using System;
using System.Runtime.CompilerServices;
using ApexPack.Buffers;

namespace ApexPack;

/// <summary>
/// ApexPack が生成する型ごとの直列化契約。static abstract により仮想呼び出しなしで呼ばれる。
/// </summary>
/// <remarks>
/// 2 層に分かれる。
/// <list type="bullet">
/// <item><see cref="SerializeWithTracker"/> / <see cref="DeserializeGuarded"/>: null を取り得る位置用(クラスは null 表現を含む)。</item>
/// <item><see cref="SerializeNonNull"/> / <see cref="DeserializeNonNull"/>: null を取らない位置用(null 表現を書かない)。</item>
/// </list>
/// 参照追跡が有効なときは両者とも追跡ヘッダー付きの同じ形式になる。
/// </remarks>
public interface ISerializePackable<T> where T : ISerializePackable<T>
{
    /// <summary>null を取り得る位置での書き込み。</summary>
    static abstract void SerializeWithTracker<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct;

    /// <summary>null を取り得る位置での読み取り。</summary>
    static abstract void DeserializeGuarded<TReader, TTracker, TGuard>(ref TReader reader, scoped out T value, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct;

    /// <summary>null を取らない位置での書き込み。クラスで null が渡されたら例外。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static virtual void SerializeNonNull<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => T.SerializeWithTracker(ref writer, in value, ref tracker);

    /// <summary>null を取らない位置での読み取り。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static virtual void DeserializeNonNull<TReader, TTracker, TGuard>(ref TReader reader, scoped out T value, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        => T.DeserializeGuarded(ref reader, out value, ref tracker, ref guard);

    /// <summary>常に同じバイト数で表現される型ならそのサイズ、そうでなければ -1。</summary>
    static virtual int ApexFixedSize => -1;

    /// <summary>LE 機のメモリ表現がそのままワイヤ形式になるなら true(配列を一括コピーできる)。</summary>
    static virtual bool ApexIsRawLayout => false;

    /// <summary>本体の最小バイト数(件数ヘッダーの妥当性検査に使う)。</summary>
    static virtual int ApexMinBodySize => 0;

    /// <summary>
    /// 追跡なしの <see cref="SerializeWithTracker"/> が書くバイト数が値によらず決まる(または安く分かる)ならその値、そうでなければ -1。
    /// ルートの直列化は、分かれば正確な大きさの配列へ直接書く。
    /// </summary>
    static virtual int ApexExactSize(scoped in T value) => -1;

    /// <summary>true ならルート API が自動で参照追跡を有効にする。</summary>
    static virtual bool ApexPreserveReferences => false;

    /// <summary>固定長型の書き込み。<paramref name="destination"/> から <see cref="ApexFixedSize"/> バイトが書き込み可能であること。</summary>
    static virtual void WriteFixed(ref byte destination, scoped in T value, bool littleEndian)
        => throw new NotSupportedException($"{typeof(T)} is not a fixed-size type.");

    /// <summary>固定長型の読み取り。</summary>
    static virtual T ReadFixed(ref readonly byte source, bool littleEndian)
        => throw new NotSupportedException($"{typeof(T)} is not a fixed-size type.");

    /// <summary>追跡なしで書き込む。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static virtual void Serialize<TWriter>(ref TWriter writer, scoped in T value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
    {
        var tracker = new NullReferenceTracker();
        T.SerializeWithTracker(ref writer, in value, ref tracker);
    }

    /// <summary>追跡なし・信頼済み入力として読み取る。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static virtual void Deserialize<TReader>(ref TReader reader, scoped out T value)
        where TReader : struct, IApexBufferReader, allows ref struct
    {
        var tracker = new NullReferenceTracker();
        var guard = new TrustedSecurityGuard();
        T.DeserializeGuarded(ref reader, out value, ref tracker, ref guard);
    }

    /// <summary>指定の追跡器で、信頼済み入力として読み取る。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static virtual void DeserializeWithTracker<TReader, TTracker>(ref TReader reader, scoped out T value, ref TTracker tracker)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        var guard = new TrustedSecurityGuard();
        T.DeserializeGuarded(ref reader, out value, ref tracker, ref guard);
    }
}
