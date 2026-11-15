using System.Text.RegularExpressions;

namespace Upstream.Runner;

/// <summary>
/// テストごとの分類の規則。テストファイルは変えないので、MemoryPack の形式に依存するテストはここで理由つきで扱いを決める。
/// </summary>
public static class Classification
{
    /// <summary>
    /// 取り込んでいないテストメソッドの数(MemoryPack のジェネレーター本体と診断 ID に依存する SourceGeneratorTests、UPSTREAM.md 参照)。
    /// </summary>
    public const int ExcludedGeneratorTestMethods = 39;

    /// <summary>
    /// MemoryPack のバイト列そのもの(長さ・値)を比べるテスト。ApexPack は形式が違うので、
    /// 「同じ入力で毎回同じバイト列」かつ「往復して同じ値」(テスト中の呼び出しを再生して確かめる)で判定する。
    /// </summary>
    public static readonly IReadOnlyList<Rule> FormatSpecific = new Rule[]
    {
        new(@"^CollectionPublicApiTest\.\w*Binary$", "MemoryPack のバイト列と完全一致を比べる"),
        new(@"^CollectionPublicApiTest\.BitArrayUnusedBitsAreCleared$", "MemoryPack のバイト列と完全一致を比べる"),
        new(@"^ReaderTest\.ValidateInvalidLengthTest$", "MemoryPack の件数ヘッダー(4 バイト)を書き換えて読む"),
        new(@"^SerializerStructBufferWriterTest\.Serialize_ShouldSupportStructAsBufferWriter_WhenValueIsUnmanagedSZArray$", "MemoryPack の出力バイト数(16)を比べる"),
    };

    /// <summary>ApexPack では対象外(理由つき)。実行時に ApexFeatureNotSupportedException で分かるもの以外。</summary>
    public static readonly IReadOnlyList<Rule> OutOfScope = new Rule[]
    {
        new(@"^(SafeNullableTest|Net10DateTimeOffsetTest|UnityDateTimeOffsetTest)\.", "MemoryPack 独自の型(SafeNullable / Net10DateTimeOffset / UnityDateTimeOffset)"),
        new(@"^MalformedDataTest\.", "MemoryPack 形式の不正データ(ApexPack の不正データ対策は tests/ApexPack.Tests の SecurityDefenseTests で確認)"),
        new(@"^CollectionPublicApiTest\.BitArray(ReadsUnusedBitsAndLongerArray|LengthIsNotMatchedToArray|NullArray)", "MemoryPack 形式で手作りしたバイト列を読む"),
        new(@"^StringTest\.MalformedUtf8$", "MemoryPack 形式(UTF-8)の不正な文字列データ"),
        new(@"^WriterTest\.WriteObjectHeaderTest$", "低レベル API のヘッダーの符号値(MemoryPack は null = 255)を直接比べる"),
        new(@"^ReflectionTest\.", "MemoryPack のジェネレーターが作る内部メンバーを反射で呼ぶ"),
        new(@"^GeneratorTest\.Versioning$", "スキーマ変更(メンバーの追加)への耐性。VersionTolerant と同じ種類の機能"),
    };

    /// <summary>
    /// 仕様の違い。ApexPack が意図して MemoryPack と違う動きをするもの。
    /// </summary>
    public static readonly IReadOnlyList<Rule> DesignDifference = new Rule[]
    {
        new(@"^Fuzz\.Random$", "null を許さない(? なし)と宣言したメンバーに null を入れている。ApexPack は ? の有無で null を表すかを決め、? なしのメンバーの null は例外にする"),
    };

    public static Rule? Match(IReadOnlyList<Rule> rules, string testId)
        => rules.FirstOrDefault(r => r.Pattern.IsMatch(testId));

    public sealed class Rule
    {
        public Rule(string pattern, string reason)
        {
            Pattern = new Regex(pattern, RegexOptions.CultureInvariant);
            Reason = reason;
        }

        public Regex Pattern { get; }

        public string Reason { get; }
    }
}
