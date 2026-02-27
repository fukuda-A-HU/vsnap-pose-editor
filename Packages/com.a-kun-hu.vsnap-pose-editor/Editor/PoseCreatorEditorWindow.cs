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
        
        // Thumbnail generation
        private bool _generateThumbnails;
        private Camera _thumbnailCamera;
        private GameObject _character;
        private int _thumbnailWidth = 512;
        private int _thumbnailHeight = 512;

        private void OnGUI()
        {
            GUILayout.Label("Animation Clip to Pose Converter", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Recursively searches AnimationClips in the selected folder and creates Pose assets.",
                MessageType.Info
            );
            EditorGUILayout.Space();

            // Folder selection
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

            // Output folder preview
            if (!string.IsNullOrEmpty(_outputFolderPath))
            {
                EditorGUILayout.HelpBox($"Output: {_outputFolderPath}", MessageType.None);
            }

            EditorGUILayout.Space();

            // Options
            _overwriteExisting = EditorGUILayout.Toggle("Overwrite Existing Poses", _overwriteExisting);
            
            EditorGUILayout.Space();
            
            // Thumbnail settings
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
                _character = (GameObject)EditorGUILayout.ObjectField(
                    "Character",
                    _character,
                    typeof(GameObject),
                    true
                );
                
                if (_thumbnailCamera == null || _character == null)
                {
                    EditorGUILayout.HelpBox(
                        "Specify a Camera and Character to generate thumbnails.",
                        MessageType.Warning
                    );
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.Space();

            // Execute
            bool canExecute = _targetFolder != null && 
                             (!_generateThumbnails || (_thumbnailCamera != null && _character != null));
            GUI.enabled = canExecute;
            if (GUILayout.Button("Create Poses", GUILayout.Height(30)))
            {
                CreatePosesFromAnimationClips();
            }
            GUI.enabled = true;

            // Results
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
                EditorUtility.DisplayDialog("Error", "Please select a folder.", "OK");
                return;
            }

            string folderPath = AssetDatabase.GetAssetPath(_targetFolder);
            
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                EditorUtility.DisplayDialog("Error", "Please select a valid folder.", "OK");
                return;
            }

            _createdPoses.Clear();
            _showResults = false;

            // Generate output folder
            string outputFolderPath = GenerateOutputFolderPath(folderPath);
            if (string.IsNullOrEmpty(outputFolderPath))
            {
                EditorUtility.DisplayDialog("Error", "Failed to generate output folder.", "OK");
                return;
            }

            try
            {
                // Recursively find AnimationClips in folder
                string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
                
                if (guids.Length == 0)
                {
                    EditorUtility.DisplayDialog("Info", "No AnimationClips found.", "OK");
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

                    // Compute relative path and output path
                    string relativePath = GetRelativePath(folderPath, clipPath);
                    string outputDirectory = Path.Combine(outputFolderPath, Path.GetDirectoryName(relativePath) ?? "").Replace("\\", "/");
                    
                    // Create output directory if needed
                    if (!AssetDatabase.IsValidFolder(outputDirectory))
                    {
                        CreateFolderRecursively(outputDirectory);
                    }

                    string poseAssetName = $"{clip.name}_Pose.asset";
                    string poseAssetPath = Path.Combine(outputDirectory, poseAssetName).Replace("\\", "/");

                    // Skip if existing and not overwriting
                    if (!_overwriteExisting && AssetDatabase.LoadAssetAtPath<PoseData>(poseAssetPath) != null)
                    {
                        skippedCount++;
                        continue;
                    }

                    // Create Pose asset
                    PoseData pose = ScriptableObject.CreateInstance<PoseData>();
                    pose.poseName = clip.name;
                    pose.animation = clip;
                    pose.snsTag = string.Empty;
                    
                    // Thumbnail
                    Texture2D thumbnailTexture = null;
                    if (_generateThumbnails && _thumbnailCamera != null && _character != null)
                    {
                        string thumbnailPath = Path.ChangeExtension(poseAssetPath, ".png");
                        GenerateThumbnail(clip, thumbnailPath);
                        
                        // Import and load thumbnail
                        AssetDatabase.ImportAsset(thumbnailPath);
                        thumbnailTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(thumbnailPath);
                        pose.thumbnail = thumbnailTexture;
                    }

                    // Save as asset
                    if (_overwriteExisting && AssetDatabase.LoadAssetAtPath<PoseData>(poseAssetPath) != null)
                    {
                        // Overwrite existing asset
                        PoseData existingPose = AssetDatabase.LoadAssetAtPath<PoseData>(poseAssetPath);
                        EditorUtility.CopySerialized(pose, existingPose);
                        EditorUtility.SetDirty(existingPose);
                        DestroyImmediate(pose);
                    }
                    else
                    {
                        // Create new
                        AssetDatabase.CreateAsset(pose, poseAssetPath);
                    }

                    _createdPoses.Add(poseAssetPath);
                    createdCount++;
                }

                EditorUtility.ClearProgressBar();

                // Refresh AssetDatabase
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                _showResults = true;

                // Result message
                string message = $"Done.\n\nCreated: {createdCount}";
                if (skippedCount > 0)
                {
                    message += $"\nSkipped: {skippedCount}";
                }

                EditorUtility.DisplayDialog("Success", message, "OK");

                Debug.Log($"[PoseCreator] {createdCount} Pose(s) created from Animation Clips in: {folderPath}");
                Debug.Log($"[PoseCreator] Output folder: {outputFolderPath}");
            }
            catch (System.Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"An error occurred:\n{ex.Message}", "OK");
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
            // Output folder name: source folder name + "_Poses"
            string folderName = Path.GetFileName(sourceFolderPath.TrimEnd('/'));
            string parentPath = Path.GetDirectoryName(sourceFolderPath)?.Replace("\\", "/") ?? "Assets";
            
            string outputFolderPath = Path.Combine(parentPath, $"{folderName}_Poses").Replace("\\", "/");
            
            // Create output folder if it does not exist
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
                // Use scene character
                GameObject tempCharacter = _character;
                
                // Get or add Animator
                Animator animator = tempCharacter.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = tempCharacter.AddComponent<Animator>();
                }
                
                // Sample animation at first frame
                clip.SampleAnimation(tempCharacter, 0f);
                
                // Save camera transform
                Vector3 originalCameraPosition = _thumbnailCamera.transform.position;
                Quaternion originalCameraRotation = _thumbnailCamera.transform.rotation;
                
                // Get Hips bone
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null)
                {
                    // Hips position
                    Vector3 hipsPosition = hips.position;
                    
                    // Place camera in front of Hips (2m along Z)
                    Vector3 cameraPosition = hipsPosition + new Vector3(0, 0, 2f);
                    _thumbnailCamera.transform.position = cameraPosition;
                    
                    // Look at Hips (keep Y)
                    _thumbnailCamera.transform.LookAt(new Vector3(hipsPosition.x, _thumbnailCamera.transform.position.y, hipsPosition.z));
                }
                
                // Create RenderTexture
                RenderTexture renderTexture = new RenderTexture(_thumbnailWidth, _thumbnailHeight, 24);
                RenderTexture previousRT = _thumbnailCamera.targetTexture;
                _thumbnailCamera.targetTexture = renderTexture;
                
                // Render with camera
                _thumbnailCamera.Render();
                
                // Convert to Texture2D
                RenderTexture.active = renderTexture;
                Texture2D thumbnail = new Texture2D(_thumbnailWidth, _thumbnailHeight, TextureFormat.RGB24, false);
                thumbnail.ReadPixels(new Rect(0, 0, _thumbnailWidth, _thumbnailHeight), 0, 0);
                thumbnail.Apply();
                
                // Save as PNG
                byte[] bytes = thumbnail.EncodeToPNG();
                string directoryPath = Path.GetDirectoryName(thumbnailPath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                File.WriteAllBytes(thumbnailPath, bytes);
                
                // Cleanup
                RenderTexture.active = null;
                _thumbnailCamera.targetTexture = previousRT;
                DestroyImmediate(renderTexture);
                DestroyImmediate(thumbnail);
                
                // Restore camera transform
                _thumbnailCamera.transform.position = originalCameraPosition;
                _thumbnailCamera.transform.rotation = originalCameraRotation;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PoseCreator] Failed to generate thumbnail for {clip.name}: {ex.Message}");
            }
        }
    }
}
