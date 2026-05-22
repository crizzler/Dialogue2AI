using System;
using System.Collections.Generic;
using System.Linq;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Persistent memory store for structured memory events per NPC.
    /// Replaces raw chat log storage with commit-worthy events only.
    /// </summary>
    public sealed class MemoryStore
    {
        private readonly Dictionary<string, List<MemoryEvent>> npcEvents = new Dictionary<string, List<MemoryEvent>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MemoryEvent> globalEvents = new List<MemoryEvent>();
        private readonly object sync = new object();
        private int maxEventsPerNpc = 128;
        private int maxGlobalEvents = 256;
        
        /// <summary>
        /// Configure max events to retain.
        /// </summary>
        public void Configure(int maxPerNpc, int maxGlobal)
        {
            maxEventsPerNpc = Math.Max(16, maxPerNpc);
            maxGlobalEvents = Math.Max(32, maxGlobal);
        }
        
        /// <summary>
        /// Adds a memory event. If importance-weighted capacity exceeded, lowest importance is evicted.
        /// </summary>
        public void Add(MemoryEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.summary))
            {
                return;
            }
            
            lock (sync)
            {
                // Add to NPC-specific list
                if (!string.IsNullOrEmpty(evt.npcId))
                {
                    if (!npcEvents.TryGetValue(evt.npcId, out var list))
                    {
                        list = new List<MemoryEvent>();
                        npcEvents[evt.npcId] = list;
                    }
                    
                    list.Add(evt);
                    
                    // Evict low-importance events if over capacity
                    if (list.Count > maxEventsPerNpc)
                    {
                        EvictLowestImportance(list, maxEventsPerNpc);
                    }
                }
                else
                {
                    // Global event
                    globalEvents.Add(evt);
                    
                    if (globalEvents.Count > maxGlobalEvents)
                    {
                        EvictLowestImportance(globalEvents, maxGlobalEvents);
                    }
                }
            }
        }
        
        /// <summary>
        /// Gets all persistent facts for an NPC (isPersistentFact = true).
        /// </summary>
        public List<MemoryEvent> GetPersistentFacts(string npcId)
        {
            lock (sync)
            {
                var results = new List<MemoryEvent>();
                
                // Add global persistent facts
                foreach (var evt in globalEvents)
                {
                    if (evt.isPersistentFact)
                    {
                        results.Add(evt);
                    }
                }
                
                // Add NPC-specific persistent facts
                if (!string.IsNullOrEmpty(npcId) && npcEvents.TryGetValue(npcId, out var list))
                {
                    foreach (var evt in list)
                    {
                        if (evt.isPersistentFact)
                        {
                            results.Add(evt);
                        }
                    }
                }
                
                return results;
            }
        }
        
        /// <summary>
        /// Gets recent events for an NPC, sorted by recency.
        /// </summary>
        public List<MemoryEvent> GetRecentEvents(string npcId, int maxCount)
        {
            lock (sync)
            {
                var results = new List<MemoryEvent>();
                
                if (!string.IsNullOrEmpty(npcId) && npcEvents.TryGetValue(npcId, out var list))
                {
                    // Sort by timestamp descending and take top N
                    var recent = list.OrderByDescending(e => e.timestampUtc).Take(maxCount);
                    results.AddRange(recent);
                }
                
                return results;
            }
        }
        
        /// <summary>
        /// Gets events by type for an NPC.
        /// </summary>
        public List<MemoryEvent> GetEventsByType(string npcId, MemoryEventType type)
        {
            lock (sync)
            {
                var results = new List<MemoryEvent>();
                
                // Check global
                foreach (var evt in globalEvents)
                {
                    if (evt.eventType == type)
                    {
                        results.Add(evt);
                    }
                }
                
                // Check NPC-specific
                if (!string.IsNullOrEmpty(npcId) && npcEvents.TryGetValue(npcId, out var list))
                {
                    foreach (var evt in list)
                    {
                        if (evt.eventType == type)
                        {
                            results.Add(evt);
                        }
                    }
                }
                
                return results;
            }
        }
        
        /// <summary>
        /// Queries events by importance threshold.
        /// </summary>
        public List<MemoryEvent> QueryByImportance(string npcId, float minImportance, int maxCount)
        {
            lock (sync)
            {
                var candidates = new List<MemoryEvent>();
                
                // Collect matching events
                foreach (var evt in globalEvents)
                {
                    if (evt.importance >= minImportance)
                    {
                        candidates.Add(evt);
                    }
                }
                
                if (!string.IsNullOrEmpty(npcId) && npcEvents.TryGetValue(npcId, out var list))
                {
                    foreach (var evt in list)
                    {
                        if (evt.importance >= minImportance)
                        {
                            candidates.Add(evt);
                        }
                    }
                }
                
                // Sort by importance descending, then recency
                return candidates
                    .OrderByDescending(e => e.importance)
                    .ThenByDescending(e => e.timestampUtc)
                    .Take(maxCount)
                    .ToList();
            }
        }
        
        /// <summary>
        /// Gets all events for building episodic memory summaries.
        /// </summary>
        public List<MemoryEvent> GetAllEventsForNpc(string npcId)
        {
            lock (sync)
            {
                var results = new List<MemoryEvent>(globalEvents);
                
                if (!string.IsNullOrEmpty(npcId) && npcEvents.TryGetValue(npcId, out var list))
                {
                    results.AddRange(list);
                }
                
                return results.OrderBy(e => e.timestampUtc).ToList();
            }
        }
        
        /// <summary>
        /// Clears all events for an NPC.
        /// </summary>
        public void ClearNpc(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return;
            }
            
            lock (sync)
            {
                npcEvents.Remove(npcId);
            }
        }
        
        /// <summary>
        /// Clears all events.
        /// </summary>
        public void ClearAll()
        {
            lock (sync)
            {
                npcEvents.Clear();
                globalEvents.Clear();
            }
        }
        
        /// <summary>
        /// Gets total event count.
        /// </summary>
        public int GetEventCount()
        {
            lock (sync)
            {
                int total = globalEvents.Count;
                foreach (var list in npcEvents.Values)
                {
                    total += list.Count;
                }
                return total;
            }
        }
        
        private void EvictLowestImportance(List<MemoryEvent> list, int targetCount)
        {
            if (list.Count <= targetCount)
            {
                return;
            }
            
            // Sort by importance ascending, keeping persistent facts at the end
            var sorted = list
                .OrderBy(e => e.isPersistentFact ? 1 : 0)
                .ThenBy(e => e.importance)
                .ThenBy(e => e.timestampUtc)
                .ToList();
            
            int removeCount = sorted.Count - targetCount;
            for (int i = 0; i < removeCount; i++)
            {
                list.Remove(sorted[i]);
            }
        }
        
        // === Runtime API Compatibility Methods ===
        
        /// <summary>
        /// Write a memory event (alias for Add with content support).
        /// </summary>
        public void Write(MemoryEvent evt)
        {
            if (evt == null) return;
            
            // Map content to summary if needed
            if (string.IsNullOrEmpty(evt.summary) && !string.IsNullOrEmpty(evt.content))
            {
                evt.summary = evt.content;
            }
            
            // Map timestamp to timestampUtc if needed
            if (evt.timestampUtc == default && evt.timestamp != default)
            {
                evt.timestampUtc = evt.timestamp;
            }
            
            // Generate ID if missing
            if (string.IsNullOrEmpty(evt.id))
            {
                evt.id = Guid.NewGuid().ToString("N");
            }
            
            Add(evt);
        }
        
        /// <summary>
        /// Query memories for context (simple keyword matching for now).
        /// For semantic search, use AIMemoryService instead.
        /// </summary>
        public MemoryEvent[] Query(string npcId, string query, int topK)
        {
            lock (sync)
            {
                var candidates = new List<MemoryEvent>();
                
                // Collect all events for this NPC
                if (!string.IsNullOrEmpty(npcId) && npcEvents.TryGetValue(npcId, out var list))
                {
                    candidates.AddRange(list);
                }
                
                // Add global events
                candidates.AddRange(globalEvents);
                
                // Simple relevance: prioritize by importance and recency
                // If query provided, boost events containing query keywords
                var queryWords = string.IsNullOrEmpty(query) 
                    ? Array.Empty<string>() 
                    : query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                var scored = candidates.Select(e =>
                {
                    float score = e.importance;
                    
                    // Boost for recency
                    var age = DateTime.UtcNow - e.timestampUtc;
                    if (age.TotalMinutes < 5) score += 0.3f;
                    else if (age.TotalMinutes < 30) score += 0.2f;
                    else if (age.TotalHours < 1) score += 0.1f;
                    
                    // Boost for keyword match
                    string text = (e.summary ?? e.content ?? "").ToLowerInvariant();
                    foreach (var word in queryWords)
                    {
                        if (text.Contains(word))
                        {
                            score += 0.2f;
                        }
                    }
                    
                    // Boost persistent facts
                    if (e.isPersistentFact) score += 0.5f;
                    
                    return (evt: e, score);
                })
                .OrderByDescending(x => x.score)
                .Take(topK)
                .Select(x => x.evt)
                .ToArray();
                
                return scored;
            }
        }
        
        /// <summary>
        /// Export all events for save system.
        /// </summary>
        public List<MemoryEvent> ExportAll()
        {
            lock (sync)
            {
                var all = new List<MemoryEvent>(globalEvents);
                foreach (var list in npcEvents.Values)
                {
                    all.AddRange(list);
                }
                return all;
            }
        }
        
        /// <summary>
        /// Import events from save system.
        /// </summary>
        public void ImportAll(List<MemoryEvent> events)
        {
            if (events == null) return;
            
            lock (sync)
            {
                ClearAll();
                
                foreach (var evt in events)
                {
                    if (evt == null) continue;
                    
                    if (!string.IsNullOrEmpty(evt.npcId))
                    {
                        if (!npcEvents.TryGetValue(evt.npcId, out var list))
                        {
                            list = new List<MemoryEvent>();
                            npcEvents[evt.npcId] = list;
                        }
                        list.Add(evt);
                    }
                    else
                    {
                        globalEvents.Add(evt);
                    }
                }
            }
        }
    }
}
