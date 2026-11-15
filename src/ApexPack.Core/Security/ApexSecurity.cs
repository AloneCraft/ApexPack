using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ApexPack;

/// <summary>
/// 悪意のある、または壊れたペイロードが安全上の制約に違反したときの例外。
/// </summary>
public sealed class ApexSecurityException : Exception
{
    public ApexSecurityException(string message) : base(message) { }
}

/// <summary>
/// 信頼できない入力を読むときの上限。
/// </summary>
public readonly struct ApexSecurityOptions
{
    public int MaxDepth { get; init; }
    public int MaxTotalElements { get; init; }
    public int MaxCollectionLength { get; init; }

    public static ApexSecurityOptions Default => new()
    {
        MaxDepth = 64,
        MaxTotalElements = 10_000_000,
        MaxCollectionLength = 10_000_000
    };
}

/// <summary>
/// 呼び出し単位の設定。グローバルな登録表を持たないため、利用側がこのインスタンスを捨てれば何も残らない。
/// </summary>
public sealed class ApexSerializerOptions
{
    /// <summary>信頼できない入力の上限。null なら上限なし(信頼済み入力)。</summary>
    public ApexSecurityOptions? Security { get; init; }

    /// <summary>
    /// <see cref="Type"/> 値を復元するときの名前解決。null なら呼び出し側の <see cref="System.Runtime.Loader.AssemblyLoadContext"/> で解決する。
    /// 信頼できない入力(<see cref="Security"/> 指定時)では必須。
    /// </summary>
    public Func<string, Type?>? TypeResolver { get; init; }
}

/// <summary>
/// コンパイル時に選ぶ安全上限の契約。
/// </summary>
public interface ISecurityGuard
{
    static abstract bool IsActive { get; }
    void EnterDepth();
    void ExitDepth();
    void AddElements(int count);

    /// <summary><see cref="Type"/> 値の名前を解決する。解決できなければ null。</summary>
    Type? ResolveType(string name);
}

/// <summary>
/// 信頼済み入力用(既定)。すべての呼び出しが JIT で消える。
/// </summary>
public readonly struct TrustedSecurityGuard : ISecurityGuard
{
    public static bool IsActive => false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnterDepth() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ExitDepth() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddElements(int count) { }

    /// <summary>
    /// 呼び出し側の文脈で解決する。collectible な <see cref="System.Runtime.Loader.AssemblyLoadContext"/> の型は
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext.EnterContextualReflection(System.Reflection.Assembly)"/> の範囲内で解決される。
    /// 名前から型へのキャッシュは持たない(アンロード可能性の不変条件)。
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = "型名はデータ由来。トリミング環境では ApexSerializerOptions.TypeResolver で解決する。")]
    public Type? ResolveType(string name) => Type.GetType(name, throwOnError: false);
}

/// <summary>
/// 信頼済み入力用で、型名の解決だけ利用者指定の関数を使う。
/// </summary>
public readonly struct TrustedResolvingGuard : ISecurityGuard
{
    private readonly Func<string, Type?> _resolver;

    public TrustedResolvingGuard(Func<string, Type?> resolver) => _resolver = resolver;

    public static bool IsActive => false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnterDepth() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ExitDepth() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddElements(int count) { }

    public Type? ResolveType(string name) => _resolver(name);
}

/// <summary>
/// 信頼できない入力用。再帰爆弾と増幅攻撃を止める。
/// </summary>
public struct UntrustedSecurityGuard : ISecurityGuard
{
    public static bool IsActive => true;

    private readonly ApexSecurityOptions _options;
    private readonly Func<string, Type?>? _typeResolver;
    private int _currentDepth;
    private int _totalElements;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UntrustedSecurityGuard(ApexSecurityOptions options, Func<string, Type?>? typeResolver = null)
    {
        _options = options;
        _typeResolver = typeResolver;
        _currentDepth = 0;
        _totalElements = 0;
    }

    /// <summary>信頼できない入力では、利用者が明示した解決関数がなければ型名を解決しない。</summary>
    public Type? ResolveType(string name)
    {
        if (_typeResolver is null)
        {
            throw new ApexSecurityException($"Resolving type name '{name}' from untrusted input requires ApexSerializerOptions.TypeResolver.");
        }
        return _typeResolver(name);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnterDepth()
    {
        _currentDepth++;
        if (_currentDepth > _options.MaxDepth)
        {
            ApexThrowHelper.ThrowRecursionLimitExceeded(_options.MaxDepth);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ExitDepth()
    {
        _currentDepth--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddElements(int count)
    {
        if (count < 0)
        {
            ApexThrowHelper.ThrowInvalidCollectionLength(count);
        }
        if (count > _options.MaxCollectionLength)
        {
            ApexThrowHelper.ThrowCollectionLengthExceeded(count, _options.MaxCollectionLength);
        }
        _totalElements += count;
        if (_totalElements > _options.MaxTotalElements || _totalElements < 0)
        {
            ApexThrowHelper.ThrowTotalElementsExceeded(_totalElements, _options.MaxTotalElements);
        }
    }
}

/// <summary>
/// 例外送出をホットパスから切り離し、インライン化を妨げない。
/// </summary>
public static class ApexThrowHelper
{
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidLength(int length)
    {
        throw new ApexSecurityException($"Invalid element length or allocation bomb detected: {length}");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowRecursionLimitExceeded(int maxDepth)
    {
        throw new ApexSecurityException($"Payload recursion depth exceeded maximum allowed depth of {maxDepth}.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidCollectionLength(int length)
    {
        throw new ApexSecurityException($"Negative collection length: {length}");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowCollectionLengthExceeded(int length, int maxAllowed)
    {
        throw new ApexSecurityException($"Collection length {length} exceeded maximum allowed length of {maxAllowed}.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowTotalElementsExceeded(int current, int maxAllowed)
    {
        throw new ApexSecurityException($"Total element count {current} exceeded quota limit of {maxAllowed}.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidCompactHeader(byte value)
    {
        throw new ApexSecurityException($"Invalid length header byte 0x{value:X2}.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidUnionTag(uint tag, Type targetType)
    {
        throw new InvalidOperationException($"Invalid union tag '{tag}' encountered for type '{targetType.FullName}'.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowUnregisteredUnionType(Type actualType, Type unionType)
    {
        throw new InvalidOperationException($"Type '{actualType.FullName}' is not registered as an [ApexUnion] case of '{unionType.FullName}'.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowInvalidBackReference(uint id)
    {
        throw new InvalidOperationException($"Corrupted back-reference ID '{id}': target object does not exist in tracker.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowNullNotAllowed(Type type, string? member = null)
    {
        throw new InvalidOperationException(member is null
            ? $"A null value of '{type.FullName}' was found where the declared type is non-nullable. Annotate it with '?' to allow null."
            : $"Member '{member}' of '{type.FullName}' is declared non-nullable but was null. Annotate it with '?' to allow null.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowUnexpectedNull(Type type)
    {
        throw new ApexSecurityException($"Payload contains null for non-nullable '{type.FullName}'.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowNotSupported(Type type)
    {
        throw new NotSupportedException($"ApexPack has no formatter for '{type.FullName}'. Mark it [ApexPackable] or register one with ApexFormatterCache<T>.Register.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowTypeNotResolved(string name)
    {
        throw new ApexSecurityException($"Type '{name}' could not be resolved.");
    }
}
