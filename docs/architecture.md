# CsStructureViewer アーキテクチャ設計

## 1. 全体方針

- UIパターン: **MVVM**（Model / ViewModel / View）
- 解析・レイアウト・描画を明確に分離し、各層を独立して開発・テストできる構造とする
- 解析処理は非同期（`async/await` + `CancellationToken`）で実行し、UIをブロックしない

---

## 2. プロジェクト構成

```
CsStructureViewer/
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
│
├── Models/                        # データモデル（Pure C#、WPF非依存）
│   ├── ClassNode.cs               # クラス情報
│   ├── NamespaceNode.cs           # 名前空間情報
│   ├── DependencyEdge.cs          # 依存関係（エッジ）
│   └── ProjectGraph.cs            # 解析結果グラフ全体
│
├── Analysis/                      # Roslynによるコード解析
│   ├── ProjectAnalyzer.cs         # 解析エントリポイント（非同期）
│   ├── ClassAnalyzer.cs           # クラス・依存関係の抽出
│   └── NamespaceResolver.cs       # namespace宣言の解決
│
├── Layout/                        # レイアウト計算（座標・サイズ算出）
│   ├── LayoutEngine.cs            # レイアウト計算エントリポイント
│   └── LayoutResult.cs            # 計算済みの座標・サイズ情報
│
├── Rendering/                     # WPF描画コントロール
│   └── GraphCanvas.cs             # Canvas派生の描画コントロール
│
├── ViewModels/
│   ├── MainViewModel.cs           # メイン画面のViewModel
│   └── SettingsViewModel.cs       # 設定画面のViewModel
│
├── Views/
│   ├── SettingsWindow.xaml        # 設定画面
│   └── SettingsWindow.xaml.cs
│
├── Settings/
│   ├── AppSettings.cs             # 設定データモデル
│   └── SettingsManager.cs         # 設定ファイルの読み書き
│
└── Helpers/
    └── ColorPalette.cs            # 名前空間カラー自動割り当て
```

---

## 3. データフロー

```
[ユーザー操作: フォルダ選択]
        ↓
  ProjectAnalyzer          # .csファイルを列挙してRoslynで解析
        ↓
  ProjectGraph             # ClassNode / NamespaceNode / DependencyEdge のグラフ
        ↓
  LayoutEngine             # 座標・サイズを計算
        ↓
  LayoutResult             # 全ノードの確定した Rect と矢印ルート
        ↓
  GraphCanvas              # WPF Canvas に UIElement として描画
        ↓
[ユーザー操作: ズーム/パン] # RenderTransform を更新するだけ（再描画なし）
```

---

## 4. 各層の設計

### 4.1 Models層

```csharp
// クラス情報
class ClassNode {
    string Name;
    string FullyQualifiedName;
    string NamespaceName;        // null = グローバル名前空間
    bool IsPartial;
    bool IsInterface;
    List<ClassNode> NestedClasses;
}

// 名前空間情報
class NamespaceNode {
    string Name;
    List<ClassNode> Classes;
}

// 依存関係
class DependencyEdge {
    ClassNode Source;
    ClassNode Target;
    DependencyKind Kind;         // Inheritance / Implementation / FieldReference
}

enum DependencyKind { Inheritance, Implementation, FieldReference }

// グラフ全体
class ProjectGraph {
    List<NamespaceNode> Namespaces;
    List<ClassNode> GlobalClasses;   // namespace なしのクラス
    List<DependencyEdge> Edges;
}
```

### 4.2 Analysis層（Roslyn）

- `ProjectAnalyzer` が対象フォルダ配下の `.cs` ファイルを列挙し、各ファイルを `CSharpSyntaxTree.ParseText()` で解析
- `ClassAnalyzer` が各ファイルのSyntax Treeを走査し、以下を抽出：
  - `TypeDeclarationSyntax`（class / interface / struct）→ `ClassNode`
  - `NamespaceDeclarationSyntax` / `FileScopedNamespaceDeclarationSyntax` → `NamespaceNode`
  - `BaseListSyntax` → 継承・実装の `DependencyEdge`
  - `FieldDeclarationSyntax` / `PropertyDeclarationSyntax` の型 → フィールド参照の `DependencyEdge`
- `partial class` は全ファイル解析後にクラス名で名寄せして1つの `ClassNode` に統合
- 除外設定に該当するパス（フォルダ名・ファイル名）はファイル列挙時にスキップ
- `CancellationToken` を受け取り、キャンセル時に解析を中断

### 4.3 Layout層

計算は純粋関数的に実装し、Modelの座標を直接変更しない（`LayoutResult`に書き出す）。

**計算手順：**

1. **クラス矩形のサイズ計算**  
   クラス名の文字数から幅を算出し、最大幅を超えた場合は折り返して高さを計算

2. **名前空間内クラスの配置**  
   クラス矩形を横方向に並べ、名前空間最大幅を超えたら折り返す（minPaddingを考慮）

3. **名前空間矩形のサイズ計算**  
   内包するクラス矩形の総面積＋paddingで名前空間矩形サイズを決定

4. **名前空間矩形の配置**  
   名前空間矩形を左上から横方向に並べ、Canvasの最大幅を超えたら折り返す

5. **グローバル名前空間クラスの配置**  
   名前空間矩形群の後ろに続けて配置

6. **矢印ルートの計算**  
   依存元・依存先の矩形の中心から最短のボーダー点を結ぶ直線を算出

```csharp
class LayoutResult {
    Dictionary<ClassNode, Rect> ClassRects;
    Dictionary<NamespaceNode, Rect> NamespaceRects;
    List<ArrowRoute> Arrows;       // 始点・終点・矢印種別
}
```

### 4.4 Rendering層（GraphCanvas）

- WPF `Canvas` を継承したカスタムコントロール
- `LayoutResult` を受け取り、`Border`・`TextBlock` などの `UIElement` を Canvas 上に配置
- ネストクラスは親クラス矩形内に子 `Border` を配置
- 矢印は `Path`（`LineGeometry` / `PathGeometry`）で描画
- 名前空間矩形の枠上テキストは `Canvas.SetLeft/Top` で矩形枠上に重ねて配置

**ズーム・パン：**

```
Canvas（外側：クリップ領域）
└── Canvas（内側：RenderTransform対象）
      ├── 名前空間矩形群（Border）
      ├── クラス矩形群（Border + TextBlock）
      └── 矢印群（Path）
```

- ホイール → 内側CanvasのScaleTransformをマウス位置基準で更新
- 左クリック+ドラッグ → TranslateTransformを更新

### 4.5 Settings層

**設定ファイルの保存場所：**
```
%AppData%\Roaming\CsStructureViewer\settings.json
```

**設定データ構造：**
```csharp
class AppSettings {
    List<string> ExcludePatterns;   // 除外パターン一覧
}
```

デフォルト除外パターン: `bin`, `obj`, `.git`, `Editor`, `Temp`, `temp`, `Tests`

**SettingsManager：** 起動時に読み込み、設定保存時に書き込む。ファイル未存在時はデフォルト値で生成。

---

## 5. 色の自動割り当て（ColorPalette）

- HSL色空間でHue（色相）を名前空間数で等分割して割り当てる
- Saturation: 50〜60%、Lightness: 60〜70% で視認性を確保
- 名前空間矩形: 上記色を半透明（Alpha 60〜80）で塗りつぶし
- クラス矩形: 同じHueで Lightness をやや下げた色（不透明）で塗りつぶし

---

## 6. 非同期処理・進捗表示

```
MainViewModel
├── OpenProjectCommand        # フォルダ選択ダイアログ → ProjectAnalyzer呼び出し
├── IsAnalyzing (bool)        # スピナー表示フラグ
├── CancelCommand             # CancellationTokenSource.Cancel()
└── LayoutResult              # 解析・レイアウト完了後に GraphCanvas へバインド
```

- 解析中はメインウィンドウ上にスピナーとキャンセルボタンをオーバーレイ表示
- 解析完了後に `LayoutEngine.Calculate()` を実行し `LayoutResult` を更新

---

## 7. 未定義事項（実装時に決定）

| 項目 | 候補・方針 |
|------|-----------|
| ジェネリッククラスの表示 | `List<T>` → `List<T>` をそのままクラス名として表示する方向で検討 |
| クラス矩形の最大幅 | 定数として定義（例: 200px）。将来的に設定可能にする可能性あり |
| 矢印の重なり対策 | 初期実装では直線のみ。将来的にベジェ曲線等で回避 |
| 大規模プロジェクトへの対応 | 初期実装では件数制限なし。パフォーマンス問題が出たら対処 |
