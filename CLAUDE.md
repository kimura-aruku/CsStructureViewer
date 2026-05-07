# プロジェクト設定

- **project_management_mode**: `full`

# CsStructureViewer

## プロジェクト概要

C# および Unity プロジェクト向けの依存関係可視化デスクトップアプリケーション。
クラスや名前空間の依存関係を解析・グラフ表示することで、コードの構造把握を支援する。

## 基本方針

- 対象: C# / Unity プロジェクトのソースコード
- 形態: デスクトップアプリケーション（Windows）
- 主な利用者: C# / Unity 開発者

## ビルド・リリース手順

修正作業が完了したら、必ず以下のコマンドで `publish/` フォルダの `.exe` を更新すること。

```bash
dotnet publish -c Release
```

- 出力先: `publish/`（csproj の `<PublishDir>` で固定）
- フレームワーク依存形式（self-contained ではない）
- `CsStructureViewer.exe` が起動中で `publish/` 配下のファイルがロックされている場合は、該当プロセスを終了してから再度 `dotnet publish -c Release` を実行する
- ビルド成功を確認してからコミットする
