VSnap Pose Editor
=================

VSnap 用のポーズデータ（`Pose` / `PoseGroup` / `PoseLibrary`）を Unity Editor 上でまとめて作成し、AssetBundle 出力するためのツールです。

---

## 機能概要

**一つのウィンドウで操作**  
メニュー **`VSnap/Pose Editor`** で統合ウィンドウを開き、タブで「Create Poses」「Register Pose Group」「Build PoseLibrary」を切り替えて使います。

- **Pose 作成（Create Poses タブ）**
  - 指定フォルダ内の `AnimationClip` から `Pose` アセットを一括生成します。
  - 必要に応じて、キャラクターとカメラからサムネイル画像を自動撮影して `Pose` に設定できます。

- **PoseGroup 編集（Register Pose Group タブ）**
  - 指定フォルダ内の `Pose` アセットを検索し、既存または新規の `PoseGroup` にまとめて登録します。

- **PoseLibrary ビルド（Build PoseLibrary タブ）**
  - `PoseLibrary` アセットから、Android / iOS 向けの AssetBundle をビルドします。
  - `PoseLibrary.libraryName` を小文字化したものをベースに `{libraryName}.bundle` という名前で出力します。

---

## 1. Pose アセットの一括作成（Create Poses Folder）

1. **アニメーションを準備**
   - `Assets` 以下に、ポーズにしたい `AnimationClip` をフォルダ構成ごと配置します。
2. **ウィンドウを開く**
   - メニューから `VSnap/Create Poses Folder` を選択します。
3. **Target Folder を指定**
   - `Target Folder` に、`AnimationClip` が入っているフォルダを指定します（サブフォルダも再帰的に検索されます）。
4. **オプションを設定**
   - `Overwrite Existing Poses`  
     - 有効: 既存の `Pose` アセットを上書きします。  
     - 無効: 既に存在する `Pose` はスキップします。
   - `Generate Thumbnails`  
     - 有効にすると、以下を指定してサムネイルを自動生成します。
       - `Camera`: サムネイル撮影に使うカメラ
       - `Character`: ポーズを適用するキャラクター（`Animator` が付いているオブジェクト）
5. **Create Poses を実行**
   - `Create Poses` ボタンを押すと処理が始まります。
   - 指定フォルダと同じ階層に `{フォルダ名}_Poses` というフォルダが作成され、その中に元のフォルダ構成を保ったまま `Pose` アセットが生成されます。
6. **結果を確認**
   - ウィンドウ下部に作成された `Pose` のパス一覧が表示されます。

---

## 2. PoseGroup の作成・編集（Register Pose Group）

1. **ウィンドウを開く**
   - メニューから `VSnap/Pose Editor` を開き「Register Pose Group」タブを選択します。
2. **Target Folder を指定**
   - `Target Folder` に、`Pose` アセットが格納されているフォルダ（例: 手順 1 で生成した `{フォルダ名}_Poses`）を指定します。
3. **（任意）既存 PoseGroup を指定**
   - すでに作成済みの `PoseGroup` に追加したい場合は、`Target PoseGroup` にそのアセットを指定します。
   - 新しく作りたい場合は空のままで構いません。
4. **Pose の検索**
   - `Search Pose Objects` ボタンを押すと、`Target Folder` 配下の `Pose` を検索し、一覧表示します。
5. **Pose を PoseGroup に追加**
   - 検索結果が表示された状態で、下部のボタンを押します。
     - `Create New PoseGroup and Add Poses`  
       - `Target PoseGroup` が未指定の場合に表示されます。  
       - 保存先と名前を指定して新しい `PoseGroup` を作成し、検索結果の `Pose` をすべて追加します。
     - `Add to PoseGroup`  
       - `Target PoseGroup` が指定されている場合に表示されます。  
       - 指定した `PoseGroup` に検索結果の `Pose` を追加します（重複はスキップ）。

---

## 3. PoseLibrary の作成と AssetBundle ビルド

### 3-1. PoseLibrary アセットの作成

1. **PoseLibrary を作成**
   - `Project` ビューで右クリック → `Create/VSnap/PoseLibrary` を選択し、新しい `PoseLibrary` アセットを作成します。
2. **PoseGroup を登録**
   - `PoseLibrary` を選択し、インスペクターの `poseGroups` リストに、手順 2 で作成した `PoseGroup` をドラッグ＆ドロップで追加します。
3. **libraryName を設定**
   - `libraryName` フィールドに任意の名前を設定します。  
   - この値が小文字化されて、AssetBundle のファイル名に使用されます（例: `MyPoses` → `myposes.bundle`）。

### 3-2. PoseLibrary から AssetBundle をビルド

1. **ウィンドウを開く**
   - メニューから `VSnap/Build PoseLibrary` を選択します。
2. **PoseLibrary を指定**
   - `PoseLibrary` フィールドに、ビルド対象の `PoseLibrary` アセットを指定します。
3. **Output Path を設定**
   - `Output Path` に出力先フォルダを指定します。  
   - `Browse` ボタンからフォルダを選択することもできます。
4. **ビルド実行**
   - `Build AssetBundles (Android & iOS)` ボタンを押すと、`Android` / `iOS` 各プラットフォーム用の AssetBundle が出力されます。
   - 出力先は `Output Path/{libraryNameLower}/{Platform}/` となり、その中に `{libraryNameLower}.bundle` が生成されます。
5. **出力の確認**
   - ビルド完了後、`Output Path` がエクスプローラで開かれます。
