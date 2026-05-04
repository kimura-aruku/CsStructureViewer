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

- [ ] `MainViewModel` の実装（OpenProjectCommand / IsAnalyzing / CancelCommand / LayoutResult）
- [ ] `SettingsViewModel` の実装（除外パターンリストのCRUD）

## フェーズ9: View層の実装

- [ ] `MainWindow` の実装（メニューバー、GraphCanvas配置、起動時案内メッセージ）
- [ ] 解析中オーバーレイの実装（スピナー＋キャンセルボタン）
- [ ] `SettingsWindow` の実装（除外設定リスト、+/-ボタン）

## フェーズ10: 結合・動作確認

- [ ] 各層の結合と動作確認
- [ ] 実際のC#プロジェクトで解析・表示の確認
- [ ] 実際のUnityプロジェクトで解析・表示の確認
- [ ] レイアウト・表示の調整
