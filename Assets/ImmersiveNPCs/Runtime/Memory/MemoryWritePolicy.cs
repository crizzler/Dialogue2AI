using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Policy for determining which events should be written to persistent memory.
    /// Only commit-worthy events are stored, not raw chat logs.
    /// </summary>
    public sealed class MemoryWritePolicy
    {
        // Common name patterns (will be expanded by game-specific hooks)
        private static readonly Regex NamePattern = new Regex(
            @"\b(?:my name is|i'm called|call me|i am)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex PromisePattern = new Regex(
            @"\b(?:i promise|i swear|i'll|i will|you have my word)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex ThreatPattern = new Regex(
            @"\b(?:i'll kill|you'll regret|you will pay|threaten|warn you)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex QuestPattern = new Regex(
            @"\b(?:accept|decline|refuse|agree to|take the quest|complete)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex SecretPattern = new Regex(
            @"\b(?:secret|don't tell anyone|between us|confidential)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex ItemPattern = new Regex(
            @"\b(?:give you|here take|received|handed over|gave)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        /// <summary>
        /// Minimum importance threshold for automatic storage.
        /// Events below this are discarded unless explicitly committed.
        /// </summary>
        public float MinImportanceThreshold { get; set; } = 0.3f;
        
        /// <summary>
        /// If true, player name revelations are always stored.
        /// </summary>
        public bool AlwaysStorePlayerName { get; set; } = true;
        
        /// <summary>
        /// If true, quest decisions are always stored.
        /// </summary>
        public bool AlwaysStoreQuestDecisions { get; set; } = true;
        
        /// <summary>
        /// Analyzes a turn and returns memory events to commit.
        /// Returns empty list if nothing is commit-worthy.
        /// </summary>
        public List<MemoryEvent> AnalyzeTurn(string npcId, string playerChoice, string npcLine, WorldStateSnapshot snapshot)
        {
            var events = new List<MemoryEvent>();
            
            if (string.IsNullOrEmpty(playerChoice) && string.IsNullOrEmpty(npcLine))
            {
                return events;
            }
            
            string combined = (playerChoice ?? "") + " " + (npcLine ?? "");
            
            // Check for player name
            if (AlwaysStorePlayerName)
            {
                var nameMatch = NamePattern.Match(playerChoice ?? "");
                if (nameMatch.Success)
                {
                    string name = nameMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        events.Add(MemoryEvent.PlayerName(npcId, name));
                    }
                }
            }
            
            // Check for promises
            if (PromisePattern.IsMatch(combined))
            {
                bool isPlayerPromise = PromisePattern.IsMatch(playerChoice ?? "");
                string promiser = isPlayerPromise ? "Player" : npcId;
                string context = isPlayerPromise ? playerChoice : npcLine;
                events.Add(MemoryEvent.Promise(npcId, promiser, TruncateForSummary(context), isPlayerPromise));
            }
            
            // Check for threats
            if (ThreatPattern.IsMatch(combined))
            {
                var evt = MemoryEvent.Create(MemoryEventType.ThreatMade, npcId, 
                    "A threat was made: " + TruncateForSummary(combined));
                evt.importance = 0.7f;
                evt.sentiment = -1;
                events.Add(evt);
            }
            
            // Check for quest decisions
            if (AlwaysStoreQuestDecisions && QuestPattern.IsMatch(playerChoice ?? ""))
            {
                string questId = snapshot?.activeQuestId ?? "";
                events.Add(MemoryEvent.QuestChoice(npcId, questId, 
                    "Quest decision: " + TruncateForSummary(playerChoice)));
            }
            
            // Check for secrets
            if (SecretPattern.IsMatch(combined))
            {
                var evt = MemoryEvent.Create(MemoryEventType.SecretShared, npcId,
                    "A secret was shared.");
                evt.importance = 0.8f;
                events.Add(evt);
            }
            
            // Check for item exchanges
            if (ItemPattern.IsMatch(combined))
            {
                var evt = MemoryEvent.Create(MemoryEventType.ItemExchange, npcId,
                    TruncateForSummary(combined));
                evt.importance = 0.6f;
                events.Add(evt);
            }
            
            // Add location context to all events
            if (snapshot != null && !string.IsNullOrEmpty(snapshot.currentLocation))
            {
                foreach (var evt in events)
                {
                    evt.location = snapshot.currentLocation;
                    if (!string.IsNullOrEmpty(snapshot.activeQuestId))
                    {
                        evt.questId = snapshot.activeQuestId;
                    }
                }
            }
            
            return events;
        }
        
        /// <summary>
        /// Checks if a memory event should be committed based on policy.
        /// </summary>
        public bool ShouldCommit(MemoryEvent evt)
        {
            if (evt == null)
            {
                return false;
            }
            
            // Persistent facts always commit
            if (evt.isPersistentFact)
            {
                return true;
            }
            
            // High importance events always commit
            if (evt.importance >= 0.7f)
            {
                return true;
            }
            
            // Player name always commits
            if (AlwaysStorePlayerName && evt.eventType == MemoryEventType.PlayerNameRevealed)
            {
                return true;
            }
            
            // Quest decisions always commit
            if (AlwaysStoreQuestDecisions && evt.eventType == MemoryEventType.QuestDecision)
            {
                return true;
            }
            
            // Check importance threshold
            return evt.importance >= MinImportanceThreshold;
        }
        
        /// <summary>
        /// Extracts candidate memory writes from model's memory_delta field.
        /// </summary>
        public List<MemoryEvent> ExtractFromMemoryDelta(string npcId, string memoryDelta, WorldStateSnapshot snapshot)
        {
            var events = new List<MemoryEvent>();
            
            if (string.IsNullOrWhiteSpace(memoryDelta))
            {
                return events;
            }
            
            // Create a generic custom event from the delta
            var evt = MemoryEvent.Create(MemoryEventType.Custom, npcId, memoryDelta.Trim());
            evt.importance = 0.5f;
            
            // Boost importance based on keywords
            string lower = memoryDelta.ToLowerInvariant();
            if (lower.Contains("promise") || lower.Contains("swear"))
            {
                evt.eventType = MemoryEventType.PromiseMade;
                evt.importance = 0.8f;
            }
            else if (lower.Contains("secret") || lower.Contains("trust"))
            {
                evt.importance = 0.7f;
            }
            else if (lower.Contains("quest") || lower.Contains("mission") || lower.Contains("task"))
            {
                evt.eventType = MemoryEventType.QuestDecision;
                evt.importance = 0.8f;
            }
            else if (lower.Contains("name is") || lower.Contains("called"))
            {
                evt.eventType = MemoryEventType.PlayerNameRevealed;
                evt.importance = 1.0f;
                evt.isPersistentFact = true;
            }
            
            if (snapshot != null)
            {
                evt.location = snapshot.currentLocation;
                evt.questId = snapshot.activeQuestId;
            }
            
            events.Add(evt);
            return events;
        }
        
        private static string TruncateForSummary(string text, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            
            text = text.Trim();
            if (text.Length <= maxLength)
            {
                return text;
            }
            
            return text.Substring(0, maxLength - 3) + "...";
        }
    }
}
