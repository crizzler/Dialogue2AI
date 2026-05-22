#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

namespace ImmersiveNPCs.Editor
{
    /// <summary>
    /// Automatically invalidates the dialogue cache when relevant assets are modified.
    /// This ensures stale cached responses don't persist after NPC profile changes.
    /// </summary>
    public class CacheInvalidator : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool shouldInvalidate = false;
            
            foreach (string path in importedAssets)
            {
                if (ShouldInvalidateForAsset(path))
                {
                    shouldInvalidate = true;
                    break;
                }
            }
            
            if (!shouldInvalidate)
            {
                foreach (string path in deletedAssets)
                {
                    if (ShouldInvalidateForAsset(path))
                    {
                        shouldInvalidate = true;
                        break;
                    }
                }
            }
            
            if (shouldInvalidate)
            {
                InvalidateCache();
            }
        }
        
        private static bool ShouldInvalidateForAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            
            // Check if it's an NpcProfile or GlobalWorldState asset
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null) return false;
            
            return asset is NpcProfile || 
                   asset is NpcProfileDatabase || 
                   asset is GlobalWorldState;
        }
        
        /// <summary>
        /// Clears the disk cache to ensure fresh responses after asset changes.
        /// </summary>
        public static void InvalidateCache()
        {
            // Get cache path from settings if available
            var settings = AISettingsLocator.Load();
            string cachePath = "Library/ImmersiveNPCs/Cache";
            
            if (settings != null && !string.IsNullOrEmpty(settings.diskCachePath))
            {
                cachePath = PathUtility.ResolveProjectPath(settings.diskCachePath);
            }
            
            if (Directory.Exists(cachePath))
            {
                try
                {
                    int fileCount = Directory.GetFiles(cachePath, "*.json").Length;
                    if (fileCount > 0)
                    {
                        foreach (string file in Directory.GetFiles(cachePath, "*.json"))
                        {
                            File.Delete(file);
                        }
                        Debug.Log($"[ImmersiveNPCs] Cache invalidated: cleared {fileCount} cached responses due to asset changes.");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ImmersiveNPCs] Failed to clear cache: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Menu item to manually clear the cache.
        /// </summary>
        [MenuItem("Tools/Immersive NPCs/Clear Dialogue Cache")]
        public static void ClearCacheMenuItem()
        {
            InvalidateCache();
            Debug.Log("[ImmersiveNPCs] Dialogue cache cleared manually.");
        }
        
        /// <summary>
        /// Menu item to show cache info.
        /// </summary>
        [MenuItem("Tools/Immersive NPCs/Show Cache Info")]
        public static void ShowCacheInfo()
        {
            var settings = AISettingsLocator.Load();
            string cachePath = "Library/ImmersiveNPCs/Cache";
            
            if (settings != null && !string.IsNullOrEmpty(settings.diskCachePath))
            {
                cachePath = PathUtility.ResolveProjectPath(settings.diskCachePath);
            }
            
            if (Directory.Exists(cachePath))
            {
                string[] files = Directory.GetFiles(cachePath, "*.json");
                long totalSize = 0;
                foreach (string file in files)
                {
                    totalSize += new FileInfo(file).Length;
                }
                
                Debug.Log($"[ImmersiveNPCs] Cache location: {cachePath}\n" +
                         $"Cached responses: {files.Length}\n" +
                         $"Total size: {totalSize / 1024f:F1} KB");
            }
            else
            {
                Debug.Log($"[ImmersiveNPCs] Cache directory does not exist: {cachePath}");
            }
        }
    }
}
#endif
