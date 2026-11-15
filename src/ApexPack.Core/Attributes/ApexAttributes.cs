using System;

namespace ApexPack;

/// <summary>[ApexPackable] 型の直列化方法。</summary>
public enum ApexGenerateType
{
    /// <summary>メンバーを直列化する(既定)。</summary>
    Object = 0,

    /// <summary>
    /// <see cref="System.Collections.Generic.ICollection{T}"/> を実装する型を、件数と要素の列として直列化する。
    /// 引数なしコンストラクタと Add が必要。型自身のメンバーは書かない。
    /// </summary>
    Collection = 1,
}

/// <summary>
/// ApexPack で直列化する型(class / struct / record / interface)に付ける。型は partial であること。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class ApexPackableAttribute : Attribute
{
    /// <summary>
    /// true にすると、この型をルートとして直列化するときに参照追跡(循環参照・共有インスタンスの保持)を自動で有効にする。
    /// 追跡は呼び出し単位で、有効なときはグラフ内のすべてのクラスが対象になる。
    /// </summary>
    public bool PreserveReferences { get; set; } = false;

    /// <summary>直列化方法。</summary>
    public ApexGenerateType GenerateType { get; set; } = ApexGenerateType.Object;

    /// <summary>
    /// 予約。スキーマのバージョン耐性は現在サポートしていない(true にするとコンパイル診断 APX008)。
    /// </summary>
    public bool Versioning { get; set; } = false;
}

/// <summary>
/// 多態(抽象クラス・インターフェース)の具象型とタグを登録する。タグはワイヤ上の識別子なので変更しないこと。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false, AllowMultiple = true)]
public sealed class ApexUnionAttribute : Attribute
{
    public ushort Tag { get; }
    public Type DerivedType { get; }

    public ApexUnionAttribute(ushort tag, Type derivedType)
    {
        Tag = tag;
        DerivedType = derivedType;
    }
}

/// <summary>このメンバーを直列化しない。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public sealed class ApexIgnoreAttribute : Attribute
{
}

/// <summary>非公開のフィールド・プロパティを直列化対象に含める。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public sealed class ApexIncludeAttribute : Attribute
{
}

/// <summary>
/// メンバーの順序を指定する。1 つでも指定したら全メンバーに指定すること。未指定なら宣言順(基底クラスが先)。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public sealed class ApexOrderAttribute : Attribute
{
    public int Order { get; }

    public ApexOrderAttribute(int order)
    {
        Order = order;
    }
}

/// <summary>逆直列化に使うコンストラクタ。引数名はメンバー名と(大文字小文字を無視して)一致させる。</summary>
[AttributeUsage(AttributeTargets.Constructor, Inherited = false, AllowMultiple = false)]
public sealed class ApexConstructorAttribute : Attribute
{
}

/// <summary>
/// このメンバーに独自フォーマッタを使う。<paramref name="formatterType"/> は <see cref="IApexFormatter{T}"/> を実装する構造体。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public sealed class ApexFormatterAttribute : Attribute
{
    public Type FormatterType { get; }

    public ApexFormatterAttribute(Type formatterType)
    {
        FormatterType = formatterType;
    }
}

/// <summary>直列化の直前に呼ぶ(引数なし、インスタンスまたは static)。</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ApexOnSerializingAttribute : Attribute
{
}

/// <summary>直列化の直後に呼ぶ(引数なし、インスタンスまたは static)。</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ApexOnSerializedAttribute : Attribute
{
}

/// <summary>
/// 逆直列化の直前に呼ぶ(引数なし)。インスタンスメソッドは、引数なしコンストラクタで先に生成できる型でだけ使える。
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ApexOnDeserializingAttribute : Attribute
{
}

/// <summary>逆直列化の直後に呼ぶ(引数なし、インスタンスまたは static)。</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
public sealed class ApexOnDeserializedAttribute : Attribute
{
}
