using UnityEditor;
using UnityEngine;
using System.IO;
using VSnap.Shared.Domain;


/// <summary>
/// Editor window that builds AssetBundles from a PoseLibrary.
/// The asset bundle name is the lowercased libraryName (e.g. "MyPoses" -> "myposes.bundle").
/// Build targets: Android and iOS. Opens the output folder after build.
/// </summary>
namespace VSnap.Editor
{
    public class PoseLibraryBundleBuilder : EditorWindow
    {
        private PoseLibrary poseLibrary;
        private string outputPath = "AssetBundles";
        
        private void OnGUI()
        {
            GUILayout.Label("PoseLibrary AssetBundle Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // PoseLibrary selection
            poseLibrary = (PoseLibrary)EditorGUILayout.ObjectField(
                "PoseLibrary", 
                poseLibrary, 
                typeof(PoseLibrary), 
                false
            );
            
            EditorGUILayout.Space();
            
            // Output path
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
            
            // Build button
            GUI.enabled = poseLibrary != null;
            if (GUILayout.Button("Build AssetBundles (Android & iOS)", GUILayout.Height(40)))
            {
                BuildAssetBundles();
            }
            GUI.enabled = true;
            
            EditorGUILayout.Space();
            
            // Info
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
            
            // Create output directory
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }
            
            // Get asset path
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
            
            // Save original asset name
            string originalAssetName = poseLibrary.name;
            string libraryNameLower = poseLibrary.libraryName.ToLower();
            string targetAssetName = libraryNameLower;
            
            // Temporarily rename asset to libraryName
            if (originalAssetName != targetAssetName)
            {
                string assetDirectory = Path.GetDirectoryName(assetPath);
                string newAssetPath = Path.Combine(assetDirectory, targetAssetName + ".asset");
                AssetDatabase.RenameAsset(assetPath, targetAssetName);
                AssetDatabase.SaveAssets();
                assetPath = newAssetPath;
            }
            
            // Set AssetBundle name
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            string bundleName = $"{libraryNameLower}.bundle";
            importer.assetBundleName = bundleName;
            
            // Collect all dependencies
            string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);
            foreach (string dep in dependencies)
            {
                AssetImporter depImporter = AssetImporter.GetAtPath(dep);
                if (depImporter != null)
                {
                    depImporter.assetBundleName = bundleName;
                }
            }
            
            // Build targets: Android and iOS
            BuildTarget[] buildTargets = new BuildTarget[]
            {
                BuildTarget.Android,
                BuildTarget.iOS
            };
            
            bool allSuccess = true;
            System.Text.StringBuilder resultMessage = new System.Text.StringBuilder();
            resultMessage.AppendLine("AssetBundle build completed!\n");
            
            // Build per platform
            foreach (BuildTarget target in buildTargets)
            {
                string platformPath = Path.Combine(outputPath, libraryNameLower, target.ToString());
                
                // Create platform directory
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
            
            // Clear AssetBundle names after build
            importer.assetBundleName = "";
            foreach (string dep in dependencies)
            {
                AssetImporter depImporter = AssetImporter.GetAtPath(dep);
                if (depImporter != null)
                {
                    depImporter.assetBundleName = "";
                }
            }
            
            // Restore asset name
            if (originalAssetName != targetAssetName)
            {
                AssetDatabase.RenameAsset(assetPath, originalAssetName);
                AssetDatabase.SaveAssets();
            }
            
            AssetDatabase.Refresh();
            
            // Show result
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
            
            // Open output folder
            EditorUtility.RevealInFinder(outputPath);
        }
    }
}
