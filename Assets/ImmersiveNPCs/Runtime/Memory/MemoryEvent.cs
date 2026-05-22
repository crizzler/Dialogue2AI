using System;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Type of memory event that warrants persistent storage.
    /// </summary>
    public enum MemoryEventType
    {
        /// <summary>General observation (default).</summary>
        Observation,
        
        /// <summary>Dialogue line spoken.</summary>
        Dialogue,
        
        /// <summary>Significant event worth remembering.</summary>
        Significant,
        
        /// <summary>Player revealed their name.</summary>
        PlayerNameRevealed,
        
        /// <summary>NPC or player made a promise.</summary>
        PromiseMade,
        
        /// <summary>Threat was issued.</summary>
        ThreatMade,
        
        /// <summary>Relationship changed significantly.</summary>
        RelationshipShift,
        
        /// <summary>Quest decision was made.</summary>
        QuestDecision,
        
        /// <summary>Important lore or world fact was revealed.</summary>
        LoreRevelation,
        
        /// <summary>Item was given or received.</summary>
        ItemExchange,
        
        /// <summary>Location was discovered or mentioned as important.</summary>
        LocationDiscovery,
        
        /// <summary>Secret was shared.</summary>
        SecretShared,
        
        /// <summary>Betrayal or trust violation occurred.</summary>
        TrustViolation,
        
        /// <summary>Emotional bond moment (shared laugh, sympathy, etc.).</summary>
        EmotionalBond,
        
        /// <summary>Custom event type for game-specific needs.</summary>
        Custom
    }
    
    /// <summary>
    /// A structured memory event that warrants persistent storage.
    /// Only commit-worthy events are stored, not raw chat logs.
    /// </summary>
    [Serializable]
    public sealed class MemoryEvent
    {
        /// <summary>Unique identifier for this event.</summary>
        public string id;
        
        /// <summary>Type of memory event.</summary>
        public MemoryEventType eventType;
        
        /// <summary>NPC ID this memory is associated with.</summary>
        public string npcId;
        
        /// <summary>Human-readable summary of what happened.</summary>
        public string summary;
        
        /// <summary>Key entity involved (player name, item name, location, etc.).</summary>
        public string keyEntity;
        
        /// <summary>Secondary entity if applicable.</summary>
        public string secondaryEntity;
        
        /// <summary>Sentiment of the event (-1 negative, 0 neutral, +1 positive).</summary>
        public int sentiment;
        
        /// <summary>Importance weight (0.0 to 1.0). Higher = more likely to be included.</summary>
        public float importance;
        
        /// <summary>When this event occurred.</summary>
        public DateTime timestampUtc;
        
        /// <summary>Alias for summary (used by Runtime API).</summary>
        public string content
        {
            get => summary;
            set => summary = value;
        }
        
        /// <summary>Alias for timestampUtc (used by Runtime API).</summary>
        public DateTime timestamp
        {
            get => timestampUtc;
            set => timestampUtc = value;
        }
        
        /// <summary>Quest ID if relevant.</summary>
        public string questId;
        
        /// <summary>Scene/location where event occurred.</summary>
        public string location;
        
        /// <summary>True if this event should be included in all future contexts.</summary>
        public bool isPersistentFact;
        
        /// <summary>Turn count when this was recorded (for ordering).</summary>
        public int turnIndex;
        
        /// <summary>Optional embedding for semantic retrieval.</summary>
        public float[] embedding;
        
        /// <summary>
        /// Creates a new memory event with defaults.
        /// </summary>
        public static MemoryEvent Create(MemoryEventType type, string npcId, string summary)
        {
            return new MemoryEvent
            {
                id = Guid.NewGuid().ToString("N"),
                eventType = type,
                npcId = npcId ?? string.Empty,
                summary = summary ?? string.Empty,
                timestampUtc = DateTime.UtcNow,
                importance = 0.5f,
                sentiment = 0
            };
        }
        
        /// <summary>
        /// Creates a player name revelation event.
        /// </summary>
        public static MemoryEvent PlayerName(string npcId, string playerName)
        {
            var evt = Create(MemoryEventType.PlayerNameRevealed, npcId, $"The player's name is {playerName}.");
            evt.keyEntity = playerName;
            evt.isPersistentFact = true;
            evt.importance = 1.0f;
            return evt;
        }
        
        /// <summary>
        /// Creates a promise event.
        /// </summary>
        public static MemoryEvent Promise(string npcId, string promiser, string promise, bool isPlayerPromise)
        {
            var evt = Create(MemoryEventType.PromiseMade, npcId, 
                isPlayerPromise ? $"The player promised: {promise}" : $"{promiser} promised: {promise}");
            evt.keyEntity = promiser;
            evt.secondaryEntity = promise;
            evt.importance = 0.8f;
            evt.sentiment = 1;
            return evt;
        }
        
        /// <summary>
        /// Creates a relationship shift event.
        /// </summary>
        public static MemoryEvent RelationshipChange(string npcId, string description, int delta)
        {
            var evt = Create(MemoryEventType.RelationshipShift, npcId, description);
            evt.sentiment = delta > 0 ? 1 : (delta < 0 ? -1 : 0);
            evt.importance = Math.Min(1.0f, Math.Abs(delta) / 20f);
            return evt;
        }
        
        /// <summary>
        /// Creates a quest decision event.
        /// </summary>
        public static MemoryEvent QuestChoice(string npcId, string questId, string decision)
        {
            var evt = Create(MemoryEventType.QuestDecision, npcId, decision);
            evt.questId = questId;
            evt.importance = 0.9f;
            evt.isPersistentFact = true;
            return evt;
        }
        
        /// <summary>
        /// Converts to a brief text snippet for context injection.
        /// </summary>
        public string ToSnippet()
        {
            return summary ?? string.Empty;
        }
    }
}
