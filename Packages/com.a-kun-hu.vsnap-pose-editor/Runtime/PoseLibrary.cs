using System.Collections.Generic;
using UnityEngine;

namespace VSnap.Shared.Domain
{
    [CreateAssetMenu(fileName = "PoseLibrary", menuName = "VSnap/PoseLibrary")]
    public class PoseLibrary : ScriptableObject
    {
        public string libraryName = "New Pose Library"; //AssetBundle名にも使用される
        public List<PoseGroup> poseGroups = new();
    }
}