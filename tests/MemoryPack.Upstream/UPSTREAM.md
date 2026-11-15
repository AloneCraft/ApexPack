# MemoryPack 公式テストの取り込み記録

このフォルダの `.cs` は、MemoryPack 公式テストを**無改変**でコピーしたもの。編集しないこと。
ApexPack と MemoryPack の比較に使う。

| 項目 | 値 |
| --- | --- |
| 取得元 | https://github.com/Cysharp/MemoryPack の `tests/MemoryPack.Tests` |
| タグ | `1.22.0`(NuGet の MemoryPack 1.22.0 と同じ版) |
| コミット | `239ffa0df2f7ccad63d276457557b49e88acae4b`(2026-11-11) |
| 取得日 | 2026-11-15 |
| ライセンス | MIT(Copyright (c) 2022 Cysharp, Inc.)。全文は `LICENSE.MemoryPack` |

## 除外したファイル

MemoryPack のジェネレーター本体(`MemoryPack.Generator.MemoryPackGenerator`)と診断 ID(MEMPACKxxx)に直接依存するため、取り込んでいない(テストメソッド 39 個)。
比較画面では「取り込み対象外(ジェネレーター診断)」として件数を示す(`Upstream.Runner.Classification.ExcludedGeneratorTestMethods`)。

- `SourceGeneratorTests/GeneratorDiagnosticsTest.cs`
- `SourceGeneratorTests/GeneratorDiagnosticsTest.DateTimeOffset.cs`
- `SourceGeneratorTests/GeneratorDiagnosticsTest.ManyMembers.cs`
- `SourceGeneratorTests/GeneratorDiagnosticsTest.TypeScript.cs`
- `SourceGeneratorTests/IncrementalGeneratorTest.cs`
- `Utils/CSharpGeneratorRunner.cs`(上の診断テスト専用の補助)

## 更新の手順

1. 新しいタグのリポジトリを取得する(NuGet の MemoryPack と同じ版にそろえる)。
2. `tests/MemoryPack.Tests` から、上の除外を除く `.cs` を同じ相対パスで上書きコピーする。
3. この表のタグ・コミット・取得日を更新し、`Upstream.OnMemoryPack` が全件通ることを確かめる。
4. `Upstream.Runner` で比較し、「失敗」「置換判定で不合格」が出たら原因を調べる(ApexPack・互換層を直すか、`Classification.cs` の規則に理由つきで足す)。

## 比較の仕組み

| プロジェクト | 役割 |
| --- | --- |
| `tests/MemoryPack.Upstream` | このフォルダ。上流テスト(共有プロジェクト、無改変) |
| `tests/Upstream.Harness` | 共有プロジェクト。`MemoryPack.Tests.MemoryPackSerializer`(呼び出しの横取りと記録)、テスト後の再生による計測、harness 引数で起動したときの実行入口 |
| `tests/Upstream.OnMemoryPack` | 上流テスト + Harness を本物の MemoryPack 1.22.0 で実行する |
| `tests/Upstream.OnApexPack` | 上流テスト + Harness を互換層(`src/ApexPack.MemoryPackCompat`)と ApexPack.Generator で実行する |
| `tests/Upstream.Runner` | 両側を別プロセスで実行し、結果を突き合わせて分類する(コマンドラインと比較画面の両方から使う) |

- テストファイルは namespace `MemoryPack.Tests` の中で `MemoryPackSerializer` を使う。C# はこの名前を「`MemoryPack.Tests` の型 → 外側の `MemoryPack` の型 → using」の順に探すので、global using の別名では横取りできない。Harness は同じ名前の型を `MemoryPack.Tests` に置き、そこから `global::MemoryPack.MemoryPackSerializer`(各プロジェクトの MemoryPack)を呼ぶ。
- 時間はテスト中には測らない。テスト中の Serialize / Deserialize 呼び出しを記録し、テストの後に繰り返し再生して、1 回あたりの最小値を採る。直列化の出力をそのまま逆直列化した呼び出しは、二重に数えない。
- 実行: `dotnet run --project tests/Upstream.Runner -c Release -- --build`(結果はリポジトリ直下の `upstream_report.json`)。比較画面は `benchmarks/ApexPack.BenchmarkGui` の「MemoryPack テスト比較」タブ。

## 判定

| 判定 | 条件 |
| --- | --- |
| 一致 | 両側で合格 |
| 置換判定で合格 | MemoryPack のバイト列そのものを比べるテスト(`Classification.FormatSpecific`)で、ApexPack 側の記録した呼び出しがすべて「同じ入力で毎回同じバイト列」かつ「往復して同じ値(直列化 → 逆直列化 → 直列化で同じバイト列)」。呼び出しが 1 件もなければ合格にしない |
| 対象外 | ApexPack が持たない機能(実行時に `ApexFeatureNotSupportedException` の機能名で分かる)、または `Classification.OutOfScope` の規則(MemoryPack 独自の型、MemoryPack 形式の不正データ、MemoryPack の内部メンバーの反射呼び出し、スキーマ変更への耐性など) |
| 仕様の違い | ApexPack が意図して違う動きをするもの(`Classification.DesignDifference`。例: `?` なしと宣言したメンバーの null は例外) |
| 失敗 | 上のどれにも当たらない ApexPack 側の失敗。直すべきもの |
| MemoryPack も失敗 | MemoryPack 側でも失敗(参考) |
