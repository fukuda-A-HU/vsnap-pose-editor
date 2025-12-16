using System;
using System.Collections.Generic;
using System.IO;
using VSnap.Shared.Domain;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace VSnap.Pose.Editor
{
    [Serializable]
    public class PoseLibraryGroup
    {
        public string tag = string.Empty;
        public List<AnimationClip> poses = new();
    }
    public enum Availability
    {
        Public,
        Private
    }

    [CreateAssetMenu(fileName = "PoseLibraryEditor", menuName = "VSnap/PoseLibraryEditor")]
    public class PoseLibraryEditor : ScriptableObject
    {
        public string libraryName = string.Empty;
        [HideInInspector]
        public Availability availability = Availability.Private;
        public List<PoseLibraryGroup> poseGroupByTag = new();
        [Header("For Credits")]
        public ShopInfo shopInfo = new();
    }

    [CustomEditor(typeof(PoseLibraryEditor))]
    public class PoseLibraryEditorInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(20);

            if (GUILayout.Button("Build", GUILayout.Height(40)))
            {
                var poseLibraryEditor = (PoseLibraryEditor)target;
                PoseLibraryBuilder.Build(poseLibraryEditor);
            }
        }
    }

    public static class PoseLibraryBuilder
    {
        private const string TempFolderName = "Temp";

        public static void Build(PoseLibraryEditor source)
        {
            if (string.IsNullOrEmpty(source.libraryName))
            {
                EditorUtility.DisplayDialog("Error", "Library Name is empty.", "OK");
                return;
            }

            // ScriptableObjectのパスを取得して出力先を決定
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string sourceDirectory = Path.GetDirectoryName(sourcePath) ?? "Assets";
            string outputPath = Path.Combine(sourceDirectory, "Build").Replace("\\", "/");

            string tempFolderPath = $"Assets/{TempFolderName}";
            var createdAssets = new List<string>();
            
            // プロファイル設定の復元用
            AddressableAssetSettings settings = null;
            string profileId = null;
            string originalBuildPath = null;
            string originalLoadPath = null;

            try
            {
                // 1. 一時フォルダ作成
                if (!AssetDatabase.IsValidFolder($"Assets/{TempFolderName}"))
                {
                    AssetDatabase.CreateFolder("Assets", TempFolderName);
                }

                // 2. PoseLibrary を構築
                var poseLibrary = new PoseLibrary
                {
                    shopInfo = source.shopInfo,
                    poses = new List<VSnap.Shared.Domain.Pose>()
                };

                // 3. AnimationClip を複製し、Pose 情報を作成
                // 同じAnimationClipが複数のタグで使われている場合、タグをマージする
                var clipToTags = new Dictionary<AnimationClip, List<string>>();
                foreach (var poseGroup in source.poseGroupByTag)
                {
                    foreach (var clip in poseGroup.poses)
                    {
                        if (clip == null) continue;

                        if (!clipToTags.ContainsKey(clip))
                        {
                            clipToTags[clip] = new List<string>();
                        }
                        if (!clipToTags[clip].Contains(poseGroup.tag))
                        {
                            clipToTags[clip].Add(poseGroup.tag);
                        }
                    }
                }

                // マージされたタグ情報を使ってPoseを作成
                foreach (var kvp in clipToTags)
                {
                    var clip = kvp.Key;
                    var tags = kvp.Value;

                    string clipAssetPath = AssetDatabase.GetAssetPath(clip);
                    string guid = AssetDatabase.AssetPathToGUID(clipAssetPath).Replace("-", "");
                    string clipPath = $"{tempFolderPath}/{guid}.anim";

                    // AnimationClip を複製
                    var clonedClip = UnityEngine.Object.Instantiate(clip);
                    clonedClip.name = guid;
                    AssetDatabase.CreateAsset(clonedClip, clipPath);
                    createdAssets.Add(clipPath);

                    // Pose 情報を追加（複数のタグを含む）
                    poseLibrary.poses.Add(new VSnap.Shared.Domain.Pose
                    {
                        name = clip.name,
                        guid = guid,
                        tags = tags
                    });
                }

                // 4. PoseLibrary を JSON にシリアライズ
                string json = JsonUtility.ToJson(poseLibrary, true);
                string jsonPath = $"{tempFolderPath}/library.json";
                File.WriteAllText(Path.GetFullPath(jsonPath), json);
                AssetDatabase.Refresh();
                createdAssets.Add(jsonPath);

                // 5. Addressable に登録
                settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null)
                {
                    EditorUtility.DisplayDialog("Error", "Addressable Settings not found. Please create Addressable Settings first.", "OK");
                    return;
                }

                // グループを作成または取得
                var groupName = $"PoseLibrary_{source.libraryName}";
                var group = settings.FindGroup(groupName);
                if (group == null)
                {
                    group = settings.CreateGroup(groupName, false, false, true, null, typeof(BundledAssetGroupSchema));
                }

                // ビルドパスとロードパスをカスタマイズ
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema != null)
                {
                    schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
                    schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
                }

                // カスタムビルドパスを設定（プロファイル変数をオーバーライド）
                profileId = settings.activeProfileId;
                string absoluteOutputPath = Path.GetFullPath(outputPath).Replace("\\", "/");
                
                // 元のプロファイル値を保存
                originalBuildPath = settings.profileSettings.GetValueByName(profileId, AddressableAssetSettings.kLocalBuildPath);
                originalLoadPath = settings.profileSettings.GetValueByName(profileId, AddressableAssetSettings.kLocalLoadPath);
                
                settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kLocalBuildPath, absoluteOutputPath);
                settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kLocalLoadPath, absoluteOutputPath);

                // アセットを登録
                foreach (var assetPath in createdAssets)
                {
                    var assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                    var entry = settings.CreateOrMoveEntry(assetGuid, group, false, false);
                    entry.address = Path.GetFileNameWithoutExtension(assetPath);
                }

                // 6. ビルド実行
                EditorUtility.DisplayProgressBar("Building Addressables", "Building...", 0.5f);
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
                EditorUtility.ClearProgressBar();

                if (!string.IsNullOrEmpty(result.Error))
                {
                    EditorUtility.DisplayDialog("Build Error", result.Error, "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Success", $"Build completed!\nOutput: {absoluteOutputPath}", "OK");
                }
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", e.Message, "OK");
                Debug.LogException(e);
            }
            finally
            {
                // プロファイル設定を元に戻す
                if (settings != null && profileId != null && originalBuildPath != null && originalLoadPath != null)
                {
                    settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kLocalBuildPath, originalBuildPath);
                    settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kLocalLoadPath, originalLoadPath);
                }
                
                // 一時フォルダを削除
                CleanupTempAssets(tempFolderPath);
            }
        }

        private static void CleanupTempAssets(string tempFolderPath)
        {
            if (AssetDatabase.IsValidFolder(tempFolderPath))
            {
                AssetDatabase.DeleteAsset(tempFolderPath);
            }

            // 親フォルダが空なら削除
            string parentFolder = $"Assets/{TempFolderName}";
            if (AssetDatabase.IsValidFolder(parentFolder))
            {
                var subFolders = AssetDatabase.GetSubFolders(parentFolder);
                if (subFolders.Length == 0)
                {
                    AssetDatabase.DeleteAsset(parentFolder);
                }
            }

            AssetDatabase.Refresh();
        }
    }
}
