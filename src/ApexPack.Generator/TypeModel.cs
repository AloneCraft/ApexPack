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

    /// <summary>
    /// ApexPack が持たない機能を要求する型(MemoryPack 互換属性の VersionTolerant など)。
    /// コンパイルは通し、直列化・逆直列化で ApexFeatureNotSupportedException を投げる。
    /// </summary>
    Unsupported,
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

    /// <summary>MemoryPack 互換: (ref MemoryPackWriter&lt;T&gt;, ref T?) / (ref MemoryPackReader, ref T?) を受け取る。</summary>
    public bool WithStream;
}

/// <summary>MemoryPack 互換の [MemoryPackUnionFormatter]: 別の型(インターフェースなど)に後から付ける Union。</summary>
internal sealed class ExternalUnionModel
{
    /// <summary>閉じた基底型の名前(IsOpenGeneric が false のとき)。</summary>
    public string UnionTypeName = "";

    /// <summary>基底型が開いたジェネリック(typeof(IUnion&lt;&gt;))として指定された。</summary>
    public bool IsOpenGeneric;

    /// <summary>開いたジェネリックの基底型(typeof 用、例: global::NS.IUnion&lt;&gt;)。</summary>
    public string OpenUnionTypeName = "";

    /// <summary>タグと具象型(開いたジェネリックなら具象型も開いた形)。</summary>
    public List<UnionCase> Cases = new();
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

    /// <summary>Collection: List&lt;T&gt; の派生(要素を span で読み書きできる)。</summary>
    public bool CollectionIsList;

    /// <summary>Collection: 追加の呼び方。"direct"(公開の Add(T))、"keyvalue"(Add(key, value))、"interface"(ICollection&lt;T&gt;.Add)。</summary>
    public string CollectionAdd = "interface";

    /// <summary>Collection: 公開の EnsureCapacity(int) がある。</summary>
    public bool CollectionEnsureCapacity;

    public bool PreserveReferences;

    /// <summary>
    /// ルートで参照追跡を始めるか。自分が PreserveReferences か、メンバー(配列・ジェネリック引数・Union の case を含む)に
    /// PreserveReferences の型を含むなら true。共有・循環の参照を直列化全体で 1 つの表で扱うため(MemoryPack と同じ)。
    /// </summary>
    public bool RootPreserveReferences;

    /// <summary>MemoryPack 互換の属性([MemoryPackable] など)で宣言された型。コールバックは MemoryPack の規則で呼ぶ。</summary>
    public bool IsCompat;

    /// <summary>Kind が Unsupported のときの機能名。</summary>
    public string UnsupportedFeature = "";

    /// <summary>基底型の生成コードにも ApexMinBodySize がある(new を付ける)。</summary>
    public bool HidesBaseMinBodySize;

    /// <summary>基底型の生成コードにも ApexPreserveReferences がある(new を付ける)。</summary>
    public bool HidesBasePreserveReferences;

    /// <summary>
    /// ジェネリック構造体で、型引数のメンバーが実行時に固定長なら構造体全体も固定長になる(ConditionalFixed)。
    /// 条件: マスクなし、メンバーは固定長か型引数だけ、コールバックなし。形式は Object と同じ(固定長メンバー → 型引数メンバーの順)。
    /// </summary>
    public bool ConditionalFixed;

    /// <summary>FixedStruct: 一括コピー可否の静的条件を満たすか。</summary>
    public bool FixedRawEligible;

    /// <summary>FixedStruct: 実行時に AND する入れ子構造体の条件式。</summary>
    public List<string> FixedRawConditions = new();

    public int MinBodySize => Kind == PackKind.FixedStruct ? FixedBytes : MaskBytes + FixedBytes;
}
