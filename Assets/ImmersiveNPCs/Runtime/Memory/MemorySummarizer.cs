using System.Collections.Generic;
using System.Text;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Compresses episodic memories into summaries for context injection.
    /// Uses heuristics to identify key moments worth preserving.
    /// </summary>
    public sealed class MemorySummarizer
    {
        /// <summary>
        /// Aggressiveness of summarization (0 = minimal, 1 = aggressive).
        /// Higher values produce shorter summaries.
        /// </summary>
        public float Aggressiveness { get; set; } = 0.5f;
        
        /// <summary>
        /// Maximum characters for the summary output.
        /// </summary>
        public int MaxSummaryChars { get; set; } = 600;
        
        /// <summary>
        /// Summarizes a list of memory events into a compact string for context injection.
        /// </summary>
        public string Summarize(List<MemoryEvent> events, int tokenBudget)
        {
            if (events == null || events.Count == 0)
            {
                return string.Empty;
            }
            
            // Convert token budget to approximate char limit (3 chars per token)
            int charBudget = tokenBudget * 3;
            if (charBudget <= 0)
            {
                charBudget = MaxSummaryChars;
            }
            
            // Score and sort events by importance and recency
            var scored = ScoreEvents(events);
            
            // Build summary within budget
            var builder = new StringBuilder();
            
            // First pass: Include persistent facts
            foreach (var (evt, score) in scored)
            {
                if (!evt.isPersistentFact)
                {
                    continue;
                }
                
                string snippet = evt.ToSnippet();
                if (builder.Length + snippet.Length + 2 > charBudget)
                {
                    break;
                }
                
                if (builder.Length > 0)
                {
                    builder.Append(" ");
                }
                builder.Append(snippet);
            }
            
            // Second pass: Include high-importance events
            foreach (var (evt, score) in scored)
            {
                if (evt.isPersistentFact)
                {
                    continue; // Already included
                }
                
                if (score < GetMinScoreThreshold())
                {
                    continue;
                }
                
                string snippet = evt.ToSnippet();
                if (builder.Length + snippet.Length + 2 > charBudget)
                {
                    break;
                }
                
                if (builder.Length > 0)
                {
                    builder.Append(" ");
                }
                builder.Append(snippet);
            }
            
            return builder.ToString();
        }
        
        /// <summary>
        /// Creates a session summary from raw conversation turns.
        /// Used for backwards compatibility with legacy mode.
        /// </summary>
        public string SummarizeTurns(IReadOnlyList<AIConversationTurn> turns, int tokenBudget)
        {
            if (turns == null || turns.Count == 0)
            {
                return string.Empty;
            }
            
            int charBudget = tokenBudget * 3;
            var builder = new StringBuilder();
            
            // Build from most recent to oldest, within budget
            for (int i = turns.Count - 1; i >= 0; i--)
            {
                var turn = turns[i];
                if (turn == null)
                {
                    continue;
                }
                
                string snippet = BuildTurnSnippet(turn);
                if (builder.Length + snippet.Length + 2 > charBudget)
                {
                    break;
                }
                
                if (builder.Length > 0)
                {
                    builder.Insert(0, " ");
                }
                builder.Insert(0, snippet);
            }
            
            return builder.ToString();
        }
        
        /// <summary>
        /// Builds a compressed summary from world state changes.
        /// </summary>
        public string SummarizeWorldChanges(WorldStateSnapshot before, WorldStateSnapshot after)
        {
            if (before == null || after == null)
            {
                return string.Empty;
            }
            
            var changes = new List<string>();
            
            // Quest progress
            if (before.currentQuestBeat != after.currentQuestBeat && !string.IsNullOrEmpty(after.currentQuestBeat))
            {
                changes.Add($"Quest progressed to: {after.currentQuestBeat}");
            }
            
            // Location change
            if (before.currentLocation != after.currentLocation && !string.IsNullOrEmpty(after.currentLocation))
            {
                changes.Add($"Moved to: {after.currentLocation}");
            }
            
            // Relationship change
            if (before.relationshipLevel != after.relationshipLevel)
            {
                int delta = after.relationshipLevel - before.relationshipLevel;
                string direction = delta > 0 ? "improved" : "worsened";
                changes.Add($"Relationship {direction}");
            }
            
            // New flags
            foreach (var flag in after.activeFlags)
            {
                if (!before.activeFlags.Contains(flag))
                {
                    changes.Add($"Flag set: {flag}");
                }
            }
            
            return string.Join(". ", changes);
        }
        
        private List<(MemoryEvent evt, float score)> ScoreEvents(List<MemoryEvent> events)
        {
            var scored = new List<(MemoryEvent, float)>();
            int total = events.Count;
            
            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                
                // Base score from importance
                float score = evt.importance;
                
                // Recency bonus (most recent events score higher)
                float recencyBonus = (float)(i + 1) / total * 0.3f;
                score += recencyBonus;
                
                // Type bonuses
                switch (evt.eventType)
                {
                    case MemoryEventType.PlayerNameRevealed:
                        score += 0.5f;
                        break;
                    case MemoryEventType.QuestDecision:
                        score += 0.3f;
                        break;
                    case MemoryEventType.RelationshipShift:
                        score += 0.2f;
                        break;
                    case MemoryEventType.PromiseMade:
                        score += 0.2f;
                        break;
                }
                
                scored.Add((evt, score));
            }
            
            // Sort by score descending
            scored.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            
            return scored;
        }
        
        private float GetMinScoreThreshold()
        {
            // Higher aggressiveness = higher threshold = fewer events included
            return 0.3f + (Aggressiveness * 0.4f);
        }
        
        private string BuildTurnSnippet(AIConversationTurn turn)
        {
            if (turn == null)
            {
                return string.Empty;
            }
            
            // Compress based on aggressiveness
            if (Aggressiveness > 0.7f)
            {
                // Very aggressive: just player choice
                return $"[{turn.playerChoice}]";
            }
            
            if (Aggressiveness > 0.4f)
            {
                // Moderate: NPC + player choice
                string npcShort = Truncate(turn.npcLine, 40);
                return $"{npcShort} -> {turn.playerChoice}";
            }
            
            // Full: NPC line and player response
            return $"NPC: {Truncate(turn.npcLine, 60)} | Player: {turn.playerChoice}";
        }
        
        private static string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
            {
                return text ?? string.Empty;
            }
            
            return text.Substring(0, maxLen - 3) + "...";
        }
    }
}
