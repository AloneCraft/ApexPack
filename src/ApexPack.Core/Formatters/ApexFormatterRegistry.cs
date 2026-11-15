using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ApexPack;

/// <summary>
/// ジェネリック型の一括登録。<c>Box&lt;&gt;</c> に対して <c>BoxFormatter&lt;&gt;</c> を登録すると、
/// <c>Box&lt;int&gt;</c> や <c>Box&lt;string&gt;</c> などすべての閉じた型にそのフォーマッタを使う。
/// </summary>
/// <remarks>
/// 登録表は <see cref="ConditionalWeakTable{TKey, TValue}"/> で、型を強く参照しない(アンロード可能性の不変条件)。
/// 登録は、その閉じた型を最初に使う前に行うこと(<see cref="ApexFormatterCache{T}"/> と同じ制約)。
/// </remarks>
public static class ApexFormatterRegistry
{
    private static readonly ConditionalWeakTable<Type, Type> s_generic = new();
    private static readonly ConditionalWeakTable<Type, Func<Type, object>> s_factories = new();

    /// <summary>
    /// 開いたジェネリック型 <paramref name="genericTypeDefinition"/> に、開いたフォーマッタ型 <paramref name="genericFormatterDefinition"/> を登録する。
    /// フォーマッタ型は同じ型引数の数を持ち、閉じると <see cref="ApexFormatter{T}"/> を継承し引数なしコンストラクタを持つこと。
    /// </summary>
    public static void RegisterGeneric(Type genericTypeDefinition, Type genericFormatterDefinition)
    {
        ArgumentNullException.ThrowIfNull(genericTypeDefinition);
        ArgumentNullException.ThrowIfNull(genericFormatterDefinition);
        if (!genericTypeDefinition.IsGenericTypeDefinition || !genericFormatterDefinition.IsGenericTypeDefinition
            || genericTypeDefinition.GetGenericArguments().Length != genericFormatterDefinition.GetGenericArguments().Length)
        {
            throw new ArgumentException("Both types must be open generic type definitions with the same number of type parameters.");
        }
        s_generic.AddOrUpdate(genericTypeDefinition, genericFormatterDefinition);
    }

    /// <summary>
    /// 開いたジェネリック型に、閉じた型からフォーマッタ(<see cref="ApexFormatter{T}"/>)を作る関数を登録する。
    /// 関数は閉じた型(例: <c>Box&lt;int&gt;</c>)を受け取り、その型の <see cref="ApexFormatter{T}"/> を返すこと。
    /// </summary>
    public static void RegisterGenericFactory(Type genericTypeDefinition, Func<Type, object> factory)
    {
        ArgumentNullException.ThrowIfNull(genericTypeDefinition);
        ArgumentNullException.ThrowIfNull(factory);
        if (!genericTypeDefinition.IsGenericTypeDefinition) throw new ArgumentException("The type must be an open generic type definition.");
        s_factories.AddOrUpdate(genericTypeDefinition, factory);
    }

    /// <summary>一括登録を外す。すでに作られた閉じた型のフォーマッタには影響しない。</summary>
    public static bool UnregisterGeneric(Type genericTypeDefinition)
        => s_generic.Remove(genericTypeDefinition) | s_factories.Remove(genericTypeDefinition);

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "利用者が登録したジェネリック型の閉包。AOT では閉じた型ごとに ApexFormatterCache<T>.Register を使う。")]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "同上")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "同上")]
    internal static ApexFormatter<T>? TryCreate<T>()
    {
        var type = typeof(T);
        if (!type.IsGenericType) return null;
        var genericDefinition = type.GetGenericTypeDefinition();
        if (s_factories.TryGetValue(genericDefinition, out var factory))
        {
            return factory(type) as ApexFormatter<T>
                ?? throw new InvalidOperationException($"The factory registered for {genericDefinition} did not return ApexFormatter<{type}>.");
        }
        if (!s_generic.TryGetValue(genericDefinition, out var definition))
        {
            return null;
        }
        var closed = definition.MakeGenericType(type.GetGenericArguments());
        return Activator.CreateInstance(closed) as ApexFormatter<T>
            ?? throw new InvalidOperationException($"{closed} does not derive from ApexFormatter<{type}>.");
    }
}
