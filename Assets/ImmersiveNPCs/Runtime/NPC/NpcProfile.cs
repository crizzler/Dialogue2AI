using System;
using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Defines an NPC's identity, personality, and quality settings.
    /// </summary>
    [CreateAssetMenu(fileName = "NpcProfile", menuName = "Immersive NPCs/NPC Profile")]
    public class NpcProfile : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique identifier used in dialogue scripts (e.g., 'merchant_greta')")]
        public string npcId;
        
        [Tooltip("Display name shown in UI")]
        public string displayName;
        
        [TextArea(3, 10)]
        [Tooltip("Core persona prompt injected into system message")]
        public string personaPrompt;
        
        [TextArea(2, 5)]
        [Tooltip("Speaking style guidance (e.g., 'formal medieval speech')")]
        public string speakingStyle;
        
        [Header("Quality & Performance")]
        [Tooltip("Override global quality preset for this NPC. None = use global setting.")]
        public QualityPresetOverride qualityPresetOverride = QualityPresetOverride.UseGlobal;
        
        [Range(0.5f, 2.0f)]
        [Tooltip("Multiplier for token budget (1.0 = normal, 1.5 = 50% more tokens)")]
        public float tokenBudgetMultiplier = 1.0f;
        
        [Header("Personality Traits")]
        [Tooltip("Static personality traits that don't change")]
        public List<PersonalityTrait> traits = new List<PersonalityTrait>();
        
        [Header("Relationships")]
        [Tooltip("Default relationship values toward other NPCs/factions")]
        public List<RelationshipDefault> relationships = new List<RelationshipDefault>();
        
        [Header("Voice & Presentation")]
        [Tooltip("Optional TTS voice profile reference")]
        public string voiceProfileId;
        
        [Tooltip("Portrait sprite for dialogue UI")]
        public Sprite portrait;
        
        [Header("Advanced")]
        [Tooltip("Custom key-value pairs for game-specific data")]
        public List<CustomProperty> customProperties = new List<CustomProperty>();
        
        [Tooltip("Tags for filtering/grouping NPCs")]
        public List<string> tags = new List<string>();
        
        // === Runtime Helpers ===
        
        /// <summary>
        /// Get effective quality preset, considering override
        /// </summary>
        public QualityPreset GetEffectivePreset(QualityPreset globalPreset)
        {
            return qualityPresetOverride == QualityPresetOverride.UseGlobal 
                ? globalPreset 
                : (QualityPreset)((int)qualityPresetOverride - 1);
        }
        
        /// <summary>
        /// Get a trait value by name
        /// </summary>
        public string GetTrait(string traitName)
        {
            var trait = traits.Find(t => t.name.Equals(traitName, StringComparison.OrdinalIgnoreCase));
            return trait?.value;
        }
        
        /// <summary>
        /// Get default relationship value toward a target
        /// </summary>
        public float GetRelationship(string targetId)
        {
            var rel = relationships.Find(r => r.targetId == targetId);
            return rel?.defaultValue ?? 0f;
        }
        
        /// <summary>
        /// Get a custom property value
        /// </summary>
        public string GetCustomProperty(string key)
        {
            var prop = customProperties.Find(p => p.key == key);
            return prop?.value;
        }
        
        /// <summary>
        /// Check if NPC has a specific tag
        /// </summary>
        public bool HasTag(string tag)
        {
            return tags.Contains(tag);
        }
    }
    
    /// <summary>
    /// Quality preset override options for per-NPC control
    /// </summary>
    public enum QualityPresetOverride
    {
        [Tooltip("Use the global quality preset from settings")]
        UseGlobal = 0,
        
        [Tooltip("2K tokens, no planning - fast responses")]
        FastSmall = 1,
        
        [Tooltip("4K tokens with planning - balanced")]
        Balanced = 2,
        
        [Tooltip("8K tokens, rich memory - for important NPCs")]
        DeepConversation = 3,
        
        [Tooltip("16K tokens, full pipeline - for cutscenes")]
        CinematicQuality = 4
    }
    
    /// <summary>
    /// A personality trait with name, value, and optional numeric intensity
    /// </summary>
    [Serializable]
    public class PersonalityTrait
    {
        [Tooltip("Trait name (e.g., 'patience', 'humor', 'formality')")]
        public string name;
        
        [Tooltip("Trait value or description")]
        public string value;
        
        [Range(-1f, 1f)]
        [Tooltip("Numeric intensity (-1 to 1) for traits like 'aggressive' vs 'peaceful'")]
        public float intensity;
    }
    
    /// <summary>
    /// Default relationship value toward another NPC or faction
    /// </summary>
    [Serializable]
    public class RelationshipDefault
    {
        [Tooltip("Target NPC ID or faction name")]
        public string targetId;
        
        [Range(-100f, 100f)]
        [Tooltip("Default relationship value (-100 hostile to +100 friendly)")]
        public float defaultValue;
    }
    
    /// <summary>
    /// Custom key-value property for game-specific data
    /// </summary>
    [Serializable]
    public class CustomProperty
    {
        public string key;
        public string value;
    }
}
