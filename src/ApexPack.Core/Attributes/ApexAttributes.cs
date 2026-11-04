using System;

namespace ApexPack;

/// <summary>
/// Marks a class or struct as serializable by ApexPack.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class ApexPackableAttribute : Attribute
{
    /// <summary>
    /// When true, enables object identity tracking to support circular references and shared instances.
    /// When false (default), object reference tracking is completely eliminated with ZERO runtime cost.
    /// </summary>
    public bool PreserveReferences { get; set; } = false;

    /// <summary>
    /// When true, includes schema versioning information to tolerate added/removed fields across versions.
    /// When false (default), fixed schema mode is used for maximum memory bus throughput.
    /// </summary>
    public bool Versioning { get; set; } = false;
}

/// <summary>
/// Ignores the target property or field during serialization.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public sealed class ApexIgnoreAttribute : Attribute
{
}

/// <summary>
/// Marks a specific constructor to be used by the deserializer.
/// </summary>
[AttributeUsage(AttributeTargets.Constructor, Inherited = false, AllowMultiple = false)]
public sealed class ApexConstructorAttribute : Attribute
{
}
