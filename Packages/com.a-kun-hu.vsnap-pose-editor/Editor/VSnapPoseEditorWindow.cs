using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PoseData = VSnap.Shared.Domain.Pose;
using VSnap.Shared.Domain;

namespace VSnap.Editor
{
    /// <summary>
    /// Unified editor window for Create Poses, Register Pose Group, and Build PoseLibrary.
    /// </summary>
    public class VSnapPoseEditorWindow : EditorWindow
    {
        private const int TabCreatePoses = 0;
        private const int TabPoseGroup = 1;
        private const int TabBuild = 2;

        private int _selectedTab;
        private static readonly string[] TabNames = { "Create Poses", "Register Pose Group", "Build PoseLibrary" };

        // --- Create Poses ---
        private DefaultAsset _creatorTargetFolder;
        private string _creatorOutputFolderPath;
        private bool _creatorOverwriteExisting;
        private Vector2 _creatorScrollPosition;
        private List<string> _createdPoses = new List<string>();
        private bool _creatorShowResults;
        private bool _creatorGenerateThumbnails;
        private Camera _creatorThumbnailCamera;
        private GameObject _creatorCharacter;
        private int _thumbnailWidth = 512;
        private int _thumbnailHeight = 512;

        // --- Pose Group Editor ---
        private DefaultAsset _groupTargetFolder;
        private PoseGroup _groupTargetPoseGroup;
        private Vector2 _groupScrollPosition;
        private List<PoseData> _groupFoundPoses = new List<PoseData>();
        private bool _groupHasSearched;

        // --- Build PoseLibrary ---
        private PoseLibrary _buildPoseLibrary;
        private string _buildOutputPath = "AssetBundles";

        [MenuItem("VSnap/Pose Editor")]
        public static void Open()
        {
            var window = GetWindow<VSnapPoseEditorWindow>("VSnap Pose Editor");
            window.minSize = new Vector2(420, 380);
            window.Show();
        }

        private void OnGUI()
        {
            _selectedTab = GUILayout.Toolbar(_selectedTab, TabNames);

            EditorGUILayout.Space(8);

            switch (_selectedTab)
            {
                case TabCreatePoses:
                    DrawCreatePosesTab();
                    break;
                case TabPoseGroup:
                    DrawPoseGroupTab();
                    break;
                case TabBuild:
                    DrawBuildTab();
                    break;
            }
        }

        #region Create Poses Tab

        private void DrawCreatePosesTab()
        {
            GUILayout.Label("Animation Clip to Pose", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("Recursively searches AnimationClips in the selected folder and creates Pose assets.", MessageType.Info);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _creatorTargetFolder = (DefaultAsset)EditorGUILayout.ObjectField("Target Folder", _creatorTargetFolder, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                _creatorShowResults = false;
                UpdateCreatorOutputFolderPath();
            }

            if (!string.IsNullOrEmpty(_creatorOutputFolderPath))
                EditorGUILayout.HelpBox($"Output: {_creatorOutputFolderPath}", MessageType.None);
            EditorGUILayout.Space(4);

            _creatorOverwriteExisting = EditorGUILayout.Toggle("Overwrite Existing Poses", _creatorOverwriteExisting);
            EditorGUILayout.Space(4);
            _creatorGenerateThumbnails = EditorGUILayout.Toggle("Generate Thumbnails", _creatorGenerateThumbnails);
            if (_creatorGenerateThumbnails)
            {
                EditorGUI.indentLevel++;
                _creatorThumbnailCamera = (Camera)EditorGUILayout.ObjectField("Camera", _creatorThumbnailCamera, typeof(Camera), true);
                _creatorCharacter = (GameObject)EditorGUILayout.ObjectField("Character", _creatorCharacter, typeof(GameObject), true);
                if (_creatorThumbnailCamera == null || _creatorCharacter == null)
                    EditorGUILayout.HelpBox("Specify a Camera and Character to generate thumbnails.", MessageType.Warning);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(8);

            bool canExecute = _creatorTargetFolder != null &&
                             (!_creatorGenerateThumbnails || (_creatorThumbnailCamera != null && _creatorCharacter != null));
            GUI.enabled = canExecute;
            if (GUILayout.Button("Create Poses", GUILayout.Height(28)))
                CreatePosesFromAnimationClips();
            GUI.enabled = true;

            if (_creatorShowResults && _createdPoses.Count > 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField($"Created {_createdPoses.Count} Pose(s):", EditorStyles.boldLabel);
                _creatorScrollPosition = EditorGUILayout.BeginScrollView(_creatorScrollPosition, GUILayout.Height(180));
                foreach (var posePath in _createdPoses)
                    EditorGUILayout.LabelField($"✓ {posePath}", EditorStyles.miniLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        private void UpdateCreatorOutputFolderPath()
        {
            if (_creatorTargetFolder == null) { _creatorOutputFolderPath = string.Empty; return; }
            string folderPath = AssetDatabase.GetAssetPath(_creatorTargetFolder);
            _creatorOutputFolderPath = AssetDatabase.IsValidFolder(folderPath) ? GenerateOutputFolderPath(folderPath) : string.Empty;
        }

        private string GenerateOutputFolderPath(string sourceFolderPath)
        {
            string folderName = Path.GetFileName(sourceFolderPath.TrimEnd('/'));
            string parentPath = Path.GetDirectoryName(sourceFolderPath)?.Replace("\\", "/") ?? "Assets";
            string outputFolderPath = Path.Combine(parentPath, $"{folderName}_Poses").Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(outputFolderPath))
                CreateFolderRecursively(outputFolderPath);
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
                    AssetDatabase.CreateFolder(parentPath, newFolder);
            }
        }

        private string GetRelativePath(string basePath, string fullPath)
        {
            basePath = basePath.Replace("\\", "/").TrimEnd('/');
            fullPath = fullPath.Replace("\\", "/");
            if (fullPath.StartsWith(basePath))
                return fullPath.Substring(basePath.Length).TrimStart('/');
            return Path.GetFileName(fullPath);
        }

        private void CreatePosesFromAnimationClips()
        {
            if (_creatorTargetFolder == null) { EditorUtility.DisplayDialog("Error", "Please select a folder.", "OK"); return; }
            string folderPath = AssetDatabase.GetAssetPath(_creatorTargetFolder);
            if (!AssetDatabase.IsValidFolder(folderPath)) { EditorUtility.DisplayDialog("Error", "Please select a valid folder.", "OK"); return; }

            _createdPoses.Clear();
            _creatorShowResults = false;
            string outputFolderPath = GenerateOutputFolderPath(folderPath);
            if (string.IsNullOrEmpty(outputFolderPath)) { EditorUtility.DisplayDialog("Error", "Failed to generate output folder.", "OK"); return; }

            try
            {
                string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
                if (guids.Length == 0) { EditorUtility.DisplayDialog("Info", "No AnimationClips found.", "OK"); return; }

                int createdCount = 0, skippedCount = 0;
                EditorUtility.DisplayProgressBar("Creating Poses", "Processing...", 0);

                for (int i = 0; i < guids.Length; i++)
                {
                    string clipPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    if (clip == null) continue;

                    EditorUtility.DisplayProgressBar("Creating Poses", $"{clip.name} ({i + 1}/{guids.Length})", (float)i / guids.Length);
                    string relativePath = GetRelativePath(folderPath, clipPath);
                    string outputDirectory = Path.Combine(outputFolderPath, Path.GetDirectoryName(relativePath) ?? "").Replace("\\", "/");
                    if (!AssetDatabase.IsValidFolder(outputDirectory))
                        CreateFolderRecursively(outputDirectory);

                    string poseAssetName = $"{clip.name}_Pose.asset";
                    string poseAssetPath = Path.Combine(outputDirectory, poseAssetName).Replace("\\", "/");
                    if (!_creatorOverwriteExisting && AssetDatabase.LoadAssetAtPath<PoseData>(poseAssetPath) != null) { skippedCount++; continue; }

                    PoseData pose = ScriptableObject.CreateInstance<PoseData>();
                    pose.poseName = clip.name;
                    pose.animation = clip;
                    pose.snsTag = string.Empty;
                    if (_creatorGenerateThumbnails && _creatorThumbnailCamera != null && _creatorCharacter != null)
                    {
                        string thumbnailPath = Path.ChangeExtension(poseAssetPath, ".png");
                        GenerateThumbnail(clip, thumbnailPath);
                        AssetDatabase.ImportAsset(thumbnailPath);
                        pose.thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(thumbnailPath);
                    }

                    if (_creatorOverwriteExisting && AssetDatabase.LoadAssetAtPath<PoseData>(poseAssetPath) != null)
                    {
                        PoseData existingPose = AssetDatabase.LoadAssetAtPath<PoseData>(poseAssetPath);
                        EditorUtility.CopySerialized(pose, existingPose);
                        EditorUtility.SetDirty(existingPose);
                        DestroyImmediate(pose);
                    }
                    else
                        AssetDatabase.CreateAsset(pose, poseAssetPath);

                    _createdPoses.Add(poseAssetPath);
                    createdCount++;
                }

                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                _creatorShowResults = true;
                string message = $"Done.\n\nCreated: {createdCount}";
                if (skippedCount > 0) message += $"\nSkipped: {skippedCount}";
                EditorUtility.DisplayDialog("Success", message, "OK");
                Debug.Log($"[PoseCreator] {createdCount} Pose(s) created. Output: {outputFolderPath}");
            }
            catch (System.Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"An error occurred:\n{ex.Message}", "OK");
                Debug.LogError($"[PoseCreator] {ex}");
            }
        }

        private void GenerateThumbnail(AnimationClip clip, string thumbnailPath)
        {
            try
            {
                GameObject tempCharacter = _creatorCharacter;
                Animator animator = tempCharacter.GetComponent<Animator>();
                if (animator == null) animator = tempCharacter.AddComponent<Animator>();
                clip.SampleAnimation(tempCharacter, 0f);

                Vector3 originalCameraPosition = _creatorThumbnailCamera.transform.position;
                Quaternion originalCameraRotation = _creatorThumbnailCamera.transform.rotation;
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null)
                {
                    Vector3 hipsPosition = hips.position;
                    _creatorThumbnailCamera.transform.position = hipsPosition + new Vector3(0, 0, 2f);
                    _creatorThumbnailCamera.transform.LookAt(new Vector3(hipsPosition.x, _creatorThumbnailCamera.transform.position.y, hipsPosition.z));
                }

                RenderTexture renderTexture = new RenderTexture(_thumbnailWidth, _thumbnailHeight, 24);
                RenderTexture previousRT = _creatorThumbnailCamera.targetTexture;
                _creatorThumbnailCamera.targetTexture = renderTexture;
                _creatorThumbnailCamera.Render();
                RenderTexture.active = renderTexture;
                Texture2D thumbnail = new Texture2D(_thumbnailWidth, _thumbnailHeight, TextureFormat.RGB24, false);
                thumbnail.ReadPixels(new Rect(0, 0, _thumbnailWidth, _thumbnailHeight), 0, 0);
                thumbnail.Apply();
                byte[] bytes = thumbnail.EncodeToPNG();
                string directoryPath = Path.GetDirectoryName(thumbnailPath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                    Directory.CreateDirectory(directoryPath);
                File.WriteAllBytes(thumbnailPath, bytes);
                RenderTexture.active = null;
                _creatorThumbnailCamera.targetTexture = previousRT;
                DestroyImmediate(renderTexture);
                DestroyImmediate(thumbnail);
                _creatorThumbnailCamera.transform.position = originalCameraPosition;
                _creatorThumbnailCamera.transform.rotation = originalCameraRotation;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PoseCreator] Thumbnail failed: {ex.Message}");
            }
        }

        #endregion

        #region Pose Group Tab

        private void DrawPoseGroupTab()
        {
            GUILayout.Label("Register Poses to PoseGroup", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Searches for Pose assets in the selected folder and registers them to an existing or new PoseGroup.",
                MessageType.Info
            );
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target Folder:", GUILayout.Width(100));
            _groupTargetFolder = (DefaultAsset)EditorGUILayout.ObjectField(_groupTargetFolder, typeof(DefaultAsset), false);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target PoseGroup:", GUILayout.Width(100));
            _groupTargetPoseGroup = (PoseGroup)EditorGUILayout.ObjectField(_groupTargetPoseGroup, typeof(PoseGroup), false);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(12);

            EditorGUI.BeginDisabledGroup(_groupTargetFolder == null);
            if (GUILayout.Button("Search Pose Objects", GUILayout.Height(32)))
                SearchPoses();
            EditorGUI.EndDisabledGroup();
            if (_groupTargetFolder == null)
                EditorGUILayout.HelpBox("Please select a folder to search for Pose assets.", MessageType.Info);
            EditorGUILayout.Space(8);

            if (_groupHasSearched)
            {
                EditorGUILayout.LabelField($"Found Poses: {_groupFoundPoses.Count}", EditorStyles.boldLabel);
                EditorGUILayout.Space(4);
                if (_groupFoundPoses.Count > 0)
                {
                    _groupScrollPosition = EditorGUILayout.BeginScrollView(_groupScrollPosition, GUILayout.Height(180));
                    foreach (var pose in _groupFoundPoses)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.ObjectField(pose, typeof(PoseData), false);
                        EditorGUI.EndDisabledGroup();
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                    EditorGUILayout.Space(8);
                    string buttonText = _groupTargetPoseGroup == null ? "Create New PoseGroup and Add Poses" : "Add to PoseGroup";
                    if (GUILayout.Button(buttonText, GUILayout.Height(36)))
                        AddPosesToGroup();
                    if (_groupTargetPoseGroup == null)
                        EditorGUILayout.HelpBox("No PoseGroup selected. Click the button to create a new one or assign an existing PoseGroup.", MessageType.Info);
                }
                else
                    EditorGUILayout.HelpBox("No Pose assets found in the selected folder.", MessageType.Info);
            }
        }

        private void SearchPoses()
        {
            _groupFoundPoses.Clear();
            _groupHasSearched = true;
            if (_groupTargetFolder == null) return;
            string folderPath = AssetDatabase.GetAssetPath(_groupTargetFolder);
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                EditorUtility.DisplayDialog("Error", "Selected object is not a valid folder.", "OK");
                return;
            }
            string[] guids = AssetDatabase.FindAssets("t:Pose", new[] { folderPath });
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                PoseData pose = AssetDatabase.LoadAssetAtPath<PoseData>(assetPath);
                if (pose != null) _groupFoundPoses.Add(pose);
            }
            _groupFoundPoses = _groupFoundPoses.OrderBy(p => p.name).ToList();
            Debug.Log($"Found {_groupFoundPoses.Count} Pose objects in folder: {folderPath}");
        }

        private void AddPosesToGroup()
        {
            if (_groupFoundPoses.Count == 0) return;
            if (_groupTargetPoseGroup == null)
            {
                _groupTargetPoseGroup = CreateNewPoseGroup();
                if (_groupTargetPoseGroup == null) return;
            }
            int addedCount = 0;
            foreach (var pose in _groupFoundPoses)
            {
                if (!_groupTargetPoseGroup.poses.Contains(pose))
                {
                    _groupTargetPoseGroup.poses.Add(pose);
                    addedCount++;
                }
            }
            EditorUtility.SetDirty(_groupTargetPoseGroup);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Success", $"Added {addedCount} pose(s) to {_groupTargetPoseGroup.name}.\n({_groupFoundPoses.Count - addedCount} already existed)", "OK");
            Debug.Log($"Added {addedCount} poses to PoseGroup: {_groupTargetPoseGroup.name}");
        }

        private PoseGroup CreateNewPoseGroup()
        {
            string defaultPath = "Assets/";
            if (_groupTargetFolder != null)
            {
                string folderPath = AssetDatabase.GetAssetPath(_groupTargetFolder);
                if (!string.IsNullOrEmpty(folderPath)) defaultPath = folderPath + "/";
            }
            string savePath = EditorUtility.SaveFilePanelInProject("Create New PoseGroup", "NewPoseGroup", "asset", "Enter a name for the new PoseGroup", defaultPath);
            if (string.IsNullOrEmpty(savePath)) return null;
            PoseGroup newPoseGroup = ScriptableObject.CreateInstance<PoseGroup>();
            newPoseGroup.poses = new List<PoseData>();
            AssetDatabase.CreateAsset(newPoseGroup, savePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Created new PoseGroup at: {savePath}");
            return newPoseGroup;
        }

        #endregion

        #region Build Tab

        private void DrawBuildTab()
        {
            GUILayout.Label("Build AssetBundle from PoseLibrary", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Builds Android and iOS AssetBundles from a PoseLibrary asset. Output filename is the lowercased libraryName with .bundle extension.",
                MessageType.Info
            );
            EditorGUILayout.Space(4);

            _buildPoseLibrary = (PoseLibrary)EditorGUILayout.ObjectField("PoseLibrary", _buildPoseLibrary, typeof(PoseLibrary), false);
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            _buildOutputPath = EditorGUILayout.TextField("Output Path", _buildOutputPath);
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                string selectedPath = EditorUtility.SaveFolderPanel("Select Output Folder", _buildOutputPath, "");
                if (!string.IsNullOrEmpty(selectedPath)) _buildOutputPath = selectedPath;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            GUI.enabled = _buildPoseLibrary != null;
            if (GUILayout.Button("Build AssetBundles (Android & iOS)", GUILayout.Height(36)))
                BuildAssetBundles();
            GUI.enabled = true;
            EditorGUILayout.Space(6);

            if (_buildPoseLibrary != null)
                EditorGUILayout.HelpBox($"PoseLibrary: {_buildPoseLibrary.name}\nPose Groups: {_buildPoseLibrary.poseGroups.Count}", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Please select a PoseLibrary to build.", MessageType.Warning);
        }

        private void BuildAssetBundles()
        {
            if (_buildPoseLibrary == null) { EditorUtility.DisplayDialog("Error", "Please select a PoseLibrary.", "OK"); return; }
            if (!Directory.Exists(_buildOutputPath)) Directory.CreateDirectory(_buildOutputPath);

            string assetPath = AssetDatabase.GetAssetPath(_buildPoseLibrary);
            if (string.IsNullOrEmpty(assetPath)) { EditorUtility.DisplayDialog("Error", "Failed to get asset path.", "OK"); return; }

            string originalAssetName = _buildPoseLibrary.name;
            string libraryNameLower = _buildPoseLibrary.libraryName.ToLower();
            string targetAssetName = libraryNameLower;
            if (originalAssetName != targetAssetName)
            {
                string assetDirectory = Path.GetDirectoryName(assetPath);
                assetPath = Path.Combine(assetDirectory, targetAssetName + ".asset");
                AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(_buildPoseLibrary), targetAssetName);
                AssetDatabase.SaveAssets();
            }

            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            string bundleName = $"{libraryNameLower}.bundle";
            importer.assetBundleName = bundleName;
            string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);
            foreach (string dep in dependencies)
            {
                AssetImporter depImporter = AssetImporter.GetAtPath(dep);
                if (depImporter != null) depImporter.assetBundleName = bundleName;
            }

            BuildTarget[] buildTargets = { BuildTarget.Android, BuildTarget.iOS };
            bool allSuccess = true;
            var resultMessage = new System.Text.StringBuilder();
            resultMessage.AppendLine("AssetBundle build completed!\n");

            foreach (BuildTarget target in buildTargets)
            {
                string platformPath = Path.Combine(_buildOutputPath, libraryNameLower, target.ToString());
                if (!Directory.Exists(platformPath)) Directory.CreateDirectory(platformPath);
                try
                {
                    EditorUtility.DisplayProgressBar("Building AssetBundles", $"Building for {target}...", 0.5f);
                    BuildPipeline.BuildAssetBundles(platformPath, BuildAssetBundleOptions.None, target);
                    resultMessage.AppendLine($"✓ {target}: {Path.Combine(platformPath, bundleName)}");
                    Debug.Log($"AssetBundle built for {target}: {platformPath}");
                }
                catch (System.Exception e)
                {
                    allSuccess = false;
                    resultMessage.AppendLine($"✗ {target}: Failed - {e.Message}");
                    Debug.LogError($"AssetBundle build failed for {target}: {e}");
                }
            }

            EditorUtility.ClearProgressBar();
            importer.assetBundleName = "";
            foreach (string dep in dependencies)
            {
                AssetImporter depImporter = AssetImporter.GetAtPath(dep);
                if (depImporter != null) depImporter.assetBundleName = "";
            }
            if (originalAssetName != targetAssetName)
            {
                AssetDatabase.RenameAsset(assetPath, originalAssetName);
                AssetDatabase.SaveAssets();
            }
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(allSuccess ? "Success" : "Build Completed with Errors", resultMessage.ToString(), "OK");
            EditorUtility.RevealInFinder(_buildOutputPath);
        }

        #endregion
    }
}
