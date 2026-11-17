# C# ファイル1本で始める

.NET 10 SDK 以降のファイルベースアプリとして、[QuickStart.cs](examples/QuickStart.cs) を実行できます。`#:project` は同じチェックアウト内の `ApexPack.Core` を参照します。チェックアウトのルートで、次のコマンドを実行してください。

```sh
dotnet run --file docs/examples/QuickStart.cs
```

[!code-csharp[](examples/QuickStart.cs)]

```text
Payload: 14 bytes
Restored: 10, null, 30, 40
```

`Serialize` は配列を `byte[]` に書き出します。`Deserialize<int?[]>` には復元したい型を指定します。この例では、整数と null の位置が元の配列と一致することを確認しています。

[Microsoft Learn のファイルベースアプリの解説](https://learn.microsoft.com/ja-jp/dotnet/core/sdk/file-based-apps)
