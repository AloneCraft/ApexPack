namespace MemoryPack;

// MemoryPack 1.22 と同じ名前・同じ引数の属性。意味は ApexPack.Generator が ApexPack の属性に読み替える
// (TypeAnalyzer.CompatAlias)。属性の名前・引数の位置を変えるとジェネレーターの読み取りと食い違うので、一緒に直すこと。

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class MemoryPackableAttribute : Attribute
{
    public GenerateType GenerateType { get; }
    public SerializeLayout SerializeLayout { get; }

    public MemoryPackableAttribute(GenerateType generateType = GenerateType.Object)
    {
        GenerateType = generateType;
        SerializeLayout = generateType is GenerateType.VersionTolerant or GenerateType.CircularReference
            ? SerializeLayout.Explicit
            : SerializeLayout.Sequential;
    }

    public MemoryPackableAttribute(SerializeLayout serializeLayout)
    {
        GenerateType = GenerateType.Object;
        SerializeLayout = serializeLayout;
    }

    public MemoryPackableAttribute(GenerateType generateType, SerializeLayout serializeLayout)
    {
        GenerateType = generateType;
        SerializeLayout = serializeLayout;
    }
}

/// <summary>値はジェネレーターが数値で読む(Object=0, VersionTolerant=1, CircularReference=2, Collection=3, NoGenerate=4)。</summary>
public enum GenerateType
{
    Object = 0,
    VersionTolerant = 1,
    CircularReference = 2,
    Collection = 3,
    NoGenerate = 4,
}

public enum SerializeLayout
{
    Sequential = 0,
    Explicit = 1,
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class MemoryPackUnionAttribute : Attribute
{
    public ushort Tag { get; }
    public Type Type { get; }

    public MemoryPackUnionAttribute(ushort tag, Type type)
    {
        Tag = tag;
        Type = type;
    }
}

/// <summary>別の型(インターフェースなど)に後から Union を付ける。ApexPack では実行時 Union として自動登録する。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MemoryPackUnionFormatterAttribute : Attribute
{
    public Type Type { get; }

    public MemoryPackUnionFormatterAttribute(Type type) => Type = type;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MemoryPackAllowSerializeAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MemoryPackOrderAttribute : Attribute
{
    public int Order { get; }

    public MemoryPackOrderAttribute(int order) => Order = order;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MemoryPackIgnoreAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class MemoryPackIncludeAttribute : Attribute;

[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class MemoryPackConstructorAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MemoryPackOnSerializingAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MemoryPackOnSerializedAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MemoryPackOnDeserializingAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MemoryPackOnDeserializedAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class GenerateTypeScriptAttribute : Attribute;

/// <summary>
/// ApexPack が持たない機能を表す属性の基底。メンバーにこの派生属性があると、ジェネレーターは型ごと
/// 「対象外」(直列化で ApexFeatureNotSupportedException を投げる)にする。
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public abstract class ApexUnsupportedAttribute : Attribute;

/// <summary>VersionTolerant と組み合わせる機能(データにないメンバーの初期値を残す)。ApexPack では対象外。</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SuppressDefaultInitializationAttribute : ApexUnsupportedAttribute;

/// <summary>MemoryPack の利用者定義の属性フォーマッタ。ApexPack では対象外(独自の書き方は ApexFormatter で登録する)。</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public abstract class MemoryPackCustomFormatterAttribute<T> : ApexUnsupportedAttribute
{
    public abstract IMemoryPackFormatter<T> GetFormatter();
}

/// <summary>MemoryPack の利用者定義の属性フォーマッタ。ApexPack では対象外。</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public abstract class MemoryPackCustomFormatterAttribute<TFormatter, T> : ApexUnsupportedAttribute
{
    public abstract TFormatter GetFormatter();
}

// ---- 組み込み属性フォーマッタ(すべて対象外。名前と引数だけ MemoryPack と同じにする) ----

public sealed class Utf8StringFormatterAttribute : ApexUnsupportedAttribute;

public sealed class Utf16StringFormatterAttribute : ApexUnsupportedAttribute;

public sealed class OrdinalIgnoreCaseStringDictionaryFormatter<TValue> : ApexUnsupportedAttribute;

public sealed class InternStringFormatterAttribute : ApexUnsupportedAttribute;

public sealed class BitPackFormatterAttribute : ApexUnsupportedAttribute;

public sealed class BrotliFormatterAttribute : ApexUnsupportedAttribute
{
    public System.IO.Compression.CompressionLevel CompressionLevel { get; }
    public int Window { get; }
    public int DecompressionSizeLimit { get; }

    public BrotliFormatterAttribute(System.IO.Compression.CompressionLevel compressionLevel = System.IO.Compression.CompressionLevel.Fastest, int window = 22, int decompressionSizeLimit = 1024 * 1024 * 128)
    {
        CompressionLevel = compressionLevel;
        Window = window;
        DecompressionSizeLimit = decompressionSizeLimit;
    }
}

public sealed class BrotliFormatterAttribute<T> : ApexUnsupportedAttribute
{
    public System.IO.Compression.CompressionLevel CompressionLevel { get; }
    public int Window { get; }

    public BrotliFormatterAttribute(System.IO.Compression.CompressionLevel compressionLevel = System.IO.Compression.CompressionLevel.Fastest, int window = 22)
    {
        CompressionLevel = compressionLevel;
        Window = window;
    }
}

public sealed class BrotliStringFormatterAttribute : ApexUnsupportedAttribute
{
    public System.IO.Compression.CompressionLevel CompressionLevel { get; }
    public int Window { get; }
    public int DecompressionSizeLimit { get; }

    public BrotliStringFormatterAttribute(System.IO.Compression.CompressionLevel compressionLevel = System.IO.Compression.CompressionLevel.Fastest, int window = 22, int decompressionSizeLimit = 1024 * 1024 * 128)
    {
        CompressionLevel = compressionLevel;
        Window = window;
        DecompressionSizeLimit = decompressionSizeLimit;
    }
}

public sealed class MemoryPoolFormatterAttribute<T> : ApexUnsupportedAttribute;

public sealed class ReadOnlyMemoryPoolFormatterAttribute<T> : ApexUnsupportedAttribute;
