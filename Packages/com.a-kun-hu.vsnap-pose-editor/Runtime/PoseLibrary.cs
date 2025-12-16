using System.Collections.Generic;
using UnityEngine;

namespace VSnap.Shared.Domain
{
    [CreateAssetMenu(fileName = "PoseLibrary", menuName = "VSnap/PoseLibrary")]
    public class PoseLibrary : ScriptableObject
    {
        public List<PoseGroup> poseGroups = new();
    }
}