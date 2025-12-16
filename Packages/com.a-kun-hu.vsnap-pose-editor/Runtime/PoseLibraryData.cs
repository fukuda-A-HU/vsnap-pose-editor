using System;
using System.Collections.Generic;

namespace VSnap.Shared.Domain
{
    [Serializable]
    public class ShopInfo
    {
        public string url = "";
        public string name = "";
        public string snsTag = "";
    }

    [Serializable]
    public class Pose
    {
        public string name = "";
        public string guid = "";
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public class PoseLibrary
    {
        public ShopInfo shopInfo = new ShopInfo();
        public List<Pose> poses = new List<Pose>();
    }
}
