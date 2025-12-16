using UnityEditor;
using UnityEngine;
using System.IO;
using VSnap.Shared.Domain;


/// <summary>
/// PoseLibraryのAssetBundleをビルドするエディタウィンドウ
/// poseLibralyの名前を小文字化したものをアセットバンドル名とする。
/// 例: libraryNameが"MyPoses"なら、アセットバンドル名は"myposes.bundle"
/// ビルド対象プラットフォームはAndroidとiOS。
/// ビルド後、出力フォルダを開く。
/// </summary>
namespace VSnap.Editor
{
    public class PoseLibraryBundleBuilder : EditorWindow
    {
        private PoseLibrary poseLibrary;
        private string outputPath = "AssetBundles";
        
        [MenuItem("VSnap/Build PoseLibrary")]
        public static void ShowWindow()
        {
            var window = GetWindow<PoseLibraryBundleBuilder>("PoseLibrary Bundle Builder");
            window.Show();
        }
        
        private void OnGUI()
        {
            GUILayout.Label("PoseLibrary AssetBundle Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // PoseLibraryの選択
            poseLibrary = (PoseLibrary)EditorGUILayout.ObjectField(
                "PoseLibrary", 
                poseLibrary, 
                typeof(PoseLibrary), 
                false
            );
            
            EditorGUILayout.Space();
            
            // 出力パスの設定
            EditorGUILayout.BeginHorizontal();
            outputPath = EditorGUILayout.TextField("Output Path", outputPath);
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                string selectedPath = EditorUtility.SaveFolderPanel(
                    "Select Output Folder", 
                    outputPath, 
                    ""
                );
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    outputPath = selectedPath;
                }
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            
            // ビルドボタン
            GUI.enabled = poseLibrary != null;
            if (GUILayout.Button("Build AssetBundles (Android & iOS)", GUILayout.Height(40)))
            {
                BuildAssetBundles();
            }
            GUI.enabled = true;
            
            EditorGUILayout.Space();
            
            // 情報表示
            if (poseLibrary != null)
            {
                EditorGUILayout.HelpBox(
                    $"PoseLibrary: {poseLibrary.name}\n" +
                    $"Pose Groups: {poseLibrary.poseGroups.Count}", 
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Please select a PoseLibrary to build.", 
                    MessageType.Warning
                );
            }
        }
        
        private void BuildAssetBundles()
        {
            if (poseLibrary == null)
            {
                EditorUtility.DisplayDialog(
                    "Error", 
                    "Please select a PoseLibrary.", 
                    "OK"
                );
                return;
            }
            
            // 出力ディレクトリの作成
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }
            
            // アセットのパスを取得
            string assetPath = AssetDatabase.GetAssetPath(poseLibrary);
            
            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog(
                    "Error", 
                    "Failed to get asset path.", 
                    "OK"
                );
                return;
            }
            
            // 元のアセット名を保存
            string originalAssetName = poseLibrary.name;
            string libraryNameLower = poseLibrary.libraryName.ToLower();
            string targetAssetName = libraryNameLower;
            
            // アセット名を一時的にlibraryNameに変更
            if (originalAssetName != targetAssetName)
            {
                string assetDirectory = Path.GetDirectoryName(assetPath);
                string newAssetPath = Path.Combine(assetDirectory, targetAssetName + ".asset");
                AssetDatabase.RenameAsset(assetPath, targetAssetName);
                AssetDatabase.SaveAssets();
                assetPath = newAssetPath;
            }
            
            // AssetBundleの名前を設定
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            string bundleName = $"{libraryNameLower}.bundle";
            importer.assetBundleName = bundleName;
            
            // 依存する全てのアセットを収集
            string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);
            foreach (string dep in dependencies)
            {
                AssetImporter depImporter = AssetImporter.GetAtPath(dep);
                if (depImporter != null)
                {
                    depImporter.assetBundleName = bundleName;
                }
            }
            
            // Android と iOS 用のビルドターゲット
            BuildTarget[] buildTargets = new BuildTarget[]
            {
                BuildTarget.Android,
                BuildTarget.iOS
            };
            
            bool allSuccess = true;
            System.Text.StringBuilder resultMessage = new System.Text.StringBuilder();
            resultMessage.AppendLine("AssetBundle build completed!\n");
            
            // 各プラットフォーム用にビルド
            foreach (BuildTarget target in buildTargets)
            {
                string platformPath = Path.Combine(outputPath, libraryNameLower, target.ToString());
                
                // プラットフォーム用のディレクトリを作成
                if (!Directory.Exists(platformPath))
                {
                    Directory.CreateDirectory(platformPath);
                }
                
                try
                {
                    EditorUtility.DisplayProgressBar(
                        "Building AssetBundles", 
                        $"Building for {target}...", 
                        0.5f
                    );
                    
                    BuildPipeline.BuildAssetBundles(
                        platformPath,
                        BuildAssetBundleOptions.None,
                        target
                    );
                    
                    string fullPath = Path.Combine(platformPath, bundleName);
                    resultMessage.AppendLine($"✓ {target}: {fullPath}");
                    Debug.Log($"AssetBundle built for {target}: {fullPath}");
                }
                catch (System.Exception e)
                {
                    allSuccess = false;
                    resultMessage.AppendLine($"✗ {target}: Failed - {e.Message}");
                    Debug.LogError($"AssetBundle build failed for {target}: {e}");
                }
            }
            
            EditorUtility.ClearProgressBar();
            
            // ビルド後、AssetBundle名をクリア
            importer.assetBundleName = "";
            foreach (string dep in dependencies)
            {
                AssetImporter depImporter = AssetImporter.GetAtPath(dep);
                if (depImporter != null)
                {
                    depImporter.assetBundleName = "";
                }
            }
            
            // アセット名を元に戻す
            if (originalAssetName != targetAssetName)
            {
                AssetDatabase.RenameAsset(assetPath, originalAssetName);
                AssetDatabase.SaveAssets();
            }
            
            AssetDatabase.Refresh();
            
            // 結果を表示
            if (allSuccess)
            {
                EditorUtility.DisplayDialog(
                    "Success", 
                    resultMessage.ToString(), 
                    "OK"
                );
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Build Completed with Errors", 
                    resultMessage.ToString(), 
                    "OK"
                );
            }
            
            // 出力フォルダを開く
            EditorUtility.RevealInFinder(outputPath);
        }
    }
}
