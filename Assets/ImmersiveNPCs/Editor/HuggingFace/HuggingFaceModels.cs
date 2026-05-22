#if UNITY_EDITOR
using System;

namespace ImmersiveNPCs.Editor
{
    [Serializable]
    internal class HuggingFaceModelList
    {
        public HuggingFaceModel[] items;
    }

    [Serializable]
    internal class HuggingFaceModel
    {
        public string _id;
        public string id;
        public string modelId;
        public string author;
        public string pipeline_tag;
        public string library_name;
        public string[] tags;
        public int downloads;
        public int likes;
        public bool gated;
        public bool privateModel;
        public string lastModified;
    }

    [Serializable]
    internal class HuggingFaceFileList
    {
        public HuggingFaceFile[] items;
    }

    [Serializable]
    internal class HuggingFaceFile
    {
        public string path;
        public string type;
        public long size;
    }
}
#endif
