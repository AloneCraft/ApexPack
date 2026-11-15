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
/// 借りた配列は返却前に必ずクリアし、利用者オブジェクトへの参照を共有プールに残さない(アンロード可能性の不変条件)。
/// </summary>
public struct ActiveReferenceTracker : IReferenceTracker, IDisposable
{
    public static bool IsTrackingEnabled => true;

    private struct Entry
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
    private object?[]? _idToObject;

    public ActiveReferenceTracker()
    {
        const int initialCapacity = 32;
        _buckets = ArrayPool<int>.Shared.Rent(initialCapacity);
        Array.Fill(_buckets, -1, 0, initialCapacity);
        _entries = ArrayPool<Entry>.Shared.Rent(initialCapacity);
        _idToObject = ArrayPool<object?>.Shared.Rent(initialCapacity);
        _bucketMask = initialCapacity - 1;
        _count = 0;
        _nextId = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextId() => _nextId++;

    public bool TryGetReferenceId(object obj, out uint id)
    {
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
        if (_buckets == null || _entries == null)
        {
            this = new ActiveReferenceTracker();
        }

        if (_count >= _entries!.Length || _count > _bucketMask)
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

        if (id >= _idToObject!.Length)
        {
            var newIdMap = ArrayPool<object?>.Shared.Rent((int)Math.Max((long)id * 2, 64));
            Array.Copy(_idToObject, newIdMap, _idToObject.Length);
            ArrayPool<object?>.Shared.Return(_idToObject, clearArray: true);
            _idToObject = newIdMap;
        }
        _idToObject[id] = obj;
    }

    public bool TryGetObject(uint id, out object? obj)
    {
        if (_idToObject != null && id < _idToObject.Length && _idToObject[id] != null)
        {
            obj = _idToObject[id];
            return true;
        }
        obj = null;
        return false;
    }

    private void Resize()
    {
        int newCapacity = (int)BitOperations.RoundUpToPowerOf2((uint)(_count * 2));
        var newBuckets = ArrayPool<int>.Shared.Rent(newCapacity);
        Array.Fill(newBuckets, -1, 0, newCapacity);

        var newEntries = ArrayPool<Entry>.Shared.Rent(newCapacity);
        Array.Copy(_entries!, newEntries, _count);

        int newMask = newCapacity - 1;
        for (int i = 0; i < _count; i++)
        {
            int bucket = newEntries[i].HashCode & newMask;
            newEntries[i].Next = newBuckets[bucket];
            newBuckets[bucket] = i;
        }

        ArrayPool<int>.Shared.Return(_buckets!);
        ArrayPool<Entry>.Shared.Return(_entries!, clearArray: true);

        _buckets = newBuckets;
        _entries = newEntries;
        _bucketMask = newMask;
    }

    public void Dispose()
    {
        if (_buckets != null)
        {
            ArrayPool<int>.Shared.Return(_buckets);
            _buckets = null;
        }
        if (_entries != null)
        {
            ArrayPool<Entry>.Shared.Return(_entries, clearArray: true);
            _entries = null;
        }
        if (_idToObject != null)
        {
            ArrayPool<object?>.Shared.Return(_idToObject, clearArray: true);
            _idToObject = null;
        }
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
    /// ヘッダーを読む。新規インスタンスなら true を返し、<paramref name="newId"/> に予約した ID を入れる(本体読み取り後に Register する)。
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
