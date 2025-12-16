using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VSnap.Shared.Domain;

namespace VSnap.Pose.Editor
{
    public class PoseGroupEditorWindow : EditorWindow
    {
        private DefaultAsset targetFolder;
        private VSnap.Shared.Domain.PoseGroup targetPoseGroup;
        private Vector2 scrollPosition;
        private List<VSnap.Shared.Domain.Pose> foundPoses = new List<VSnap.Shared.Domain.Pose>();
        private bool hasSearched = false;

        [MenuItem("VSnap/Pose Group Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<PoseGroupEditorWindow>("Pose Group Editor");
            window.minSize = new Vector2(450, 400);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Pose Group Editor", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            // フォルダ選択
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target Folder:", GUILayout.Width(100));
            targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                targetFolder,
                typeof(DefaultAsset),
                false
            );
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // PoseGroup選択
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target PoseGroup:", GUILayout.Width(100));
            targetPoseGroup = (VSnap.Shared.Domain.PoseGroup)EditorGUILayout.ObjectField(
                targetPoseGroup,
                typeof(VSnap.Shared.Domain.PoseGroup),
                false
            );
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(15);

            // 検索ボタン
            EditorGUI.BeginDisabledGroup(targetFolder == null);
            if (GUILayout.Button("Search Pose Objects", GUILayout.Height(35)))
            {
                SearchPoses();
            }
            EditorGUI.EndDisabledGroup();

            if (targetFolder == null)
            {
                EditorGUILayout.HelpBox("Please select a folder to search for Pose objects.", MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // 検索結果表示
            if (hasSearched)
            {
                EditorGUILayout.LabelField($"Found Poses: {foundPoses.Count}", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                if (foundPoses.Count > 0)
                {
                    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
                    foreach (var pose in foundPoses)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.ObjectField(pose, typeof(VSnap.Shared.Domain.Pose), false);
                        EditorGUI.EndDisabledGroup();
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();

                    EditorGUILayout.Space(10);

                    // 追加ボタン
                    EditorGUI.BeginDisabledGroup(targetPoseGroup == null);
                    if (GUILayout.Button("Add to PoseGroup", GUILayout.Height(40)))
                    {
                        AddPosesToGroup();
                    }
                    EditorGUI.EndDisabledGroup();

                    if (targetPoseGroup == null)
                    {
                        EditorGUILayout.HelpBox("Please select a PoseGroup to add the found poses.", MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("No Pose objects found in the selected folder.", MessageType.Info);
                }
            }
        }

        private void SearchPoses()
        {
            foundPoses.Clear();
            hasSearched = true;

            if (targetFolder == null)
                return;

            string folderPath = AssetDatabase.GetAssetPath(targetFolder);

            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                EditorUtility.DisplayDialog("Error", "Selected object is not a valid folder.", "OK");
                return;
            }

            // フォルダ内のすべてのアセットのGUIDを取得
            string[] guids = AssetDatabase.FindAssets("t:Pose", new[] { folderPath });

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                VSnap.Shared.Domain.Pose pose = AssetDatabase.LoadAssetAtPath<VSnap.Shared.Domain.Pose>(assetPath);

                if (pose != null)
                {
                    foundPoses.Add(pose);
                }
            }

            // 名前でソート
            foundPoses = foundPoses.OrderBy(p => p.name).ToList();

            Debug.Log($"Found {foundPoses.Count} Pose objects in folder: {folderPath}");
        }

        private void AddPosesToGroup()
        {
            if (targetPoseGroup == null || foundPoses.Count == 0)
                return;

            int addedCount = 0;
            foreach (var pose in foundPoses)
            {
                // 既に含まれているかチェック
                if (!targetPoseGroup.poses.Contains(pose))
                {
                    targetPoseGroup.poses.Add(pose);
                    addedCount++;
                }
            }

            EditorUtility.SetDirty(targetPoseGroup);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog(
                "Success",
                $"Added {addedCount} pose(s) to {targetPoseGroup.name}.\n({foundPoses.Count - addedCount} already existed)",
                "OK"
            );

            Debug.Log($"Added {addedCount} poses to PoseGroup: {targetPoseGroup.name}");
        }
    }
}
