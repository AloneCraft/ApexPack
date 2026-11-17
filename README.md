# ApexPack

.NET 向けのバイナリシリアライザーです。

- [C# ファイル1本の実行例](docs/examples/QuickStart.cs)
- [性能とデータサイズのインフォグラフィック](docs/performance.md)
- [ワイヤ形式](docs/WireFormat.md)

.NET 10 SDK 以降の環境で、チェックアウトのルートから実行例を試せます。

```sh
dotnet run --file docs/examples/QuickStart.cs
```

## ドキュメントの公開

GitHub リポジトリの **Settings → Pages → Build and deployment → Source** で **GitHub Actions** を選択します。既定ブランチへ push すると、[ドキュメントのワークフロー](.github/workflows/docs.yml) が実行例を確認し、Docfx で生成したサイトを GitHub Pages に公開します。Actions タブから手動実行することもできます。

ローカルでは .NET 10 SDK 以降で次のコマンドを実行すると、`docs/_site` に生成したサイトをプレビューできます。

```sh
dotnet tool install --global docfx --version 2.81.0
docfx build docs/docfx.json --serve
```
