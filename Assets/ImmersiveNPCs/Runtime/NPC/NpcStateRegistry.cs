using System.Collections.Generic;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Central registry for all NPC runtime states.
    /// Manages NpcStateStore instances for all NPCs.
    /// </summary>
    public class NpcStateRegistry
    {
        private readonly NpcProfileDatabase profileDatabase;
        private readonly Dictionary<string, NpcStateStore> states = new Dictionary<string, NpcStateStore>();
        
        public NpcStateRegistry(NpcProfileDatabase profileDatabase)
        {
            this.profileDatabase = profileDatabase;
        }
        
        /// <summary>
        /// Get or create state for an NPC
        /// </summary>
        public NpcStateStore GetOrCreate(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                npcId = "default";
            }
            
            if (!states.TryGetValue(npcId, out var state))
            {
                NpcProfile profile = profileDatabase?.FindProfile(npcId);
                state = new NpcStateStore(npcId, profile);
                states[npcId] = state;
            }
            return state;
        }
        
        /// <summary>
        /// Try to get existing state (returns null if not found)
        /// </summary>
        public NpcStateStore TryGet(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            states.TryGetValue(npcId, out var state);
            return state;
        }
        
        /// <summary>
        /// Check if state exists for an NPC
        /// </summary>
        public bool HasState(string npcId)
        {
            return !string.IsNullOrEmpty(npcId) && states.ContainsKey(npcId);
        }
        
        /// <summary>
        /// Get all active NPC states
        /// </summary>
        public IEnumerable<NpcStateStore> GetAll()
        {
            return states.Values;
        }
        
        /// <summary>
        /// Get all NPC IDs with active states
        /// </summary>
        public IEnumerable<string> GetAllNpcIds()
        {
            return states.Keys;
        }
        
        /// <summary>
        /// Remove state for an NPC
        /// </summary>
        public bool Remove(string npcId)
        {
            return states.Remove(npcId);
        }
        
        /// <summary>
        /// Clear all NPC states
        /// </summary>
        public void Clear()
        {
            states.Clear();
        }
        
        /// <summary>
        /// Get count of active states
        /// </summary>
        public int Count => states.Count;
        
        /// <summary>
        /// Export all states for saving
        /// </summary>
        public Dictionary<string, Dictionary<string, object>> ToSaveData()
        {
            var result = new Dictionary<string, Dictionary<string, object>>();
            foreach (var kv in states)
            {
                result[kv.Key] = kv.Value.ToSaveData();
            }
            return result;
        }
        
        /// <summary>
        /// Import states from save data
        /// </summary>
        public void FromSaveData(Dictionary<string, Dictionary<string, object>> data)
        {
            if (data == null) return;
            
            foreach (var kv in data)
            {
                var state = GetOrCreate(kv.Key);
                state.FromSaveData(kv.Value);
            }
        }
        
        /// <summary>
        /// Reset all states to defaults
        /// </summary>
        public void ResetAll()
        {
            foreach (var state in states.Values)
            {
                state.Reset();
            }
        }
    }
}
