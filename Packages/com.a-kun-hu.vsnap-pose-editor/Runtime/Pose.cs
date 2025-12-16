using UnityEngine;

namespace VSnap.Shared.Domain
{
    [CreateAssetMenu(fileName = "PoseData", menuName = "VSnap/Pose")]
    public class Pose : ScriptableObject
    {
        public string poseName = "New Pose";
        public AnimationClip animation = null;
        public Texture2D thumbnail = null;
        public string snsTag = string.Empty;
    }
}
