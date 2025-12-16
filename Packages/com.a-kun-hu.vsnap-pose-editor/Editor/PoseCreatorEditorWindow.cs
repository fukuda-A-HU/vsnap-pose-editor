using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PoseData = VSnap.Shared.Domain.Pose;

namespace VSnap.Editor
{
    public class PoseCreatorEditorWindow : EditorWindow
    {
        private DefaultAsset _targetFolder;
        private string _outputFolderPath;
        private bool _overwriteExisting;
        private Vector2 _scrollPosition;
        private List<string> _createdPoses = new List<string>();
        private bool _showResults;
        
        // サムネイル生成用
        private bool _generateThumbnails;
        private Camera _thumbnailCamera;
        private GameObject _characterPrefab;
        private int _thumbnailWidth = 512;
        private int _thumbnailHeight = 512;

        [MenuItem("VSnap/Create Poses from Animation Clips")]
        public static void ShowWindow()
        {
            var window = GetWindow<PoseCreatorEditorWindow>("Pose Creator");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Animation Clip to Pose Converter", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "指定したフォルダ内のAnimationClipを再帰的に検索し、Poseアセットを作成します。",
                MessageType.Info
            );
            EditorGUILayout.Space();

            // フォルダ選択
            EditorGUI.BeginChangeCheck();
            _targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Target Folder",
                _targetFolder,
                typeof(DefaultAsset),
                false
            );
            if (EditorGUI.EndChangeCheck())
            {
                _showResults = false;
                UpdateOutputFolderPath();
            }

            // 出力先フォルダの表示
            if (!string.IsNullOrEmpty(_outputFolderPath))
            {
                EditorGUILayout.HelpBox($"出力先: {_outputFolderPath}", MessageType.None);
            }

            EditorGUILayout.Space();

            // オプション
            _overwriteExisting = EditorGUILayout.Toggle("Overwrite Existing Poses", _overwriteExisting);
            
            EditorGUILayout.Space();
            
            // サムネイル生成設定
            _generateThumbnails = EditorGUILayout.Toggle("Generate Thumbnails", _generateThumbnails);
            
            if (_generateThumbnails)
            {
                EditorGUI.indentLevel++;
                _thumbnailCamera = (Camera)EditorGUILayout.ObjectField(
                    "Camera",
                    _thumbnailCamera,
                    typeof(Camera),
                    true
                );
                _characterPrefab = (GameObject)EditorGUILayout.ObjectField(
                    "Character Prefab",
                    _characterPrefab,
                    typeof(GameObject),
                    true
                );
                
                if (_thumbnailCamera == null || _characterPrefab == null)
                {
                    EditorGUILayout.HelpBox(
                        "サムネイルを生成するにはカメラとキャラクターを指定してください。",
                        MessageType.Warning
                    );
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.Space();

            // 実行ボタン
            bool canExecute = _targetFolder != null && 
                             (!_generateThumbnails || (_thumbnailCamera != null && _characterPrefab != null));
            GUI.enabled = canExecute;
            if (GUILayout.Button("Create Poses", GUILayout.Height(30)))
            {
                CreatePosesFromAnimationClips();
            }
            GUI.enabled = true;

            // 結果表示
            if (_showResults && _createdPoses.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Created {_createdPoses.Count} Pose(s):", EditorStyles.boldLabel);
                
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));
                foreach (var posePath in _createdPoses)
                {
                    EditorGUILayout.LabelField($"✓ {posePath}", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void CreatePosesFromAnimationClips()
        {
            if (_targetFolder == null)
            {
                EditorUtility.DisplayDialog("Error", "フォルダを選択してください。", "OK");
                return;
            }

            string folderPath = AssetDatabase.GetAssetPath(_targetFolder);
            
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                EditorUtility.DisplayDialog("Error", "有効なフォルダを選択してください。", "OK");
                return;
            }

            _createdPoses.Clear();
            _showResults = false;

            // 出力先フォルダを生成
            string outputFolderPath = GenerateOutputFolderPath(folderPath);
            if (string.IsNullOrEmpty(outputFolderPath))
            {
                EditorUtility.DisplayDialog("Error", "出力先フォルダの生成に失敗しました。", "OK");
                return;
            }

            try
            {
                // 指定フォルダ配下のAnimationClipを再帰的に検索
                string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
                
                if (guids.Length == 0)
                {
                    EditorUtility.DisplayDialog("Info", "AnimationClipが見つかりませんでした。", "OK");
                    return;
                }

                int createdCount = 0;
                int skippedCount = 0;

                EditorUtility.DisplayProgressBar("Creating Poses", "Processing...", 0);

                for (int i = 0; i < guids.Length; i++)
                {
                    string clipPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

                    if (clip == null)
                        continue;

                    float progress = (float)i / guids.Length;
                    EditorUtility.DisplayProgressBar(
                        "Creating Poses",
                        $"Processing: {clip.name} ({i + 1}/{guids.Length})",
                        progress
                    );

                    // 相対パスを計算して出力先のパスを生成
                    string relativePath = GetRelativePath(folderPath, clipPath);
                    string outputDirectory = Path.Combine(outputFolderPath, Path.GetDirectoryName(relativePath) ?? "").Replace("\\", "/");
                    
                    // 出力先ディレクトリが存在しない場合は作成
                    if (!AssetDatabase.IsValidFolder(outputDirectory))
                    {
                        CreateFolderRecursively(outputDirectory);
                    }

                    string poseAssetName = $"{clip.name}_Pose.asset";
                    string poseAssetPath = Path.Combine(outputDirectory, poseAssetName).Replace("\\", "/");

                    // 既存チェック
                    if (!_overwriteExisting && AssetDatabase.LoadAssetAtPath<PoseData>(poseAssetPath) != null)
                    {
                        skippedCount++;
                        continue;
                    }

                    // Poseアセットを作成
                    PoseData pose = ScriptableObject.CreateInstance<PoseData>();
                    pose.animation = clip;
                    pose.snsTag = string.Empty;
                    
                    // サムネイル生成
                    if (_generateThumbnails && _thumbnailCamera != null && _characterPrefab != null)
                    {
                        string thumbnailPath = Path.ChangeExtension(poseAssetPath, ".png");
                        GenerateThumbnail(clip, thumbnailPath);
                    }

                    // アセットとして保存
                    if (_overwriteExisting && AssetDatabase.LoadAssetAtPath<PoseData>(poseAssetPath) != null)
                    {
                        // 既存のアセットを上書き
                        PoseData existingPose = AssetDatabase.LoadAssetAtPath<PoseData>(poseAssetPath);
                        EditorUtility.CopySerialized(pose, existingPose);
                        EditorUtility.SetDirty(existingPose);
                        DestroyImmediate(pose);
                    }
                    else
                    {
                        // 新規作成
                        AssetDatabase.CreateAsset(pose, poseAssetPath);
                    }

                    _createdPoses.Add(poseAssetPath);
                    createdCount++;
                }

                EditorUtility.ClearProgressBar();

                // アセットデータベースを更新
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                _showResults = true;

                // 結果表示
                string message = $"完了しました。\n\n作成: {createdCount}件";
                if (skippedCount > 0)
                {
                    message += $"\nスキップ: {skippedCount}件";
                }

                EditorUtility.DisplayDialog("Success", message, "OK");

                Debug.Log($"[PoseCreator] {createdCount} Pose(s) created from Animation Clips in: {folderPath}");
                Debug.Log($"[PoseCreator] Output folder: {outputFolderPath}");
            }
            catch (System.Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"エラーが発生しました:\n{ex.Message}", "OK");
                Debug.LogError($"[PoseCreator] Error: {ex}");
            }
        }

        private void UpdateOutputFolderPath()
        {
            if (_targetFolder == null)
            {
                _outputFolderPath = string.Empty;
                return;
            }

            string folderPath = AssetDatabase.GetAssetPath(_targetFolder);
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                _outputFolderPath = GenerateOutputFolderPath(folderPath);
            }
            else
            {
                _outputFolderPath = string.Empty;
            }
        }

        private string GenerateOutputFolderPath(string sourceFolderPath)
        {
            // 元のフォルダ名に "_Poses" を付けて出力先フォルダ名を生成
            string folderName = Path.GetFileName(sourceFolderPath.TrimEnd('/'));
            string parentPath = Path.GetDirectoryName(sourceFolderPath)?.Replace("\\", "/") ?? "Assets";
            
            string outputFolderPath = Path.Combine(parentPath, $"{folderName}_Poses").Replace("\\", "/");
            
            // 出力先フォルダが存在しない場合は作成
            if (!AssetDatabase.IsValidFolder(outputFolderPath))
            {
                CreateFolderRecursively(outputFolderPath);
            }

            return outputFolderPath;
        }

        private void CreateFolderRecursively(string folderPath)
        {
            folderPath = folderPath.Replace("\\", "/");
            string[] folders = folderPath.Split('/');
            string currentPath = folders[0];

            for (int i = 1; i < folders.Length; i++)
            {
                string newFolder = folders[i];
                string parentPath = currentPath;
                currentPath = $"{currentPath}/{newFolder}";

                if (!AssetDatabase.IsValidFolder(currentPath))
                {
                    AssetDatabase.CreateFolder(parentPath, newFolder);
                }
            }
        }

        private string GetRelativePath(string basePath, string fullPath)
        {
            basePath = basePath.Replace("\\", "/").TrimEnd('/');
            fullPath = fullPath.Replace("\\", "/");

            if (fullPath.StartsWith(basePath))
            {
                string relativePath = fullPath.Substring(basePath.Length).TrimStart('/');
                return relativePath;
            }

            return Path.GetFileName(fullPath);
        }
        
        private void GenerateThumbnail(AnimationClip clip, string thumbnailPath)
        {
            try
            {
                // 一時的にキャラクターをインスタンス化
                GameObject tempCharacter = Instantiate(_characterPrefab);
                tempCharacter.hideFlags = HideFlags.HideAndDontSave;
                
                // Animatorコンポーネントを取得または追加
                Animator animator = tempCharacter.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = tempCharacter.AddComponent<Animator>();
                }
                
                // アニメーションを適用（最初のフレーム）
                clip.SampleAnimation(tempCharacter, 0f);
                
                // RenderTextureを作成
                RenderTexture renderTexture = new RenderTexture(_thumbnailWidth, _thumbnailHeight, 24);
                RenderTexture previousRT = _thumbnailCamera.targetTexture;
                _thumbnailCamera.targetTexture = renderTexture;
                
                // カメラでレンダリング
                _thumbnailCamera.Render();
                
                // Texture2Dに変換
                RenderTexture.active = renderTexture;
                Texture2D thumbnail = new Texture2D(_thumbnailWidth, _thumbnailHeight, TextureFormat.RGB24, false);
                thumbnail.ReadPixels(new Rect(0, 0, _thumbnailWidth, _thumbnailHeight), 0, 0);
                thumbnail.Apply();
                
                // PNGとして保存
                byte[] bytes = thumbnail.EncodeToPNG();
                string directoryPath = Path.GetDirectoryName(thumbnailPath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                File.WriteAllBytes(thumbnailPath, bytes);
                
                // クリーンアップ
                RenderTexture.active = null;
                _thumbnailCamera.targetTexture = previousRT;
                DestroyImmediate(renderTexture);
                DestroyImmediate(thumbnail);
                DestroyImmediate(tempCharacter);
                
                // アセットをインポート
                AssetDatabase.ImportAsset(thumbnailPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PoseCreator] Failed to generate thumbnail for {clip.name}: {ex.Message}");
            }
        }
    }
}
