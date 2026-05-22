#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using ImmersiveNPCs;

namespace ImmersiveNPCs.Editor
{
    internal static class AISettingsAssetUtility
    {
        private const string ResourceFolder = "Assets/ImmersiveNPCs/Resources";
        private const string AssetPath = ResourceFolder + "/AIConversationSettings.asset";

        public static AIConversationSettings LoadOrCreate()
        {
            AIConversationSettings asset = AssetDatabase.LoadAssetAtPath<AIConversationSettings>(AssetPath);
            if (asset != null)
            {
                return asset;
            }

            asset = FindAnySettings();
            if (asset != null)
            {
                return asset;
            }

            return CreateSettingsAsset();
        }

        public static AIConversationSettings FindAnySettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:AIConversationSettings");
            if (guids.Length == 0)
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<AIConversationSettings>(path);
        }

        public static AIConversationSettings CreateSettingsAsset()
        {
            if (!Directory.Exists(ResourceFolder))
            {
                Directory.CreateDirectory(ResourceFolder);
            }

            AIConversationSettings asset = ScriptableObject.CreateInstance<AIConversationSettings>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = asset;
            return asset;
        }
    }
}
#endif
