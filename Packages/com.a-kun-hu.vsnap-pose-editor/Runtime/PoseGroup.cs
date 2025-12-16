using System.Collections.Generic;
using UnityEngine;

namespace VSnap.Shared.Domain
{
    [CreateAssetMenu(fileName = "PoseGroup", menuName = "VSnap/PoseGroup")]
    public class PoseGroup : ScriptableObject
    {
        public string groupName = "New Pose Group";
        public List<Pose> poses = new(); // Poseはグループ間で同じものが使い回されることがある。
        public Texture2D thumbnail = null;
        public string linkUrl = string.Empty; // ショップや詳細ページへのリンク
        public string linkText = string.Empty; // リンクの表示テキスト
    }
}
