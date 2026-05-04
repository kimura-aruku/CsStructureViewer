# タスクリスト

## フェーズ1: プロジェクト初期セットアップ

- [x] WPF + .NET 10 プロジェクトの作成
- [x] NuGetパッケージの追加（Microsoft.CodeAnalysis.CSharp）
- [x] architecture.md に基づくフォルダ構成の作成

## フェーズ2: Models層の実装

- [x] `ClassNode` の実装
- [x] `NamespaceNode` の実装
- [x] `DependencyEdge` / `DependencyKind` の実装
- [x] `ProjectGraph` の実装

## フェーズ3: Settings層の実装

- [x] `AppSettings` の実装（除外パターン一覧）
- [x] `SettingsManager` の実装（JSON読み書き、デフォルト値生成）

## フェーズ4: Analysis層の実装

- [x] `NamespaceResolver` の実装（namespace宣言の抽出）
- [x] `ClassAnalyzer` の実装（クラス・継承・実装・フィールド参照の抽出）
- [x] `partial class` の名寄せ処理
- [x] `ProjectAnalyzer` の実装（ファイル列挙・除外・非同期・CancellationToken）

## フェーズ5: Helpers層の実装

- [x] `ColorPalette` の実装（HSL色空間での名前空間カラー自動割り当て）

## フェーズ6: Layout層の実装

- [x] `LayoutResult` の実装（ClassRects / NamespaceRects / ArrowRoutes）
- [x] `LayoutEngine` の実装（6ステップのレイアウト計算）

## フェーズ7: Rendering層の実装

- [x] `GraphCanvas` の基盤実装（Canvas入れ子構造、LayoutResult受け取り）
- [x] 名前空間矩形の描画（半透明塗りつぶし、枠上テキスト）
- [x] クラス矩形の描画（可変サイズ、最大幅折り返し、ネストクラス）
- [x] UML矢印の描画（汎化・実現・関連の3種）
- [x] ズーム操作の実装（ホイール + ScaleTransform）
- [x] パン操作の実装（左クリック+ドラッグ + TranslateTransform）

## フェーズ8: ViewModel層の実装

- [x] `MainViewModel` の実装（OpenProjectCommand / IsAnalyzing / CancelCommand / LayoutResult）
- [x] `SettingsViewModel` の実装（除外パターンリストのCRUD）

## フェーズ9: View層の実装

- [x] `MainWindow` の実装（メニューバー、GraphCanvas配置、起動時案内メッセージ）
- [x] 解析中オーバーレイの実装（スピナー＋キャンセルボタン）
- [x] `SettingsWindow` の実装（除外設定リスト、+/-ボタン）

## フェーズ10: 結合・動作確認

- [x] 各層の結合と動作確認
- [x] 実際のC#プロジェクトで解析・表示の確認
- [ ] 実際のUnityプロジェクトで解析・表示の確認
- [ ] レイアウト・表示の調整

## フェーズ11: 機能改善（仕様v2）

### 11.1 Settings層の更新
- [ ] `AppSettings` に `InternalExcludePatterns` プロパティを追加
- [ ] `SettingsManager` のデフォルト値生成を更新（`InternalExcludePatterns` は空リスト）

### 11.2 Layout層の更新
- [ ] `LayoutEngine.Calculate()` にキャンバス最大幅（`canvasMaxWidth`）を引数として追加
- [ ] `LayoutResult` に `FolderNamespaces`（フォルダ矩形対象の名前空間集合）を追加
- [ ] `ArrowRoute` を折れ線対応（`IReadOnlyList<Point> Waypoints`）に変更
- [ ] フォルダ矩形レイアウト計算を実装（内部依存除外パターンに一致する名前空間を検出）
- [ ] オルソゴナル矢印ルーティングを実装（直角折れ線、矩形回避）
- [ ] 矢印スプレッド処理を実装（同一辺通過の矢印を均等にずらす）

### 11.3 Rendering層の更新
- [ ] フォルダ矩形の描画を実装（点線枠、グレー半透明、ラベル）
- [ ] 矢印描画を折れ線（`PolyLineSegment`）対応に変更

### 11.4 ViewModel層の更新
- [ ] `MainViewModel` に `RefreshCommand` を追加
- [ ] `MainViewModel` に `LastFolderPath` を追加
- [ ] `MainViewModel` に `ShowRefresh` プロパティを追加
- [ ] `LayoutEngine.Calculate()` 呼び出し時にウィンドウ幅を渡す

### 11.5 View層の更新
- [ ] `MainWindow` に「更新」ボタンを追加（ビューワー左上、メニューバー下）
- [ ] `SettingsWindow` に内部依存除外パターンのリストUIを追加（ラベル変更含む）
