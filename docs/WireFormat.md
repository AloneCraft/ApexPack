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

### ApexGenerateType.Collection

件数 + 要素(コレクションと同じ形式)。型自身のメンバーは書かない。

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
- `Type` の値は型名で書く。信頼できない入力では `ApexSerializerOptions.TypeResolver` を指定しないと解決しない。

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
- ルートの `Deserialize<T>` は、固定長型を型ごとの static readonly(JIT が定数として扱う)で判定し、仮想呼び出しを経由せずに読む。
