using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VSnap.Shared.Domain;

namespace VSnap.Editor
{
    public class PoseGroupEditorWindow : EditorWindow
    {
        private DefaultAsset targetFolder;
        private PoseGroup targetPoseGroup;
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
            targetPoseGroup = (PoseGroup)EditorGUILayout.ObjectField(
                targetPoseGroup,
                typeof(PoseGroup),
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
                    string buttonText = targetPoseGroup == null ? "Create New PoseGroup and Add Poses" : "Add to PoseGroup";
                    if (GUILayout.Button(buttonText, GUILayout.Height(40)))
                    {
                        AddPosesToGroup();
                    }

                    if (targetPoseGroup == null)
                    {
                        EditorGUILayout.HelpBox("No PoseGroup selected. Click the button to create a new PoseGroup.", MessageType.Info);
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
            if (foundPoses.Count == 0)
                return;

            // PoseGroupが未設定の場合は新規作成
            if (targetPoseGroup == null)
            {
                targetPoseGroup = CreateNewPoseGroup();
                if (targetPoseGroup == null)
                {
                    // ユーザーがキャンセルした場合
                    return;
                }
            }

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

        private PoseGroup CreateNewPoseGroup()
        {
            // 保存先パスを指定
            string defaultPath = "Assets/";
            if (targetFolder != null)
            {
                string folderPath = AssetDatabase.GetAssetPath(targetFolder);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    defaultPath = folderPath + "/";
                }
            }

            string savePath = EditorUtility.SaveFilePanelInProject(
                "Create New PoseGroup",
                "NewPoseGroup",
                "asset",
                "Enter a name for the new PoseGroup",
                defaultPath
            );

            if (string.IsNullOrEmpty(savePath))
            {
                // ユーザーがキャンセルした
                return null;
            }

            // 新しいPoseGroupを作成
            PoseGroup newPoseGroup = ScriptableObject.CreateInstance<PoseGroup>();
            newPoseGroup.poses = new List<VSnap.Shared.Domain.Pose>();

            // アセットとして保存
            AssetDatabase.CreateAsset(newPoseGroup, savePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"Created new PoseGroup at: {savePath}");

            return newPoseGroup;
        }
    }
}
