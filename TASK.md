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
- [x] 実際のUnityプロジェクトで解析・表示の確認

## フェーズ11: 機能改善（仕様v2）

### 11.1 Settings層の更新
- [x] `AppSettings` に `InternalExcludePatterns` プロパティを追加
- [x] `SettingsManager` のデフォルト値生成を更新（`InternalExcludePatterns` は空リスト）

### 11.2 Layout層の更新
- [x] `LayoutEngine.Calculate()` にキャンバス最大幅（`canvasMaxWidth`）を引数として追加
- [x] `LayoutResult` に `FolderNamespaces`（フォルダ矩形対象の名前空間集合）を追加
- [x] `ArrowRoute` を折れ線対応（`IReadOnlyList<Point> Waypoints`）に変更
- [x] フォルダ矩形レイアウト計算を実装（内部依存除外パターンに一致する名前空間を検出）
- [x] オルソゴナル矢印ルーティングを実装（直角折れ線、矩形回避）
- [x] 矢印スプレッド処理を実装（同一辺通過の矢印を均等にずらす）

### 11.3 Rendering層の更新
- [x] フォルダ矩形の描画を実装（点線枠、グレー半透明、ラベル）
- [x] 矢印描画を折れ線（`PolyLineSegment`）対応に変更

### 11.4 ViewModel層の更新
- [x] `MainViewModel` に `RefreshCommand` を追加
- [x] `MainViewModel` に `LastFolderPath` を追加
- [x] `MainViewModel` に `ShowRefresh` プロパティを追加
- [x] `LayoutEngine.Calculate()` 呼び出し時にウィンドウ幅を渡す

### 11.5 View層の更新
- [x] `MainWindow` に「更新」ボタンを追加（ビューワー左上、メニューバー下）
- [x] `SettingsWindow` に内部依存除外パターンのリストUIを追加（ラベル変更含む）

## フェーズ12: 修正タスク

### 12.1 矢印線の矩形回避ロジック改善

**問題:** 矢印線がクラス矩形と重なる。

**修正方針:**
- `LayoutEngine` でレイアウト計算を2ステップに分離する
  1. 全名前空間矩形・クラス矩形を先にすべて確定させる
  2. 矢印線生成時に、確定済みの全矩形リストを受け取り、線がいずれの矩形の内側も通過しないよう経路を決定する
- 矩形回避の判定仕様:
  - 矩形は点ではなく「左上・右下の2点で定まる矩形領域」として扱う
  - 線分がその矩形領域内を通過する場合は「当たり」とみなす
  - 当たりが発生した場合は、矩形の外側を迂回するよう中間ウェイポイントを追加する
- 対象矩形: クラス矩形（`ClassRects`）および名前空間矩形（`NamespaceRects`）の全要素
  - ただし矢印の始点・終点が属する矩形自身は回避対象から除外する

- [x] `LayoutEngine` の矢印ルーティングを2ステップ構成に変更（矩形確定後にルーティング実行）
- [x] 線分と矩形の交差判定ロジックを実装（矩形を面積として扱う）
- [x] 交差が検出された場合の迂回ウェイポイント追加ロジックを実装
- [x] 始点・終点所属矩形を回避対象から除外する処理を追加

### 12.2 内部依存除外パターンをフォルダ名指定に変更

**問題:** 現在は名前空間名でパターンを指定しているが、フォルダ名で指定したい。

**修正方針:**
- 解析時（`ProjectAnalyzer`）にファイルパスからフォルダ名を判定し、`InternalExcludePatterns` に一致するフォルダ配下の `.cs` ファイルに含まれるクラス・名前空間を「内部依存除外対象」としてマークする
- マーク方法: `ProjectGraph` または `NamespaceNode` に「フォルダ除外フラグ」を持たせるか、`LayoutEngine` に渡す前に該当名前空間リストを別途計算して渡す
- パターン一致の仕様: ファイルパスの各フォルダ名コンポーネントのいずれかがパターンと完全一致すれば対象

- [x] `ProjectAnalyzer` でファイルパスのフォルダ名コンポーネントを抽出し、`InternalExcludePatterns` との一致を判定するロジックを実装
- [x] 一致したフォルダ配下のファイルに属するクラスを「内部依存除外対象」としてマークする仕組みを追加（`NamespaceNode.IsInternal` フラグ等）
- [x] `LayoutEngine` でフォルダ名ベースのマーク情報を参照してフォルダ矩形を判定するよう変更
- [x] `SettingsWindow` の内部依存除外パターンのラベル・説明文を「フォルダ名で指定」に更新

### 12.3 内部クラスの表示廃止

**方針:** ネストクラス（内部クラス）の表示機能を廃止する。

- [x] 内部クラスの表示を廃止

### 12.4 矢印線の重なりと終端クリアランス改善

**問題:** DebugProject表示時に、Dog・Cat・AnimalService から出る矢印線の一部が重なり、短い終端線分により矢印記号と隣接線分が重なる。

**修正方針:**
- 同一直線上に重なる候補がある場合、非ゼロのレーンへ迂回して線分の完全重なりを避ける
- 矢印終端の手前に、矢印記号分の幅または高さを確保する
- 直線接続で終端クリアランスを確保できない場合は、終端手前に迂回点を追加する

- [x] specification.md に矢印終端クリアランスの仕様を追記
- [x] `LayoutEngine` の水平・垂直接続ルート生成を修正
- [x] 同一直線上の矢印が重なる場合のレーン迂回候補を追加
