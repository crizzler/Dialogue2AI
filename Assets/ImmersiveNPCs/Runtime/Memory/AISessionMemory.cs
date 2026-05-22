using System;
using System.Collections.Generic;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Tracks a single turn's entities and options for session memory.
    /// </summary>
    [Serializable]
    public class SessionTurnRecord
    {
        public string npcLine;
        public string playerChoice;
        public List<string> offeredOptions = new List<string>();
        public List<MentionedEntity> mentionedEntities = new List<MentionedEntity>();
        public DateTime timestamp;
        public int turnIndex;
    }

    /// <summary>
    /// In-memory session tracker for conversation context.
    /// Tracks entities mentioned, options offered, and provides
    /// coherence data for the current conversation session.
    /// </summary>
    public sealed class AISessionMemory
    {
        private readonly Dictionary<string, SessionContext> sessions = new Dictionary<string, SessionContext>();
        private readonly object sync = new object();

        private class SessionContext
        {
            public string npcId;
            public List<SessionTurnRecord> turns = new List<SessionTurnRecord>();
            public HashSet<string> allMentionedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> allOfferedOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> entityMentionCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public List<MentionedEntity> unresolvedEntities = new List<MentionedEntity>();
            public DateTime lastActivity = DateTime.UtcNow;
        }

        /// <summary>
        /// Maximum turns to keep in session memory before trimming oldest.
        /// </summary>
        public int MaxTurnsPerSession { get; set; } = 50;

        /// <summary>
        /// Record a completed turn in the session.
        /// </summary>
        public void RecordTurn(string npcId, string npcLine, string playerChoice, List<string> offeredOptions)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                npcId = "default";
            }

            SessionContext ctx = GetOrCreateSession(npcId);
            lock (sync)
            {
                // Extract entities from NPC line
                List<MentionedEntity> entities = AIEntityExtractor.ExtractEntities(npcLine);

                SessionTurnRecord record = new SessionTurnRecord
                {
                    npcLine = npcLine ?? string.Empty,
                    playerChoice = playerChoice ?? string.Empty,
                    timestamp = DateTime.UtcNow,
                    turnIndex = ctx.turns.Count
                };

                if (offeredOptions != null)
                {
                    record.offeredOptions.AddRange(offeredOptions);
                    foreach (string option in offeredOptions)
                    {
                        if (!string.IsNullOrWhiteSpace(option))
                        {
                            ctx.allOfferedOptions.Add(option.Trim());
                        }
                    }
                }

                if (entities != null)
                {
                    record.mentionedEntities.AddRange(entities);
                    foreach (MentionedEntity entity in entities)
                    {
                        string key = entity.text.ToLowerInvariant().Trim();
                        ctx.allMentionedEntities.Add(key);
                        
                        if (!ctx.entityMentionCount.ContainsKey(key))
                        {
                            ctx.entityMentionCount[key] = 0;
                        }
                        ctx.entityMentionCount[key]++;
                    }
                }

                // Track unresolved entities (mentioned but no option offered)
                UpdateUnresolvedEntities(ctx, entities, offeredOptions);

                ctx.turns.Add(record);
                ctx.lastActivity = DateTime.UtcNow;

                // Trim old turns if needed
                while (ctx.turns.Count > MaxTurnsPerSession)
                {
                    ctx.turns.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Get entities that were mentioned but never had options offered for them.
        /// </summary>
        public List<MentionedEntity> GetUnresolvedEntities(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                npcId = "default";
            }

            lock (sync)
            {
                if (sessions.TryGetValue(npcId, out SessionContext ctx))
                {
                    return new List<MentionedEntity>(ctx.unresolvedEntities);
                }
            }
            return new List<MentionedEntity>();
        }

        /// <summary>
        /// Get the most recently mentioned entities (last N turns).
        /// </summary>
        public List<MentionedEntity> GetRecentEntities(string npcId, int lastNTurns = 3)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                npcId = "default";
            }

            List<MentionedEntity> result = new List<MentionedEntity>();
            lock (sync)
            {
                if (!sessions.TryGetValue(npcId, out SessionContext ctx))
                {
                    return result;
                }

                int startIdx = Math.Max(0, ctx.turns.Count - lastNTurns);
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = ctx.turns.Count - 1; i >= startIdx; i--)
                {
                    foreach (MentionedEntity entity in ctx.turns[i].mentionedEntities)
                    {
                        string key = entity.text.ToLowerInvariant();
                        if (!seen.Contains(key))
                        {
                            seen.Add(key);
                            result.Add(entity);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Get all options that have been offered in this session.
        /// </summary>
        public List<string> GetAllOfferedOptions(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                npcId = "default";
            }

            lock (sync)
            {
                if (sessions.TryGetValue(npcId, out SessionContext ctx))
                {
                    return new List<string>(ctx.allOfferedOptions);
                }
            }
            return new List<string>();
        }

        /// <summary>
        /// Get frequently mentioned entities (mentioned more than once).
        /// </summary>
        public List<string> GetFrequentlyMentionedEntities(string npcId, int minMentions = 2)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                npcId = "default";
            }

            List<string> result = new List<string>();
            lock (sync)
            {
                if (!sessions.TryGetValue(npcId, out SessionContext ctx))
                {
                    return result;
                }

                foreach (var kvp in ctx.entityMentionCount)
                {
                    if (kvp.Value >= minMentions)
                    {
                        result.Add(kvp.Key);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Get recent turns for context injection.
        /// </summary>
        public List<SessionTurnRecord> GetRecentTurns(string npcId, int count = 5)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                npcId = "default";
            }

            List<SessionTurnRecord> result = new List<SessionTurnRecord>();
            lock (sync)
            {
                if (!sessions.TryGetValue(npcId, out SessionContext ctx))
                {
                    return result;
                }

                int startIdx = Math.Max(0, ctx.turns.Count - count);
                for (int i = startIdx; i < ctx.turns.Count; i++)
                {
                    result.Add(ctx.turns[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// Build a context string summarizing recent entities for prompt injection.
        /// </summary>
        public string BuildEntityContext(string npcId)
        {
            List<MentionedEntity> recent = GetRecentEntities(npcId, 5);
            List<MentionedEntity> unresolved = GetUnresolvedEntities(npcId);

            if (recent.Count == 0 && unresolved.Count == 0)
            {
                return string.Empty;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            if (unresolved.Count > 0)
            {
                sb.Append("Previously mentioned (player may ask about): ");
                for (int i = 0; i < Math.Min(unresolved.Count, 5); i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(unresolved[i].text);
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Clear session data for an NPC.
        /// </summary>
        public void ClearSession(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                npcId = "default";
            }

            lock (sync)
            {
                sessions.Remove(npcId);
            }
        }

        /// <summary>
        /// Clear all sessions.
        /// </summary>
        public void ClearAllSessions()
        {
            lock (sync)
            {
                sessions.Clear();
            }
        }

        private SessionContext GetOrCreateSession(string npcId)
        {
            lock (sync)
            {
                if (!sessions.TryGetValue(npcId, out SessionContext ctx))
                {
                    ctx = new SessionContext { npcId = npcId };
                    sessions[npcId] = ctx;
                }
                return ctx;
            }
        }

        private void UpdateUnresolvedEntities(SessionContext ctx, List<MentionedEntity> newEntities, List<string> offeredOptions)
        {
            if (newEntities == null)
            {
                return;
            }

            // Mark existing unresolved as resolved if an option now addresses them
            if (offeredOptions != null)
            {
                for (int i = ctx.unresolvedEntities.Count - 1; i >= 0; i--)
                {
                    MentionedEntity entity = ctx.unresolvedEntities[i];
                    foreach (string option in offeredOptions)
                    {
                        if (AIEntityExtractor.OptionMatchesEntity(option, entity))
                        {
                            ctx.unresolvedEntities.RemoveAt(i);
                            break;
                        }
                    }
                }
            }

            // Check new entities
            foreach (MentionedEntity entity in newEntities)
            {
                bool resolved = false;
                if (offeredOptions != null)
                {
                    foreach (string option in offeredOptions)
                    {
                        if (AIEntityExtractor.OptionMatchesEntity(option, entity))
                        {
                            resolved = true;
                            break;
                        }
                    }
                }

                if (!resolved && entity.confidence >= 0.7f)
                {
                    // Check if already in unresolved list
                    bool alreadyTracked = false;
                    string key = entity.text.ToLowerInvariant();
                    foreach (MentionedEntity existing in ctx.unresolvedEntities)
                    {
                        if (existing.text.ToLowerInvariant() == key)
                        {
                            alreadyTracked = true;
                            break;
                        }
                    }

                    if (!alreadyTracked)
                    {
                        ctx.unresolvedEntities.Add(entity);
                    }
                }
            }

            // Limit unresolved list size
            while (ctx.unresolvedEntities.Count > 10)
            {
                ctx.unresolvedEntities.RemoveAt(0);
            }
        }
    }
}
