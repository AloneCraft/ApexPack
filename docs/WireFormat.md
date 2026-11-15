# ApexPack ワイヤ形式 (v2)

ApexPack が書き出すバイト列の仕様と、実装が守る不変条件をまとめる。
生成コード(`src/ApexPack.Generator/CodeEmitter.cs`)と Core のフォーマッタはこの文書に一致させること。
バイト列はテスト(`tests/ApexPack.Tests/WireFormatTests.cs`、`EndiannessTests.cs`、`NullableSequenceTests.cs`)で固定している。

v1 の形式とは互換性がない。ペイロードにバージョン情報やヘッダーは付けない。

## 基本の符号化

| 対象 | 符号化 |
| --- | --- |
| 整数・浮動小数・char・enum | 固定幅のリトルエンディアン(LE)。enum は基になる整数型 |
| bool(単独・配列・固定長構造体の中) | 1 バイト。0 以外は true |
| DateTime / TimeSpan | 内部の 64 ビット値をそのまま 8 バイト |
| DateTimeOffset | 時計の ticks 8 バイト + オフセット分 2 バイト(計 10 バイト) |
| Guid | 16 バイト(`Guid.ToByteArray()` と同じ並び) |
| decimal | flags 4 / hi 4 / lo 8(各 LE、計 16 バイト) |
| nint / nuint | 8 バイト |
| Half, Int128, UInt128, Rune, DateOnly, TimeOnly, Complex, Vector2/3/4, Quaternion, Plane, Matrix3x2/4x4 | 構成要素ごとに LE |

### CompactUInt(長さ・件数・タグ)

| 値 | バイト列 |
| --- | --- |
| 0 〜 0xFC | 1 バイト |
| 0xFD 〜 0xFFFF | `0xFD` + u16 LE |
| 0x10000 〜 0xFFFFFFFF | `0xFE` + u32 LE |
| `0xFF` で始まる | 不正(拒否する) |

### 文字列

`CompactUInt(((長さ << 1) | wide) + 1)`。0 は null、1 は空文字列。

- 全文字が U+00FF 以下: wide = 0、1 バイト/文字(Latin1)
- それ以外: wide = 1、UTF-16LE

### コレクション

`CompactUInt(件数 + 1)` の後に要素。0 は null。
配列・List・Queue・HashSet・Immutable 系・各インターフェース型など、要素の並びを持つ型は同じ形式。

- 辞書: 件数の後に key, value の組を並べる
- Stack / ConcurrentStack / ImmutableStack: 列挙順(上から)に書き、読むときに逆順で積む
- 多次元配列: `CompactUInt(d0 + 1)`、`CompactUInt(d1)`… の後に行優先の要素
- 固定長要素で LE 機のメモリ表現が正規形と一致する場合は一括コピーする(結果のバイト列は要素ごとに書いた場合と同じ)

### Nullable 要素の列(`T?[]`、`List<T?>`)

`CompactUInt(件数 + 1)`、存在ビットマップ(`(件数 + 7) / 8` バイト、要素 i はバイト i/8 のビット i%8)、存在する値だけを順に。
ビットマップの余りビット(件数を超える位置)が立っていたら不正。

## [ApexPackable] 型

### クラス・構造体(一般形)

```
[マスク][固定長領域][Nullable 固定長の値][可変長メンバー(宣言順)]
```

- **マスク**: 次のメンバーに 1 ビットずつ、宣言順に割り当てる。必要なバイト数ちょうど(LE のビット順)。
  - bool: 値そのもの
  - `Nullable<T>`: 値の有無
  - null を取り得る参照型(`?` 付き、または nullable 無効の文脈): null でなければ 1
- **非 null の印**: マスクを持つ**クラス**は bit0 を常に 1 にする。そのためマスクの先頭バイトは 0 にならない。
- **固定長領域**: 固定長メンバー(スカラー、固定長の [ApexPackable] 構造体)を宣言順に詰める。パディングなし。
- **Nullable 固定長の値**: 値があるものだけを宣言順に詰める。
- **可変長メンバー**: 文字列・コレクション・他の型など。null を取り得る参照型は、マスクのビットが 1 のときだけ本体を書く。
- `?` なしの参照型メンバーは null 情報を書かない。実行時に null なら書き込みで例外になる。

### null を取り得る単独の位置(ルート、`List<T?>` の要素、辞書の値など)

| 型 | null | 非 null |
| --- | --- | --- |
| マスクを持つクラス | `0x00` | 本体そのもの(先頭は印が立ったマスク) |
| マスクを持たないクラス | `0x00` | `0x01` + 本体 |
| 構造体 | (null なし) | 本体 |

`?` なしの要素(`List<Person>` など)は印やタグを書かず、本体だけを並べる。

### 固定長構造体

全メンバーが固定長の構造体は、メンバーを宣言順にパディングなしで詰めた固定長の値になる。
次の条件をすべて満たすときは、LE 機でメモリをそのまま一括コピーする。結果のバイト列は詰めた形と同じになる。

- 宣言が 1 か所(partial で分割していない)
- `StructLayout` 属性がない
- フィールドの並びと直列化するメンバーの並びが一致する
- `Unsafe.SizeOf<T>()` が詰めたサイズと等しい(パディングなし)
- 入れ子の構造体も同じ条件を満たす

### Union(抽象クラス・インターフェース)

`CompactUInt(タグ + 1)` の後に、具象型の本体を null 表現なしで書く。0 は null。

### 参照追跡(`PreserveReferences` / `SerializeWithReferences`)

追跡が有効なとき、クラスの位置ごとに次の `CompactUInt` を書く。

| 値 | 意味 |
| --- | --- |
| 0 | null |
| 1 | 新しいインスタンス。続けて本体を書く。ID は出現順に 0, 1, 2… と暗黙に振る |
| n ≥ 2 | ID (n − 2) への後方参照 |

追跡を始める位置:

- **ルート**: ルートの型が参照保持の型を含むなら、ルートから追跡する。「含む」とは、自分が `PreserveReferences` であるか、メンバー(配列の要素・ジェネリック引数・Union の case を含む)をたどって `PreserveReferences` の型に届くこと。生成コードはこの結果を `static bool ApexPreserveReferences` に出し、`List<Node>` などのルートは実行時に要素・型引数をたどって判定する。共有・循環の参照は、直列化全体で 1 つの表で扱う(リストの要素同士や、別のメンバーの間でも同じインスタンスになる)。
- **途中**: 追跡なしで書いている途中で `PreserveReferences` の型に入った場合(コンパイル時に分からない、実行時に登録した型の中など)は、その型の位置から部分木の追跡を始める。読み取りも同じ位置で始めるので、形式は一致する。

### ApexGenerateType.Collection

件数 + 要素(コレクションと同じ形式)。型自身のメンバーは書かない。

### 実行時の登録

- `ApexFormatterRegistry.RegisterGeneric(typeof(Box<>), typeof(BoxFormatter<>))`: 開いたジェネリック型にフォーマッタを一括登録する。形式は登録したフォーマッタのもの。`RegisterGenericFactory` は閉じた型を受け取ってフォーマッタを作る。
- `ApexDynamicUnionFormatter<T>`: 基底型に `[ApexUnion]` を付けられないときの Union。形式は `[ApexUnion]` と同じ(`CompactUInt(タグ + 1)` + 具象型の本体、0 は null)。`Add` / `Remove` で実行中に対応を変えられる。`ApexDynamicUnion.RegisterGeneric(typeof(IUnion<>), (0, typeof(Case<>)))` は、開いたジェネリックの基底型と具象型を、同じ型引数で閉じて対応づける。
- 登録表は `ConditionalWeakTable` か、型ごとの `ApexFormatterCache<T>` に置く(型を強く握らない)。

### 低レベル API(`ApexLowLevelExtensions`)

手書きのフォーマッタ向け。

| 操作 | 形式 |
| --- | --- |
| `WriteObjectHeader(n)`(n ≤ 254) | 1 バイト `n + 1` |
| `WriteNullObjectHeader()` / `PeekIsNull()` | 1 バイト `0` / 次のバイトが 0 か(読み進めない) |
| `WriteCollectionHeader(n)` / `TryReadCollectionHeader` | コレクションと同じ `CompactUInt(n + 1)`、null は 0 |
| `WriteVarInt(long)` / `ReadVarInt()` | ZigZag 符号化した値を 7 ビットずつ、下位から(続きがあれば最上位ビット 1)。最大 10 バイト |
| `WriteVarUInt(ulong)` / `ReadVarUInt()` | 符号なしの値を 7 ビットずつ、下位から |

## MemoryPack 互換層(`src/ApexPack.MemoryPackCompat`)

MemoryPack と同じ名前の API(`namespace MemoryPack`)を ApexPack の上に実装する。ワイヤ形式は ApexPack のもので、MemoryPack とのバイト列の互換はない。

- ジェネレーターは、このアセンブリの `[MemoryPackable]` などを ApexPack の属性と同じ意味に読む(本物の MemoryPack の属性には反応しない)。`GenerateType.CircularReference` → `PreserveReferences`、`Collection` → `ApexGenerateType.Collection`、`NoGenerate` → 生成しない。
- `[MemoryPackUnionFormatter(typeof(IUnion))]` + `[MemoryPackUnion]` は、実行時 Union として自動登録する。
- コールバックは MemoryPack の規則で呼ぶ(宣言順。直列化では static は値が null でも呼び、インスタンスメソッドは値があるときだけ。新しく作る逆直列化では `OnDeserializing` は static だけ)。writer / reader を受け取るコールバックが読み書きするデータは、本体の前後に長さ付きの塊として置く(その段階に該当するコールバックがある型だけ)。
- 低レベル API の `MemoryPackWriter` / `MemoryPackReader` は ApexPack の writer / reader として動き、ヘッダーと可変長整数は上の表の形式で書く(`MemoryPackCode.NullObject` は 0)。可変長整数は型によらず符号つき(ZigZag)で書くので、書いた型と違う `ReadVarIntXxx` でも読める。
- ApexPack が持たない機能は、機能名つきの `ApexFeatureNotSupportedException` を投げる: VersionTolerant と `SuppressDefaultInitialization`(スキーマ変更への耐性)、既存インスタンスへの上書き(`Deserialize` の `ref` 引数に値が入っている)、組み込み属性フォーマッタ(`[BrotliFormatter]` など)、Brotli 圧縮、`MemoryPackSerializerOptions.Utf16`、使った後のフォーマッタ差し替え。
- MemoryPack 公式テストでの比較: `tests/Upstream.Runner`(コマンドライン)と、`benchmarks/ApexPack.BenchmarkGui` の「MemoryPack テスト比較」タブ。手順と分類は `tests/MemoryPack.Upstream/UPSTREAM.md`。

## エンディアン

ワイヤ形式は LE で固定する。

- 書き込み・読み取りの各処理は `TWriter.IsHostLittleEndian` / `TReader.IsHostLittleEndian` で分岐する。これは JIT 時の定数なので、LE 機ではバイト反転の経路がコードごと消え、実行時の判定コストはかからない。
- BE 機では反転経路を通る。`ApexBigEndianSimWriter` / `ApexBigEndianSimReader` を使うと、反転経路を LE 機で検証できる。
- 反転経路の出力は、正規形の各数値フィールドをバイト反転したものになる。
- `[ApexPackable]` でない unmanaged 構造体は、メモリをそのままコピーする。構造が分からないので反転できず、エンディアンをまたいだ互換性はない。この場合はコンパイル時に警告 APX004 を出す。

## 安全性(信頼できない入力)

- 件数・長さは、残りバイト数と要素の最小サイズを比べて、配列を割り当てる前に検査する。
  - クラス要素の最小サイズは 1 バイトとして扱う(null や後方参照があり得るため)
- `DeserializeSafe` と `ApexSerializerOptions.Security` では、深さ・1 コレクションの件数・総要素数に上限を設ける。
- `Type` の値は型名(`AssemblyQualifiedName` から各アセンブリ名の Version / Culture / PublicKeyToken を除いたもの。例: `System.String, System.Private.CoreLib`)で書く。読み取りは元の長い名前でも解決できる。信頼できない入力では `ApexSerializerOptions.TypeResolver` を指定しないと解決しない。
- 書き込み側: 参照保持なしで循環したグラフを書くと再帰が終わらない。参照型のメンバーを持つクラスは、本体を書く前にスタックの残りを確かめ(`RuntimeHelpers.TryEnsureSufficientExecutionStack`)、足りなければ `ApexSecurityException` にする(スタックあふれでプロセスが落ちるのを防ぐ)。

## アンロード可能性(不変条件)

ApexPack.Core は、利用者の型や `Type` を強く参照する非ジェネリックな static 状態を持たない。

- 型ごとの情報は `ApexFormatterCache<T>` などのジェネリック static にだけ置く。collectible な型で閉じたジェネリック型の static は、その型と一緒に解放される。
- 共有の配列プールへ返す配列に利用者のオブジェクトを残さない(返す前にクリアする)。
- スレッドローカルに保持するのはバイト列だけ。
- 検証: `tests/ApexPack.Tests/UnloadabilityTests.cs`。プラグインを collectible な `AssemblyLoadContext` に読み込み、全経路を通した後に解放されることを確かめる。陰性対照として、static に型を握らせると解放されないことも確かめる。

## 速度上の工夫

- 書き込みは、固定長領域(マスク + 固定長 + Nullable 固定長)を 1 回の確保で書く。Nullable 固定長の値は、常に現在位置へ書き、値があるときだけ位置を進める。
- 読み取りは、マスクと固定長の範囲を 1 回の境界検査で確かめる。
- 文字列の Latin1 縮小は、SIMD で縮小しながら U+00FF を超える文字を検出する 1 パスで行う。
- Nullable 要素の列(値が 4 / 8 バイト、LE、AVX2)は、`vpermd`(並べ替え命令)で詰める・展開する。それ以外はスカラーで処理する。両者のバイト列は同じ。
- ルートの `Deserialize<T>` は、固定長型を型ごとの static readonly(JIT が定数として扱う)で判定し、仮想呼び出しを経由せずに読む。`Serialize<T>` も同じく、固定長型は正確な大きさの配列へ直接書く。
- ルートの直列化は、書くバイト数が安く分かる値(要素が固定長の配列・List、可変長メンバーのない型)では正確な大きさの配列へ直接書き、作業バッファからの写しを省く。要素をメモリからそのまま書ける 1 次元配列(`int[]` など)は、件数ヘッダー + 一括コピーで書く。
- 作業バッファ(`ApexBufferWriter`)は、足りなくなったら書き終えた分をそのまま残して次のバッファへ進む。それまでの内容を写さないので、大きな出力でも写しは最後の 1 回だけ。
- ValueTuple は、要素がすべて固定長なら固定長として扱い、メモリ表現が一致すれば一括コピーする。ジェネリック構造体は、型引数のメンバーが実行時に固定長なら構造体全体を固定長として扱う(形式は変わらない)。
- 生成コードは、固定長メンバーをローカル変数に取らずに直接読み書きし、メモリ上でもワイヤと同じ順にすき間なく並ぶ連続したフィールド(型ごとに 1 回調べる)は一括コピーする。
- 実行時に解決するフォーマッタ(型引数のメンバー、実行時に登録した型)は、固定長なら固定長の経路を使い、よく使う writer / reader の組み合わせでは非ジェネリックな仮想メソッドを呼ぶ(ジェネリックな仮想メソッドは呼び出しごとに実行時の検索が要るため)。
- 参照追跡の作業配列はスレッドごとに使い回し、書き込みでは「オブジェクト → ID」、読み取りでは「ID → オブジェクト」の表だけを使う。
