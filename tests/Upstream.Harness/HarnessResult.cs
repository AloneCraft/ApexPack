// テスト実行の結果(JSON)。Upstream.Harness(各テストプロジェクトに入る)が書き、Upstream.Runner が読む。
// 両方からこのファイルを共有する。

using System.Collections.Generic;

namespace Upstream.Harness;

public sealed class HarnessRun
{
    /// <summary>"MemoryPack" または "ApexPack"。</summary>
    public string Backend { get; set; } = "";

    public string Framework { get; set; } = "";

    public string StartedUtc { get; set; } = "";

    public List<TestResult> Tests { get; set; } = new();
}

public sealed class TestResult
{
    /// <summary>クラス名 + "." + メソッド名(+ Theory の引数)。両側の突き合わせに使う。</summary>
    public string Id { get; set; } = "";

    public string Class { get; set; } = "";

    public string Method { get; set; } = "";

    /// <summary>"pass" / "fail" / "skip"。</summary>
    public string Status { get; set; } = "";

    public string? ErrorType { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>ApexFeatureNotSupportedException の機能名(対象外の機能に当たった)。</summary>
    public string? Feature { get; set; }

    public string? SkipReason { get; set; }

    /// <summary>テストメソッド自体の所要時間(アサーションなどを含む。参考値)。</summary>
    public double TestMilliseconds { get; set; }

    public List<CallResult> Calls { get; set; } = new();
}

/// <summary>テスト中の Serialize / Deserialize 呼び出し 1 件を、テスト後に再生して測った結果。</summary>
public sealed class CallResult
{
    /// <summary>"serialize" / "deserialize"(テストで呼ばれた方)。</summary>
    public string Op { get; set; } = "";

    public string Type { get; set; } = "";

    /// <summary>直列化後のバイト数。</summary>
    public int Bytes { get; set; }

    /// <summary>直列化 1 回あたりの時間(ナノ秒、繰り返しの最小値)。測れなければ負。</summary>
    public double SerializeNs { get; set; } = -1;

    /// <summary>逆直列化 1 回あたりの時間(ナノ秒、繰り返しの最小値)。測れなければ負。</summary>
    public double DeserializeNs { get; set; } = -1;

    /// <summary>同じ値を 2 回直列化して同じバイト列になった。</summary>
    public bool? Deterministic { get; set; }

    /// <summary>直列化 → 逆直列化 → 直列化で、元と同じバイト列になった(往復して同じ値)。</summary>
    public bool? RoundTrip { get; set; }

    public string? ReplayError { get; set; }
}
