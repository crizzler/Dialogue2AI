using System;
using System.Collections.Generic;
using System.Text;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Runtime state for a single NPC. Not serialized to asset - lives in memory.
    /// Can be persisted via save system using ToSaveData()/FromSaveData().
    /// </summary>
    public class NpcStateStore
    {
        public string NpcId { get; }
        public NpcProfile Profile { get; }
        
        // Dynamic state
        public NpcMood CurrentMood { get; set; } = NpcMood.Neutral;
        public float TrustLevel { get; set; } = 0f; // -100 to +100
        public float FearLevel { get; set; } = 0f;  // 0 to 100
        public DateTime LastInteraction { get; set; }
        public int InteractionCount { get; set; }
        
        // Recent conversation context
        public List<string> RecentTopics { get; } = new List<string>();
        public string LastPlayerAction { get; set; }
        public string LastNpcResponse { get; set; }
        
        // Relationship overrides (runtime changes to defaults)
        public Dictionary<string, float> RelationshipDeltas { get; } = new Dictionary<string, float>();
        
        // Custom variables for game-specific state
        public Dictionary<string, object> CustomVars { get; } = new Dictionary<string, object>();
        
        private const int MaxRecentTopics = 10;
        
        public NpcStateStore(string npcId, NpcProfile profile)
        {
            NpcId = npcId;
            Profile = profile;
        }
        
        /// <summary>
        /// Add a topic to recent topics (FIFO)
        /// </summary>
        public void AddTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return;
            
            // Remove if already exists (will re-add at front)
            RecentTopics.Remove(topic);
            
            RecentTopics.Insert(0, topic);
            if (RecentTopics.Count > MaxRecentTopics)
            {
                RecentTopics.RemoveAt(RecentTopics.Count - 1);
            }
        }
        
        /// <summary>
        /// Modify trust level (clamped to -100 to +100)
        /// </summary>
        public void ModifyTrust(float delta)
        {
            TrustLevel = Math.Clamp(TrustLevel + delta, -100f, 100f);
        }
        
        /// <summary>
        /// Modify fear level (clamped to 0 to 100)
        /// </summary>
        public void ModifyFear(float delta)
        {
            FearLevel = Math.Clamp(FearLevel + delta, 0f, 100f);
        }
        
        /// <summary>
        /// Modify relationship delta with a target
        /// </summary>
        public void ModifyRelationship(string targetId, float delta)
        {
            if (!RelationshipDeltas.ContainsKey(targetId))
            {
                RelationshipDeltas[targetId] = 0f;
            }
            RelationshipDeltas[targetId] += delta;
        }
        
        /// <summary>
        /// Get effective relationship (base from profile + runtime delta)
        /// </summary>
        public float GetEffectiveRelationship(string targetId)
        {
            float baseValue = Profile?.GetRelationship(targetId) ?? 0f;
            RelationshipDeltas.TryGetValue(targetId, out float delta);
            return Math.Clamp(baseValue + delta, -100f, 100f);
        }
        
        /// <summary>
        /// Set a custom variable
        /// </summary>
        public void SetVar<T>(string key, T value)
        {
            CustomVars[key] = value;
        }
        
        /// <summary>
        /// Get a custom variable
        /// </summary>
        public T GetVar<T>(string key, T defaultValue = default)
        {
            if (CustomVars.TryGetValue(key, out object val) && val is T typed)
            {
                return typed;
            }
            return defaultValue;
        }
        
        /// <summary>
        /// Check if a custom variable exists
        /// </summary>
        public bool HasVar(string key)
        {
            return CustomVars.ContainsKey(key);
        }
        
        /// <summary>
        /// Build context string for prompt injection (Tier 1 - Session tier)
        /// </summary>
        public string BuildNpcStateBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[NPC State: {Profile?.displayName ?? NpcId}]");
            sb.AppendLine($"Mood: {CurrentMood}, Trust: {TrustLevel:F0}, Fear: {FearLevel:F0}");
            
            if (RecentTopics.Count > 0)
            {
                int count = Math.Min(3, RecentTopics.Count);
                sb.AppendLine($"Recent topics: {string.Join(", ", RecentTopics.GetRange(0, count))}");
            }
            
            if (!string.IsNullOrEmpty(LastPlayerAction))
            {
                sb.AppendLine($"Last player action: {LastPlayerAction}");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Serialize to dictionary for save system
        /// </summary>
        public Dictionary<string, object> ToSaveData()
        {
            return new Dictionary<string, object>
            {
                ["npcId"] = NpcId,
                ["mood"] = (int)CurrentMood,
                ["trust"] = TrustLevel,
                ["fear"] = FearLevel,
                ["lastInteraction"] = LastInteraction.ToString("o"),
                ["interactionCount"] = InteractionCount,
                ["lastPlayerAction"] = LastPlayerAction,
                ["lastNpcResponse"] = LastNpcResponse,
                ["recentTopics"] = new List<string>(RecentTopics),
                ["relationshipDeltas"] = new Dictionary<string, float>(RelationshipDeltas),
                ["customVars"] = new Dictionary<string, object>(CustomVars)
            };
        }
        
        /// <summary>
        /// Restore from save data
        /// </summary>
        public void FromSaveData(Dictionary<string, object> data)
        {
            if (data == null) return;
            
            if (data.TryGetValue("mood", out var mood)) 
                CurrentMood = (NpcMood)Convert.ToInt32(mood);
            
            if (data.TryGetValue("trust", out var trust)) 
                TrustLevel = Convert.ToSingle(trust);
            
            if (data.TryGetValue("fear", out var fear)) 
                FearLevel = Convert.ToSingle(fear);
            
            if (data.TryGetValue("lastInteraction", out var li) && li is string liStr) 
                LastInteraction = DateTime.Parse(liStr);
            
            if (data.TryGetValue("interactionCount", out var ic)) 
                InteractionCount = Convert.ToInt32(ic);
            
            if (data.TryGetValue("lastPlayerAction", out var lpa) && lpa is string)
                LastPlayerAction = (string)lpa;
            
            if (data.TryGetValue("lastNpcResponse", out var lnr) && lnr is string)
                LastNpcResponse = (string)lnr;
            
            if (data.TryGetValue("recentTopics", out var topics) && topics is List<string> topicList)
            {
                RecentTopics.Clear();
                RecentTopics.AddRange(topicList);
            }
            
            if (data.TryGetValue("relationshipDeltas", out var rels) && rels is Dictionary<string, float> relDict)
            {
                RelationshipDeltas.Clear();
                foreach (var kv in relDict) 
                    RelationshipDeltas[kv.Key] = kv.Value;
            }
            
            if (data.TryGetValue("customVars", out var vars) && vars is Dictionary<string, object> varDict)
            {
                CustomVars.Clear();
                foreach (var kv in varDict) 
                    CustomVars[kv.Key] = kv.Value;
            }
        }
        
        /// <summary>
        /// Reset to default state
        /// </summary>
        public void Reset()
        {
            CurrentMood = NpcMood.Neutral;
            TrustLevel = 0f;
            FearLevel = 0f;
            InteractionCount = 0;
            LastPlayerAction = null;
            LastNpcResponse = null;
            RecentTopics.Clear();
            RelationshipDeltas.Clear();
            CustomVars.Clear();
        }
    }
    
    /// <summary>
    /// NPC mood states
    /// </summary>
    public enum NpcMood
    {
        Hostile = -2,
        Annoyed = -1,
        Neutral = 0,
        Friendly = 1,
        Excited = 2
    }
}
