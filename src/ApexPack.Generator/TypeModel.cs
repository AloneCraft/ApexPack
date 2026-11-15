using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ApexPack.Generator;

/// <summary>[ApexPackable] 型の直列化方法。</summary>
internal enum PackKind
{
    /// <summary>[マスク][固定長領域][可変長メンバー] の一般形。</summary>
    Object,

    /// <summary>全メンバーが固定長の構造体。パディングなしの固定長表現(条件が合えば一括コピー)。</summary>
    FixedStruct,

    /// <summary>抽象クラス・インターフェース(タグ + 具象型の本体)。</summary>
    Union,

    /// <summary>ApexGenerateType.Collection(件数 + 要素)。</summary>
    Collection,
}

/// <summary>メンバーのワイヤ上の表し方。</summary>
internal enum MemberEncoding
{
    /// <summary>bool。親マスクの 1 ビット。</summary>
    BoolBit,

    /// <summary>固定長(固定長領域に置く)。</summary>
    Fixed,

    /// <summary>Nullable&lt;固定長&gt;。マスク 1 ビット + 値があるときだけ固定長領域の後ろに置く。</summary>
    NullableFixed,

    /// <summary>可変長(フォーマッタで書く)。null を取り得る参照型は <see cref="MemberModel.BitGuarded"/> でマスクのビットを使う。</summary>
    Variable,

    /// <summary>Nullable&lt;可変長の構造体&gt;。マスク 1 ビット + 値。</summary>
    NullableVariable,

    /// <summary>[ApexPackable] 参照型の T[] / List&lt;T&gt;。要素ループを直接展開する。</summary>
    InlineCollection,
}

/// <summary>固定長の型の情報。</summary>
internal sealed class FixedInfo
{
    public string TypeName = "";
    public int Size;

    /// <summary>[ApexPackable] 固定長構造体なら true(WriteFixed / ReadFixed を呼ぶ)。</summary>
    public bool IsPackable;

    /// <summary>LE 機のメモリ表現が正規形と一致する(静的に判定できる範囲)。</summary>
    public bool IsRaw;
}

internal sealed class MemberModel
{
    public ISymbol Symbol = null!;
    public string Name = "";
    public ITypeSymbol Type = null!;
    public string TypeName = "";
    public int Order;
    public MemberEncoding Encoding;
    public FixedInfo? Fixed;

    /// <summary>Variable / NullableVariable / InlineCollection の要素で使うフォーマッタ型。</summary>
    public string Formatter = "";

    /// <summary>
    /// null を取り得る参照型メンバー(Variable / InlineCollection)。null はマスクの 1 ビットで表し、本体は非 null のときだけ書く。
    /// </summary>
    public bool BitGuarded;

    /// <summary>InlineCollection: List なら true、配列なら false。</summary>
    public bool InlineIsList;

    /// <summary>InlineCollection の要素型。</summary>
    public string InlineElement = "";

    public int Bit = -1;
    public int CtorParameterIndex = -1;
    public bool CanSetAfterConstruction;
    public bool IsInitOnly;
    public bool IsRequired;
    public bool IsReadOnly;

    public string Local => "__m" + Order;
}

internal sealed class UnionCase
{
    public ushort Tag;
    public string TypeName = "";
    public int Depth;
}

internal sealed class CallbackModel
{
    public string Name = "";
    public bool IsStatic;
}

internal sealed class TypeModel
{
    public INamedTypeSymbol Symbol = null!;
    public PackKind Kind;
    public bool IsValueType;
    public bool IsInterface;
    public bool IsAbstract;

    /// <summary>生成コード内での自身の参照名(型引数込み)。</summary>
    public string SelfName = "";

    public List<MemberModel> Members = new();
    public int BitCount;

    /// <summary>クラスでマスクを持つとき、bit0 を「非 null」の印として常に 1 にする。</summary>
    public bool HasMarkerBit;

    public int MaskBits => BitCount + (HasMarkerBit ? 1 : 0);
    public int MaskBytes => (MaskBits + 7) / 8;
    public int FixedBytes;
    public int NullableFixedMaxBytes;

    public IMethodSymbol? Constructor;
    public List<MemberModel?> ConstructorArguments = new();
    public bool CreateEarly;

    public List<CallbackModel> OnSerializing = new();
    public List<CallbackModel> OnSerialized = new();
    public List<CallbackModel> OnDeserializing = new();
    public List<CallbackModel> OnDeserialized = new();

    public List<UnionCase> UnionCases = new();

    public string CollectionElement = "";
    public string CollectionElementFormatter = "";

    public bool PreserveReferences;

    /// <summary>基底型の生成コードにも ApexMinBodySize がある(new を付ける)。</summary>
    public bool HidesBaseMinBodySize;

    /// <summary>基底型の生成コードにも ApexPreserveReferences がある(new を付ける)。</summary>
    public bool HidesBasePreserveReferences;

    /// <summary>FixedStruct: 一括コピー可否の静的条件を満たすか。</summary>
    public bool FixedRawEligible;

    /// <summary>FixedStruct: 実行時に AND する入れ子構造体の条件式。</summary>
    public List<string> FixedRawConditions = new();

    public int MinBodySize => Kind == PackKind.FixedStruct ? FixedBytes : MaskBytes + FixedBytes;
}
