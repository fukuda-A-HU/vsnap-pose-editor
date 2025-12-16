using System.Collections.Generic;
using UnityEngine;

namespace VSnap.Shared.Domain
{
    [CreateAssetMenu(fileName = "PoseLibrary", menuName = "VSnap/PoseLibrary")]
    public class PoseLibrary : ScriptableObject
    {
        public string libraryName = "New Pose Library";
        public List<PoseGroup> poseGroups = new();
    }
}