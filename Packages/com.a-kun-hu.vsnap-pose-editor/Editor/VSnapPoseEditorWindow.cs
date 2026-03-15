using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
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
        private const string BuildOutputSubdir = "VSnapAssetBundleBuild";
        private static string BuildOutputPath => Path.Combine(Application.temporaryCachePath, BuildOutputSubdir);
        private PoseLibrary _buildPoseLibrary;
        private int _buildPlatformIndex; // 0 = Android, 1 = iOS

        // --- Upload (vsnap-flick) ---
        private const string VsnapFlickConfigUrl = "https://flick.vsnap.jp/config.json";
        private const string VsnapFlickUploadPassPageUrl = "https://flick.vsnap.jp/upload-pass.html";
        private static readonly string EditorPrefsUploadPass = "VSnap.PoseEditor.Upload.Pass";
        private string _uploadPass = "";
        private string _lastShortUrl;
        private Texture2D _lastQrTexture;
        private bool _uploadInProgress;
        private static readonly string[] PlatformOptions = { "Android", "iOS" };

        [MenuItem("VSnap/Pose Editor")]
        public static void Open()
        {
            var window = GetWindow<VSnapPoseEditorWindow>("VSnap Pose Editor");
            window.minSize = new Vector2(420, 380);
            window.Show();
        }

        private void OnEnable()
        {
            _uploadPass = EditorPrefs.GetString(EditorPrefsUploadPass, "");
        }

        private BuildTarget BuildTargetFromIndex => _buildPlatformIndex == 0 ? BuildTarget.Android : BuildTarget.iOS;

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
                "Builds an AssetBundle from a PoseLibrary asset. Output is the lowercased libraryName with .bundle extension, under temporary cache.",
                MessageType.Info
            );
            EditorGUILayout.Space(4);

            _buildPoseLibrary = (PoseLibrary)EditorGUILayout.ObjectField("PoseLibrary", _buildPoseLibrary, typeof(PoseLibrary), false);
            _buildPlatformIndex = EditorGUILayout.Popup("Platform", _buildPlatformIndex, PlatformOptions);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox($"Output: {BuildOutputPath}", MessageType.None);
            EditorGUILayout.Space(10);

            DrawUploadPassRow();
            EditorGUILayout.Space(8);

            GUI.enabled = _buildPoseLibrary != null;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Build", GUILayout.Height(36)))
                BuildAssetBundles(uploadAfterBuild: false);
            if (GUILayout.Button("Build & Upload", GUILayout.Height(36)))
                BuildAssetBundles(uploadAfterBuild: true);
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
            EditorGUILayout.Space(12);

            DrawUploadResultSection();
            EditorGUILayout.Space(8);

            if (_buildPoseLibrary != null)
                EditorGUILayout.HelpBox($"PoseLibrary: {_buildPoseLibrary.name}\nPose Groups: {_buildPoseLibrary.poseGroups.Count}", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Please select a PoseLibrary to build.", MessageType.Warning);
        }

        private void DrawUploadPassRow()
        {
            GUILayout.Label("Upload to vsnap-flick", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            _uploadPass = EditorGUILayout.PasswordField("Upload Pass", _uploadPass);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(EditorPrefsUploadPass, _uploadPass ?? "");
            EditorGUILayout.Space(4);
            if (EditorGUILayout.LinkButton("Upload Pass を取得（vsnap-flick）"))
                Application.OpenURL(VsnapFlickUploadPassPageUrl);
        }

        private void DrawUploadResultSection()
        {
            if (string.IsNullOrEmpty(_lastShortUrl))
                return;
            GUILayout.Label("Download", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(_lastShortUrl, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (_lastQrTexture != null)
            {
                float size = Mathf.Min(256, _lastQrTexture.width, _lastQrTexture.height);
                Rect r = GUILayoutUtility.GetRect(size, size);
                GUI.DrawTexture(r, _lastQrTexture, ScaleMode.ScaleToFit);
            }
        }

        private string GetBuiltBundlePath()
        {
            if (_buildPoseLibrary == null) return null;
            string libraryNameLower = _buildPoseLibrary.libraryName.ToLower();
            string bundleName = $"{libraryNameLower}.bundle";
            return Path.Combine(BuildOutputPath, libraryNameLower, BuildTargetFromIndex.ToString(), bundleName);
        }

        private void BuildAssetBundles(bool uploadAfterBuild)
        {
            if (_buildPoseLibrary == null) { EditorUtility.DisplayDialog("Error", "Please select a PoseLibrary.", "OK"); return; }
            var target = BuildTargetFromIndex;
            string buildOutputPath = BuildOutputPath;
            if (!Directory.Exists(buildOutputPath)) Directory.CreateDirectory(buildOutputPath);

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

            string platformPath = Path.Combine(buildOutputPath, libraryNameLower, target.ToString());
            if (!Directory.Exists(platformPath)) Directory.CreateDirectory(platformPath);
            bool success = false;
            try
            {
                EditorUtility.DisplayProgressBar("Building AssetBundles", $"Building for {target}...", 0.5f);
                BuildPipeline.BuildAssetBundles(platformPath, BuildAssetBundleOptions.None, target);
                success = true;
                Debug.Log($"AssetBundle built for {target}: {platformPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"AssetBundle build failed for {target}: {e}");
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

            EditorUtility.DisplayDialog(success ? "Success" : "Error", success ? $"AssetBundle built: {Path.Combine(platformPath, bundleName)}" : "Build failed. See Console.", "OK");
            if (success)
            {
                EditorUtility.RevealInFinder(platformPath);
                if (uploadAfterBuild)
                    UploadBundle();
            }
        }

        private void UploadBundle()
        {
            string bundlePath = GetBuiltBundlePath();
            if (string.IsNullOrEmpty(bundlePath) || !File.Exists(bundlePath))
            {
                EditorUtility.DisplayDialog("Error", "No .bundle file found. Build first.", "OK");
                return;
            }
            _uploadInProgress = true;
            EditorApplication.update += UploadBundleTick;
        }

        private static string ResolveVsnapFlickApiBase()
        {
            var req = new UnityWebRequest(VsnapFlickConfigUrl) { downloadHandler = new DownloadHandlerBuffer() };
            req.SendWebRequest();
            while (!req.isDone)
            {
                if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
                    break;
                System.Threading.Thread.Sleep(50);
            }
            string apiBase = null;
            if (req.result == UnityWebRequest.Result.Success && req.downloadHandler?.data != null)
            {
                try
                {
                    var config = JsonUtility.FromJson<VsnapFlickConfig>(req.downloadHandler.text);
                    if (!string.IsNullOrEmpty(config?.apiBaseUrl))
                        apiBase = config.apiBaseUrl.TrimEnd('/');
                }
                catch { }
            }
            req.Dispose();
            return apiBase ?? "https://frnf9epuh1.execute-api.ap-northeast-1.amazonaws.com/Prod";
        }

        [System.Serializable]
        private class VsnapFlickConfig { public string apiBaseUrl; }

        private void UploadBundleTick()
        {
            EditorApplication.update -= UploadBundleTick;
            _uploadInProgress = false;
            string bundlePath = GetBuiltBundlePath();
            string apiBase = ResolveVsnapFlickApiBase();
            string filename = Path.GetFileName(bundlePath);
            var initReq = new UnityWebRequest($"{apiBase}/upload/init", "POST");
            initReq.SetRequestHeader("Content-Type", "application/json");
            initReq.SetRequestHeader("X-Upload-Pass", _uploadPass ?? "");
            var body = $"{{\"filename\":\"{EscapeJson(filename)}\",\"upload_pass\":\"{EscapeJson(_uploadPass ?? "")}\"}}";
            initReq.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            initReq.downloadHandler = new DownloadHandlerBuffer();
            initReq.SendWebRequest();
            while (!initReq.isDone)
            {
                if (initReq.result == UnityWebRequest.Result.ConnectionError || initReq.result == UnityWebRequest.Result.ProtocolError)
                    break;
                System.Threading.Thread.Sleep(50);
            }
            if (initReq.result != UnityWebRequest.Result.Success)
            {
                long code = initReq.responseCode;
                string err = initReq.error;
                if (initReq.downloadHandler != null && initReq.downloadHandler.data != null)
                {
                    try
                    {
                        var json = JsonUtility.FromJson<UploadErrorResponse>(initReq.downloadHandler.text);
                        if (!string.IsNullOrEmpty(json.error)) err = json.error;
                    }
                    catch { }
                }
                string statusHint = code > 0 ? $" (HTTP {code})" : "";
                if (code == 401)
                    err = (string.IsNullOrEmpty(err) ? "アップロード用パスワードが正しくありません。Discord でログインするか、Upload Pass を取得してください。" : err) + statusHint;
                else if (code == 403)
                    err = (string.IsNullOrEmpty(err) ? "Access Denied (403)" : err) + statusHint;
                else if (code > 0)
                    err = err + statusHint;
                EditorUtility.DisplayDialog("Upload init failed", err, "OK");
                initReq.Dispose();
                return;
            }
            string initJson = initReq.downloadHandler.text;
            initReq.Dispose();

            string presignedUrl = null;
            string shortUrl = null;
            string qrCodeBase64 = null;
            PresignedField[] fieldsOrder = null;
            string fileFieldName = "file";
            try
            {
                var init = JsonUtility.FromJson<UploadInitResponse>(initJson);
                if (init.presigned_post != null)
                {
                    presignedUrl = init.presigned_post.url;
                    shortUrl = init.short_url;
                    qrCodeBase64 = init.qr_code_base64;
                    fileFieldName = string.IsNullOrEmpty(init.presigned_post.file_field_name) ? "file" : init.presigned_post.file_field_name;
                    fieldsOrder = init.presigned_post.fields_order;
                    if ((fieldsOrder == null || fieldsOrder.Length == 0) && !string.IsNullOrEmpty(presignedUrl))
                        fieldsOrder = ParseFieldsFromInitJson(initJson);
                }
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Upload init parse error", e.Message, "OK");
                return;
            }
            if (string.IsNullOrEmpty(presignedUrl) || fieldsOrder == null || fieldsOrder.Length == 0)
            {
                EditorUtility.DisplayDialog("Upload init error", "Invalid response: missing presigned_post or fields.", "OK");
                return;
            }

            byte[] fileBytes = File.ReadAllBytes(bundlePath);
            string boundary = "----UnityFormBoundary" + System.Guid.NewGuid().ToString("N");
            var formStream = new MemoryStream();
            var writer = new StreamWriter(formStream, System.Text.Encoding.UTF8, 1024, true) { NewLine = "\r\n" };
            foreach (var f in fieldsOrder)
            {
                if (string.IsNullOrEmpty(f.k)) continue;
                writer.Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"{f.k}\"\r\n\r\n{f.v}\r\n");
                writer.Flush();
            }
            writer.Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"{fileFieldName}\"; filename=\"{Path.GetFileName(bundlePath)}\"\r\nContent-Type: application/octet-stream\r\n\r\n");
            writer.Flush();
            formStream.Write(fileBytes, 0, fileBytes.Length);
            writer.Write($"\r\n--{boundary}--\r\n");
            writer.Flush();
            formStream.Position = 0;

            var uploadReq = new UnityWebRequest(presignedUrl, "POST");
            uploadReq.SetRequestHeader("Content-Type", $"multipart/form-data; boundary={boundary}");
            uploadReq.uploadHandler = new UploadHandlerRaw(formStream.ToArray());
            uploadReq.downloadHandler = new DownloadHandlerBuffer();
            uploadReq.SendWebRequest();
            while (!uploadReq.isDone)
            {
                if (uploadReq.result == UnityWebRequest.Result.ConnectionError || uploadReq.result == UnityWebRequest.Result.ProtocolError)
                    break;
                System.Threading.Thread.Sleep(50);
            }
            var uploadSuccess = uploadReq.result == UnityWebRequest.Result.Success;
            var uploadError = uploadReq.error;
            long uploadResponseCode = uploadReq.responseCode;
            uploadReq.Dispose();
            if (!uploadSuccess)
            {
                string msg = uploadError ?? "Unknown error";
                if (uploadResponseCode > 0)
                    msg = msg + " (HTTP " + uploadResponseCode + ")";
                if (uploadResponseCode == 403)
                    msg = msg + " S3 の presigned POST が拒否されました。フォームの構成を確認してください。";
                EditorUtility.DisplayDialog("Upload to S3 failed", msg, "OK");
                return;
            }

            _lastShortUrl = shortUrl;
            if (!string.IsNullOrEmpty(qrCodeBase64))
            {
                string b64 = qrCodeBase64;
                if (b64.Contains(",")) b64 = b64.Substring(b64.IndexOf(',') + 1);
                try
                {
                    byte[] imgBytes = System.Convert.FromBase64String(b64.Trim());
                    if (_lastQrTexture != null) DestroyImmediate(_lastQrTexture);
                    _lastQrTexture = new Texture2D(2, 2);
                    _lastQrTexture.LoadImage(imgBytes);
                }
                catch (System.Exception e) { Debug.LogWarning("QR decode: " + e.Message); }
            }
            Repaint();
            EditorUtility.DisplayDialog("Upload complete", $"Download URL: {shortUrl}", "OK");
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string UnescapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\\\", "\\").Replace("\\\"", "\"");
        }

        private static PresignedField[] ParseFieldsFromInitJson(string initJson)
        {
            int idx = initJson.IndexOf("\"fields\"");
            if (idx < 0) return null;
            idx = initJson.IndexOf('{', idx);
            if (idx < 0) return null;
            int depth = 1;
            int i = idx + 1;
            while (i < initJson.Length && depth > 0)
            {
                char c = initJson[i++];
                if (c == '"') { while (i < initJson.Length && (initJson[i] != '"' || initJson[i - 1] == '\\')) i++; i++; continue; }
                if (c == '{') depth++;
                else if (c == '}') depth--;
            }
            string fieldsStr = initJson.Substring(idx, i - idx);
            string[] fieldOrder = { "key", "x-amz-algorithm", "x-amz-credential", "x-amz-date", "policy", "x-amz-signature", "AWSAccessKeyId", "x-amz-security-token", "signature" };
            var list = new List<PresignedField>();
            foreach (string key in fieldOrder)
            {
                var m = Regex.Match(fieldsStr, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (m.Success)
                    list.Add(new PresignedField { k = key, v = UnescapeJsonString(m.Groups[1].Value) });
            }
            foreach (Match m in Regex.Matches(fieldsStr, "\"([^\"]+)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                string k = m.Groups[1].Value;
                if (list.All(f => f.k != k))
                    list.Add(new PresignedField { k = k, v = UnescapeJsonString(m.Groups[2].Value) });
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        [System.Serializable]
        private class UploadErrorResponse { public string error; }

        [System.Serializable]
        private class UploadInitResponse
        {
            public PresignedPost presigned_post;
            public string short_url;
            public string qr_code_base64;
        }

        [System.Serializable]
        private class PresignedPost
        {
            public string url;
            public string file_field_name;
            public PresignedField[] fields_order;
        }

        [System.Serializable]
        private class PresignedField { public string k; public string v; }

        #endregion
    }
}
