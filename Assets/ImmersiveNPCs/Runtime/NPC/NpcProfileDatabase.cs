using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Database of all NPC profiles in the game.
    /// </summary>
    [CreateAssetMenu(fileName = "NpcProfileDatabase", menuName = "Immersive NPCs/NPC Profile Database")]
    public class NpcProfileDatabase : ScriptableObject
    {
        [Tooltip("All NPC profiles in the game")]
        public List<NpcProfile> profiles = new List<NpcProfile>();
        
        private Dictionary<string, NpcProfile> lookup;
        
        /// <summary>
        /// Find a profile by NPC ID
        /// </summary>
        public NpcProfile FindProfile(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            
            if (lookup == null)
            {
                BuildLookup();
            }
            
            lookup.TryGetValue(npcId, out NpcProfile profile);
            return profile;
        }
        
        /// <summary>
        /// Find all profiles with a specific tag
        /// </summary>
        public List<NpcProfile> FindByTag(string tag)
        {
            var result = new List<NpcProfile>();
            foreach (var profile in profiles)
            {
                if (profile != null && profile.HasTag(tag))
                {
                    result.Add(profile);
                }
            }
            return result;
        }
        
        /// <summary>
        /// Get all NPC IDs in the database
        /// </summary>
        public List<string> GetAllNpcIds()
        {
            var ids = new List<string>();
            foreach (var profile in profiles)
            {
                if (profile != null && !string.IsNullOrEmpty(profile.npcId))
                {
                    ids.Add(profile.npcId);
                }
            }
            return ids;
        }
        
        /// <summary>
        /// Rebuild the lookup dictionary
        /// </summary>
        public void BuildLookup()
        {
            lookup = new Dictionary<string, NpcProfile>();
            foreach (var profile in profiles)
            {
                if (profile != null && !string.IsNullOrEmpty(profile.npcId))
                {
                    if (!lookup.ContainsKey(profile.npcId))
                    {
                        lookup[profile.npcId] = profile;
                    }
                    else
                    {
                        Debug.LogWarning($"[NpcProfileDatabase] Duplicate NPC ID: {profile.npcId}");
                    }
                }
            }
        }
        
        private void OnEnable()
        {
            BuildLookup();
        }
        
        private void OnValidate()
        {
            // Rebuild lookup when changed in editor
            lookup = null;
        }
        
#if UNITY_EDITOR
        /// <summary>
        /// Auto-populate database from all NpcProfile assets in project
        /// </summary>
        [ContextMenu("Auto-Populate from Project")]
        private void AutoPopulate()
        {
            profiles.Clear();
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:NpcProfile");
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                NpcProfile profile = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcProfile>(path);
                if (profile != null)
                {
                    profiles.Add(profile);
                }
            }
            BuildLookup();
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[NpcProfileDatabase] Found {profiles.Count} NPC profiles.");
        }
#endif
    }
}
