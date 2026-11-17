using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using ApexPack.Buffers;

namespace ApexPack;

/// <summary>
/// 循環参照・共有インスタンスの追跡。<see cref="NullReferenceTracker"/> のときは JIT ですべて消える。
/// ID はワイヤに書かず、新規インスタンスが現れた順に 0, 1, 2... と暗黙に振る。
/// </summary>
public interface IReferenceTracker
{
    static abstract bool IsTrackingEnabled { get; }
    bool TryGetReferenceId(object obj, out uint id);
    void Register(object obj, uint id);
    bool TryGetObject(uint id, out object? obj);
    uint NextId();
}

/// <summary>
/// 追跡なし(既定)。すべての呼び出しが JIT のデッドコード除去で消える。
/// </summary>
public readonly struct NullReferenceTracker : IReferenceTracker
{
    public static bool IsTrackingEnabled => false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetReferenceId(object obj, out uint id)
    {
        id = 0;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Register(object obj, uint id)
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetObject(uint id, out object? obj)
    {
        obj = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextId() => 0;
}

/// <summary>
/// 参照追跡(オプトイン)。2 のべき乗サイズの同一性ハッシュ表を使う。
/// 作業配列はスレッドごとに使い回し、返す前に使った範囲を消す(利用者オブジェクトへの参照を残さない。アンロード可能性の不変条件)。
/// </summary>
public struct ActiveReferenceTracker : IReferenceTracker, IDisposable
{
    public static bool IsTrackingEnabled => true;

    internal struct Entry
    {
        public int HashCode;
        public int Next;
        public object? Target;
        public uint Id;
    }

    private int[]? _buckets;
    private Entry[]? _entries;
    private int _bucketMask;
    private int _count;
    private uint _nextId;
    private object? _firstReadObject;
    private object?[]? _idToObject;
    private uint _idLimit; // _idToObject に書いた最大 ID + 1(後始末で消す範囲)

    // 書き込みでは「オブジェクト → ID」(ハッシュ表)だけ、読み取りでは「ID → オブジェクト」だけを使う。
    // 書き込みは必ず TryGetReferenceId を先に呼ぶので、それで書き込み側と分かる。使わない側の表は作らない。
    private bool _writing;

    public ActiveReferenceTracker() => this = default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextId() => _nextId++;

    public bool TryGetReferenceId(object obj, out uint id)
    {
        _writing = true;
        if (_buckets == null || _entries == null)
        {
            id = 0;
            return false;
        }
        int hashCode = RuntimeHelpers.GetHashCode(obj);
        for (int i = _buckets[hashCode & _bucketMask]; i >= 0; i = _entries[i].Next)
        {
            if (_entries[i].HashCode == hashCode && ReferenceEquals(_entries[i].Target, obj))
            {
                id = _entries[i].Id;
                return true;
            }
        }
        id = 0;
        return false;
    }

    public void Register(object obj, uint id)
    {
        if (!_writing)
        {
            RegisterForRead(obj, id);
            return;
        }
        if (_entries == null)
        {
            TrackerArrays.RentWrite(out _buckets, out _entries);
            _bucketMask = _buckets.Length - 1;
        }
        if (_count >= _entries.Length || _count > _bucketMask)
        {
            Resize();
        }
        int hashCode = RuntimeHelpers.GetHashCode(obj);
        int bucket = hashCode & _bucketMask;
        _entries[_count].HashCode = hashCode;
        _entries[_count].Next = _buckets![bucket];
        _entries[_count].Target = obj;
        _entries[_count].Id = id;
        _buckets[bucket] = _count;
        _count++;
    }

    private void RegisterForRead(object obj, uint id)
    {
        // 単一インスタンスの復元では作業配列を借りず、最初の後方参照も解決できる。
        if (id == 0)
        {
            _firstReadObject = obj;
            return;
        }
        _idToObject ??= TrackerArrays.RentIds();
        if (id >= _idToObject.Length)
        {
            var newIdMap = new object?[(int)Math.Max((long)id * 2, 64)];
            Array.Copy(_idToObject, newIdMap, _idToObject.Length);
            Array.Clear(_idToObject);
            _idToObject = newIdMap;
        }
        _idToObject[id] = obj;
        if (id >= _idLimit) _idLimit = id + 1;
    }

    public bool TryGetObject(uint id, out object? obj)
    {
        if (id == 0)
        {
            obj = _firstReadObject;
            return obj != null;
        }
        obj = _idToObject != null && id < _idToObject.Length ? _idToObject[id] : null;
        return obj != null;
    }

    private void Resize()
    {
        int newCapacity = (int)BitOperations.RoundUpToPowerOf2((uint)(_count * 2));
        var newBuckets = new int[newCapacity];
        Array.Fill(newBuckets, -1);
        var newEntries = new Entry[newCapacity];
        Array.Copy(_entries!, newEntries, _count);
        int newMask = newCapacity - 1;
        for (int i = 0; i < _count; i++)
        {
            int bucket = newEntries[i].HashCode & newMask;
            newEntries[i].Next = newBuckets[bucket];
            newBuckets[bucket] = i;
        }
        // 古い配列は利用者のオブジェクトを残さないよう消してから手放す
        Array.Clear(_entries!, 0, _count);
        _buckets = newBuckets;
        _entries = newEntries;
        _bucketMask = newMask;
    }

    public void Dispose()
    {
        // 使った範囲だけ消して返す(利用者のオブジェクトへの参照を残さない。アンロード可能性の不変条件)
        if (_buckets != null && _entries != null)
        {
            Array.Clear(_entries, 0, _count);
            Array.Fill(_buckets, -1);
            TrackerArrays.ReturnWrite(_buckets, _entries);
        }
        if (_idToObject != null)
        {
            Array.Clear(_idToObject, 0, (int)Math.Min(_idLimit, (uint)_idToObject.Length));
            TrackerArrays.ReturnIds(_idToObject);
        }
        this = default;
    }
}

/// <summary>
/// 参照追跡の作業配列を読み取り用と書き込み用に分け、スレッドごとに使い回す。返すときは中身を消してあるので、利用者のオブジェクトは残らない。
/// 大きくなった配列は使い回さない(メモリを抱え込まないため)。
/// </summary>
internal static class TrackerArrays
{
    private const int InitialCapacity = 32;
    private const int MaxPooledCapacity = 4096;

    [ThreadStatic] private static int[]? t_buckets;
    [ThreadStatic] private static ActiveReferenceTracker.Entry[]? t_entries;
    [ThreadStatic] private static object?[]? t_ids;

    public static void RentWrite(out int[] buckets, out ActiveReferenceTracker.Entry[] entries)
    {
        buckets = t_buckets ?? NewBuckets();
        entries = t_entries ?? new ActiveReferenceTracker.Entry[InitialCapacity];
        t_buckets = null;
        t_entries = null;
    }

    public static void ReturnWrite(int[] buckets, ActiveReferenceTracker.Entry[] entries)
    {
        if (buckets.Length <= MaxPooledCapacity && entries.Length <= MaxPooledCapacity)
        {
            t_buckets = buckets;
            t_entries = entries;
        }
    }

    public static object?[] RentIds()
    {
        var ids = t_ids ?? new object?[InitialCapacity];
        t_ids = null;
        return ids;
    }

    public static void ReturnIds(object?[] ids)
    {
        if (ids.Length <= MaxPooledCapacity)
        {
            t_ids = ids;
        }
    }

    private static int[] NewBuckets()
    {
        var buckets = new int[InitialCapacity];
        Array.Fill(buckets, -1);
        return buckets;
    }
}

/// <summary>
/// 参照追跡時のヘッダー。CompactUInt で 0 = null、1 = 新規インスタンス、n ≥ 2 = ID (n − 2) への後方参照。
/// </summary>
public static class ApexReferenceHeader
{
    public const uint Null = 0;
    public const uint NewInstance = 1;

    /// <summary>ヘッダーを書く。続けて本体を書くべきなら true。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Write<TWriter, TTracker>(ref TWriter writer, object? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        if (value is null)
        {
            writer.WriteByte(0);
            return false;
        }
        if (tracker.TryGetReferenceId(value, out uint id))
        {
            writer.WriteCompactUInt(id + 2);
            return false;
        }
        tracker.Register(value, tracker.NextId());
        writer.WriteByte(1);
        return true;
    }

    /// <summary>
    /// ヘッダーを読む。新規インスタンスなら true を返し、<paramref name="newId"/> に予約した ID を入れる(本体を読む前に Register する)。
    /// null または後方参照なら false で、<paramref name="existing"/> に結果を入れる。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Read<TReader, TTracker>(ref TReader reader, ref TTracker tracker, out uint newId, out object? existing)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        uint header = reader.ReadCompactUInt();
        if (header == NewInstance)
        {
            newId = tracker.NextId();
            existing = null;
            return true;
        }
        newId = 0;
        if (header == Null)
        {
            existing = null;
            return false;
        }
        if (!tracker.TryGetObject(header - 2, out existing))
        {
            ApexThrowHelper.ThrowInvalidBackReference(header - 2);
        }
        return false;
    }
}
