using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace ApexPack;

/// <summary>
/// Abstraction for tracking object references to support circular references and shared instances.
/// When using <see cref="NullReferenceTracker"/>, all operations are JIT-eliminated with zero overhead.
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
/// Zero-size struct tracker used for default serialization.
/// All calls are completely eliminated as dead code by the JIT compiler.
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
/// Active reference tracker for opt-in circular reference support.
/// Uses a fast identity hash map with rented array buffers.
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
    private int _count;
    private uint _nextId;
    private object?[]? _idToObject;

    public ActiveReferenceTracker()
    {
        const int initialSize = 17;
        _buckets = ArrayPool<int>.Shared.Rent(initialSize);
        Array.Fill(_buckets, -1, 0, initialSize);
        _entries = ArrayPool<Entry>.Shared.Rent(initialSize);
        _idToObject = ArrayPool<object?>.Shared.Rent(initialSize);
        _count = 0;
        _nextId = 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextId() => _nextId++;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetReferenceId(object obj, out uint id)
    {
        if (_buckets == null || _entries == null)
        {
            id = 0;
            return false;
        }

        int hashCode = RuntimeHelpers.GetHashCode(obj);
        int bucket = (hashCode & 0x7FFFFFFF) % 17;

        for (int i = _buckets[bucket]; i >= 0; i = _entries[i].Next)
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

        if (_count >= _entries!.Length)
        {
            Resize();
        }

        int hashCode = RuntimeHelpers.GetHashCode(obj);
        int bucket = (hashCode & 0x7FFFFFFF) % 17;

        _entries[_count].HashCode = hashCode;
        _entries[_count].Next = _buckets![bucket];
        _entries[_count].Target = obj;
        _entries[_count].Id = id;
        _buckets[bucket] = _count;
        _count++;

        if (id >= _idToObject!.Length)
        {
            var newIdMap = ArrayPool<object?>.Shared.Rent((int)id * 2);
            Array.Copy(_idToObject, newIdMap, _idToObject.Length);
            ArrayPool<object?>.Shared.Return(_idToObject, clearArray: true);
            _idToObject = newIdMap;
        }
        _idToObject[id] = obj;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        int newSize = _count * 2 + 1;
        var newEntries = ArrayPool<Entry>.Shared.Rent(newSize);
        Array.Copy(_entries!, newEntries, _count);
        ArrayPool<Entry>.Shared.Return(_entries!, clearArray: true);
        _entries = newEntries;
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
