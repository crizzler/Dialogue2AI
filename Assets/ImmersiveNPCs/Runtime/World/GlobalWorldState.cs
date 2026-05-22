using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ImmersiveNPCs
{
    /// <summary>
    /// ScriptableObject containing global world state for NPC context.
    /// </summary>
    [CreateAssetMenu(fileName = "GlobalWorldState", menuName = "Immersive NPCs/Global World State")]
    public class GlobalWorldState : ScriptableObject
    {
        [Header("Game Identity")]
        [Tooltip("Name of the game/world")]
        public string gameName = "My Game";
        
        [TextArea(3, 10)]
        [Tooltip("Base context always included in prompts")]
        public string baseContext;
        
        [Header("Narrative State")]
        [Tooltip("Current chapter/act for narrative context")]
        public string currentChapter;
        
        [Tooltip("Major plot points that have occurred")]
        public List<string> completedPlotPoints = new List<string>();
        
        [TextArea(2, 5)]
        [Tooltip("Lore constraints NPCs must respect")]
        public string loreConstraints;
        
        [Header("World Facts")]
        [Tooltip("Current time of day")]
        public TimeOfDay timeOfDay = TimeOfDay.Day;
        
        [Tooltip("Current weather")]
        public Weather weather = Weather.Clear;
        
        [Tooltip("Current location/region name")]
        public string currentLocation;
        
        [Header("Factions")]
        public List<FactionState> factions = new List<FactionState>();
        
        [Header("Custom Facts")]
        [Tooltip("Arbitrary key-value facts about the world state")]
        public List<WorldFact> customFacts = new List<WorldFact>();
        
        // === Runtime Methods ===
        
        /// <summary>
        /// Set or update a world fact
        /// </summary>
        public void SetFact(string key, string value)
        {
            var existing = customFacts.Find(f => f.key == key);
            if (existing != null)
            {
                existing.value = value;
                existing.lastModified = DateTime.UtcNow.ToString("o");
            }
            else
            {
                customFacts.Add(new WorldFact 
                { 
                    key = key, 
                    value = value, 
                    lastModified = DateTime.UtcNow.ToString("o") 
                });
            }
        }
        
        /// <summary>
        /// Get a world fact value
        /// </summary>
        public string GetFact(string key)
        {
            return customFacts.Find(f => f.key == key)?.value;
        }
        
        /// <summary>
        /// Check if a fact exists
        /// </summary>
        public bool HasFact(string key)
        {
            return customFacts.Exists(f => f.key == key);
        }
        
        /// <summary>
        /// Remove a world fact
        /// </summary>
        public bool RemoveFact(string key)
        {
            return customFacts.RemoveAll(f => f.key == key) > 0;
        }
        
        /// <summary>
        /// Get faction state by ID
        /// </summary>
        public FactionState GetFaction(string factionId)
        {
            return factions.Find(f => f.factionId == factionId);
        }
        
        /// <summary>
        /// Add a completed plot point
        /// </summary>
        public void CompleteplotPoint(string plotPoint)
        {
            if (!completedPlotPoints.Contains(plotPoint))
            {
                completedPlotPoints.Add(plotPoint);
            }
        }
        
        /// <summary>
        /// Check if a plot point has been completed
        /// </summary>
        public bool IsPlotPointComplete(string plotPoint)
        {
            return completedPlotPoints.Contains(plotPoint);
        }
        
        /// <summary>
        /// Build context string for prompt injection (Tier 0 - Identity tier)
        /// </summary>
        public string BuildWorldContextBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[World: {gameName}]");
            
            if (!string.IsNullOrEmpty(currentLocation))
                sb.AppendLine($"Location: {currentLocation}");
            
            sb.AppendLine($"Time: {timeOfDay}, Weather: {weather}");
            
            if (!string.IsNullOrEmpty(currentChapter))
                sb.AppendLine($"Chapter: {currentChapter}");
            
            if (!string.IsNullOrEmpty(baseContext))
            {
                sb.AppendLine();
                sb.AppendLine(baseContext);
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Build faction context for prompt injection
        /// </summary>
        public string BuildFactionContextBlock()
        {
            if (factions.Count == 0) return string.Empty;
            
            var sb = new StringBuilder();
            sb.AppendLine("[Faction Status]");
            
            foreach (var faction in factions)
            {
                string stance = faction.isHostile ? "hostile" : 
                    (faction.playerReputation > 50 ? "friendly" : "neutral");
                sb.AppendLine($"- {faction.displayName}: {stance} (reputation: {faction.playerReputation:F0})");
            }
            
            return sb.ToString();
        }
    }
    
    /// <summary>
    /// Time of day enum
    /// </summary>
    public enum TimeOfDay 
    { 
        Dawn, 
        Day, 
        Dusk, 
        Night 
    }
    
    /// <summary>
    /// Weather conditions enum
    /// </summary>
    public enum Weather 
    { 
        Clear, 
        Cloudy, 
        Rain, 
        Storm, 
        Snow, 
        Fog 
    }
    
    /// <summary>
    /// Faction state with player reputation
    /// </summary>
    [Serializable]
    public class FactionState
    {
        public string factionId;
        public string displayName;
        
        [Range(-100f, 100f)]
        [Tooltip("Player's reputation with this faction")]
        public float playerReputation;
        
        [Tooltip("Is this faction currently hostile to player?")]
        public bool isHostile;
        
        [TextArea(1, 3)]
        [Tooltip("Brief description of faction")]
        public string description;
    }
    
    /// <summary>
    /// A world fact with key, value, and modification timestamp
    /// </summary>
    [Serializable]
    public class WorldFact
    {
        public string key;
        public string value;
        [HideInInspector] public string lastModified;
    }
}
