using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ApexPack;

/// <summary>
/// Type 指定(非ジェネリック)の入口から型引数つきの入口へ橋渡しする。型ごとに 1 つ作って
/// <see cref="ConditionalWeakTable{TKey, TValue}"/> に置く(型を強く握らないので、型のアセンブリをアンロードできる)。
/// </summary>
internal abstract class ApexObjectBridge
{
    private static readonly ConditionalWeakTable<Type, ApexObjectBridge> s_bridges = new();

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Type 指定の入口は実行時に型を閉じる")]
    [UnconditionalSuppressMessage("Trimming", "IL2071", Justification = "同上")]
    public static ApexObjectBridge For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return s_bridges.GetValue(type, static t => (ApexObjectBridge)Activator.CreateInstance(typeof(ApexObjectBridge<>).MakeGenericType(t))!);
    }

    /// <summary>値を <paramref name="value"/> の型として書く。null は型の既定値として書く(値型は既定値、参照型は null)。</summary>
    public abstract byte[] Serialize(object? value);

    public abstract object? Deserialize(ReadOnlySpan<byte> bytes, out int consumed);
}

internal sealed class ApexObjectBridge<T> : ApexObjectBridge
{
    public override byte[] Serialize(object? value)
        => ApexPackSerializer.Serialize<T>(value is null ? default! : (T)value);

    public override object? Deserialize(ReadOnlySpan<byte> bytes, out int consumed)
        => ApexPackSerializer.Deserialize<T>(bytes, out consumed);
}
