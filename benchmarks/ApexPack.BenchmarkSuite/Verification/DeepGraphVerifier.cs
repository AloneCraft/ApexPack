using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ApexPack.BenchmarkSuite.Verification;

/// <summary>
/// Fast deep structural and referential equivalence verifier.
/// Compiles zero-boxing field getters with DynamicMethod to verify hundreds of thousands of objects in milliseconds.
/// </summary>
public static class DeepGraphVerifier
{
    private static readonly ConcurrentDictionary<Type, List<(string Name, Func<object, object?> Getter)>> s_getters = new();

    public static bool AreEqual(object? a, object? b)
    {
        var visited = new HashSet<(object, object)>(ReferenceTupleComparer.Instance);
        return DeepEqualsInternal(a, b, visited);
    }

    private static bool DeepEqualsInternal(object? a, object? b, HashSet<(object, object)> visited)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        var typeA = a.GetType();
        var typeB = b.GetType();
        if (typeA != typeB) return false;

        // Primitives / basic value types / strings
        if (typeA.IsPrimitive || typeA == typeof(string) || typeA == typeof(decimal) || typeA == typeof(Guid) || typeA == typeof(DateTime) || typeA == typeof(TimeSpan))
        {
            return a.Equals(b);
        }

        // Cycle / shared reference guard
        if (!typeA.IsValueType)
        {
            if (!visited.Add((a, b)))
            {
                return true; // Already compared in this cycle
            }
        }

        // Arrays / Lists / Collections
        if (a is IEnumerable enumA && b is IEnumerable enumB)
        {
            var itA = enumA.GetEnumerator();
            var itB = enumB.GetEnumerator();
            try
            {
                while (itA.MoveNext())
                {
                    if (!itB.MoveNext()) return false;
                    if (!DeepEqualsInternal(itA.Current, itB.Current, visited)) return false;
                }
                return !itB.MoveNext();
            }
            finally
            {
                if (itA is IDisposable da) da.Dispose();
                if (itB is IDisposable db) db.Dispose();
            }
        }

        // Dynamic compiled field comparison
        var fields = s_getters.GetOrAdd(typeA, static t =>
        {
            var list = new List<(string, Func<object, object?>)>();
            for (var curr = t; curr != null && curr != typeof(object); curr = curr.BaseType)
            {
                foreach (var f in curr.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.Name.Contains("k__BackingField") || !f.IsPrivate)
                    {
                        list.Add((f.Name, ApexDynamicIlHelper.GetFieldGetter(f)));
                    }
                }
            }
            return list;
        });

        foreach (var (name, getter) in fields)
        {
            var valA = getter(a);
            var valB = getter(b);
            if (!DeepEqualsInternal(valA, valB, visited)) return false;
        }

        return true;
    }

    private sealed class ReferenceTupleComparer : IEqualityComparer<(object A, object B)>
    {
        public static readonly ReferenceTupleComparer Instance = new();

        public bool Equals((object A, object B) x, (object A, object B) y)
        {
            return ReferenceEquals(x.A, y.A) && ReferenceEquals(x.B, y.B);
        }

        public int GetHashCode((object A, object B) obj)
        {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(obj.A), RuntimeHelpers.GetHashCode(obj.B));
        }
    }
}
