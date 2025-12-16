using System;
using System.Collections.Generic;

namespace VSnap.Shared.Domain
{
    [Serializable]
    public class ShopInfo
    {
        public string url;
        public string name;
        public string snsTag;
    }

    [Serializable]
    public class Pose
    {
        public string name;
        public string guid;
        public List<string> tags;
    }

    [Serializable]
    public class PoseLibrary
    {
        public List<ShopInfo> shopInfo;
        public List<Pose> poses;
    }
}
