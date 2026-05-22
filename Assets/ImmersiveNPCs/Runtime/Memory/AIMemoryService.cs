using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public sealed class AIMemoryService
    {
        private readonly AIConversationSettings settings;
        private readonly IEmbeddingProvider embeddingProvider;
        private readonly List<AIMemoryEntry> entries = new List<AIMemoryEntry>();
        private readonly Dictionary<string, long> npcRevision = new Dictionary<string, long>();
        private readonly object sync = new object();
        private long globalRevision;
        private bool seeded;

        public AIMemoryService(AIConversationSettings settings, IEmbeddingProvider embeddingProvider)
        {
            this.settings = settings;
            this.embeddingProvider = embeddingProvider;
        }

        public bool Enabled => settings != null && settings.enableMemory && embeddingProvider != null && embeddingProvider.IsAvailable;
        public string Status => embeddingProvider != null ? embeddingProvider.Status : "Disabled";

        public string GetMemoryKey(string npcId)
        {
            if (!Enabled)
            {
                return string.Empty;
            }

            long npcKey = GetNpcRevision(npcId);
            switch (settings.memoryScope)
            {
                case MemoryScopeMode.GlobalOnly:
                    return "g:" + globalRevision;
                case MemoryScopeMode.PerNpcOnly:
                    return "n:" + npcKey;
                case MemoryScopeMode.GlobalAndNpc:
                default:
                    return "g:" + globalRevision + "|n:" + npcKey;
            }
        }

        public async Task AddChoiceAsync(string npcId, string playerChoice, string npcLine, CancellationToken ct)
        {
            if (!Enabled)
            {
                return;
            }

            await EnsureSeededAsync(ct).ConfigureAwait(false);

            // Filter out confused/broken NPC responses from being stored in memory
            bool isConfusedResponse = IsConfusedResponse(npcLine);

            if (settings.memoryStorePlayerChoices && !string.IsNullOrWhiteSpace(playerChoice) && !isConfusedResponse)
            {
                await AddMemoryAsync(BuildChoiceMemory(playerChoice, npcLine), npcId, MemorySourceType.PlayerChoice, settings.memoryScope, ct).ConfigureAwait(false);
            }

            if (settings.memoryStoreNpcReplies && !string.IsNullOrWhiteSpace(npcLine) && !isConfusedResponse)
            {
                await AddMemoryAsync(BuildNpcReplyMemory(npcLine), npcId, MemorySourceType.NpcReply, settings.memoryScope, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Detects confused/broken NPC responses that should not be stored in memory.
        /// These responses indicate the model lost context and would pollute future generations.
        /// </summary>
        private static bool IsConfusedResponse(string npcLine)
        {
            if (string.IsNullOrWhiteSpace(npcLine))
            {
                return true;
            }

            string lower = npcLine.ToLowerInvariant();
            
            // Common confused response patterns
            if (lower.Contains("i don't understand"))
                return true;
            if (lower.Contains("could you please clarify"))
                return true;
            if (lower.Contains("what do you mean"))
                return true;
            if (lower.Contains("i'm not sure what you're asking"))
                return true;
            if (npcLine == "...")
                return true;
            if (npcLine.Length < 10)
                return true;
                
            return false;
        }

        public async Task<List<AIMemorySnippet>> QueryAsync(AIContext context, CancellationToken ct)
        {
            List<AIMemorySnippet> results = new List<AIMemorySnippet>();
            if (!Enabled || context == null)
            {
                return results;
            }

            await EnsureSeededAsync(ct).ConfigureAwait(false);

            string query = BuildQuery(context);
            if (string.IsNullOrWhiteSpace(query))
            {
                return results;
            }

            float[] queryEmbedding;
            try
            {
                queryEmbedding = await embeddingProvider.EmbedAsync(query, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AILogger.Warn("Memory embedding failed: " + ex.Message);
                return results;
            }
            if (queryEmbedding == null || queryEmbedding.Length == 0)
            {
                return results;
            }

            List<AIMemoryEntry> snapshot;
            lock (sync)
            {
                snapshot = new List<AIMemoryEntry>(entries);
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                AIMemoryEntry entry = snapshot[i];
                if (entry == null || entry.embedding == null || entry.embedding.Length == 0)
                {
                    continue;
                }

                if (!MatchesScope(entry, context.npcId))
                {
                    continue;
                }

                float similarity = CosineSimilarity(queryEmbedding, entry.embedding);
                if (similarity <= 0f)
                {
                    continue;
                }

                float decay = settings.memoryUseTimeDecay ? ComputeDecay(entry.timestampUtc) : 1f;
                float score = similarity * decay * Math.Max(0.1f, entry.importance);
                results.Add(new AIMemorySnippet
                {
                    text = entry.text,
                    source = entry.source,
                    npcId = entry.npcId,
                    score = score
                });
            }

            if (results.Count == 0)
            {
                return results;
            }

            results.Sort((a, b) => b.score.CompareTo(a.score));
            int take = Math.Min(settings.memoryTopK, results.Count);
            if (take < results.Count)
            {
                results.RemoveRange(take, results.Count - take);
            }

            TrimToCharLimit(results);
            return results;
        }

        private async Task AddMemoryAsync(string text, string npcId, MemorySourceType source, MemoryScopeMode scopeMode, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            float[] embedding;
            try
            {
                embedding = await embeddingProvider.EmbedAsync(text, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AILogger.Warn("Memory embedding failed: " + ex.Message);
                return;
            }
            if (embedding == null || embedding.Length == 0)
            {
                return;
            }

            bool makeGlobal = scopeMode != MemoryScopeMode.PerNpcOnly;
            bool makeNpc = scopeMode != MemoryScopeMode.GlobalOnly;

            if (makeGlobal)
            {
                AddEntry(new AIMemoryEntry
                {
                    id = Guid.NewGuid().ToString("N"),
                    text = text.Trim(),
                    npcId = string.Empty,
                    isGlobal = true,
                    source = source,
                    importance = 1f,
                    timestampUtc = DateTime.UtcNow,
                    embedding = embedding
                });
            }

            if (makeNpc && !string.IsNullOrWhiteSpace(npcId))
            {
                AddEntry(new AIMemoryEntry
                {
                    id = Guid.NewGuid().ToString("N"),
                    text = text.Trim(),
                    npcId = npcId,
                    isGlobal = false,
                    source = source,
                    importance = 1f,
                    timestampUtc = DateTime.UtcNow,
                    embedding = embedding
                });
            }
        }

        private void AddEntry(AIMemoryEntry entry)
        {
            lock (sync)
            {
                entries.Add(entry);
                TrimEntries(entry.npcId, entry.isGlobal);
                IncrementRevision(entry.npcId, entry.isGlobal);
            }
        }

        private void TrimEntries(string npcId, bool isGlobal)
        {
            int maxEntries = Math.Max(1, settings.memoryMaxEntries);
            if (entries.Count > maxEntries)
            {
                entries.Sort((a, b) => a.timestampUtc.CompareTo(b.timestampUtc));
                while (entries.Count > maxEntries)
                {
                    entries.RemoveAt(0);
                }
            }

            if (!string.IsNullOrWhiteSpace(npcId))
            {
                int perNpcMax = Math.Max(1, settings.memoryMaxEntriesPerNpc);
                int count = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].npcId == npcId && !entries[i].isGlobal)
                    {
                        count++;
                    }
                }

                if (count > perNpcMax)
                {
                    entries.Sort((a, b) => a.timestampUtc.CompareTo(b.timestampUtc));
                    for (int i = entries.Count - 1; i >= 0 && count > perNpcMax; i--)
                    {
                        if (entries[i].npcId == npcId && !entries[i].isGlobal)
                        {
                            entries.RemoveAt(i);
                            count--;
                        }
                    }
                }
            }
        }

        private void IncrementRevision(string npcId, bool isGlobal)
        {
            if (isGlobal)
            {
                globalRevision++;
            }

            if (!string.IsNullOrWhiteSpace(npcId))
            {
                npcRevision[npcId] = GetNpcRevision(npcId) + 1;
            }
        }

        private long GetNpcRevision(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                return 0;
            }

            lock (sync)
            {
                if (npcRevision.TryGetValue(npcId, out long value))
                {
                    return value;
                }
            }

            return 0;
        }

        private bool MatchesScope(AIMemoryEntry entry, string npcId)
        {
            switch (settings.memoryScope)
            {
                case MemoryScopeMode.GlobalOnly:
                    return entry.isGlobal;
                case MemoryScopeMode.PerNpcOnly:
                    return !string.IsNullOrWhiteSpace(npcId) && entry.npcId == npcId;
                case MemoryScopeMode.GlobalAndNpc:
                default:
                    return entry.isGlobal || (!string.IsNullOrWhiteSpace(npcId) && entry.npcId == npcId);
            }
        }

        private string BuildQuery(AIContext context)
        {
            StringBuilder builder = new StringBuilder(256);
            if (!string.IsNullOrWhiteSpace(context.lastPlayerChoice))
            {
                builder.Append("Player choice: ").Append(context.lastPlayerChoice.Trim());
            }

            if (!string.IsNullOrWhiteSpace(context.summary))
            {
                if (builder.Length > 0) builder.Append("\n");
                builder.Append("Summary: ").Append(context.summary.Trim());
            }

            if (context.recentTurns != null && context.recentTurns.Count > 0)
            {
                AIConversationTurn last = context.recentTurns[context.recentTurns.Count - 1];
                if (last != null && !string.IsNullOrWhiteSpace(last.npcLine))
                {
                    if (builder.Length > 0) builder.Append("\n");
                    builder.Append("NPC last said: ").Append(last.npcLine.Trim());
                }
            }

            return builder.ToString();
        }

        private void TrimToCharLimit(List<AIMemorySnippet> snippets)
        {
            int maxChars = Math.Max(64, settings.memoryMaxChars);
            int total = 0;
            for (int i = 0; i < snippets.Count; i++)
            {
                total += snippets[i].text != null ? snippets[i].text.Length : 0;
                if (total <= maxChars)
                {
                    continue;
                }

                snippets.RemoveRange(i, snippets.Count - i);
                break;
            }
        }

        private float ComputeDecay(DateTime timestampUtc)
        {
            int halfLife = Math.Max(1, settings.memoryDecayHalfLifeMinutes);
            double minutes = (DateTime.UtcNow - timestampUtc).TotalMinutes;
            if (minutes <= 0)
            {
                return 1f;
            }

            double decay = Math.Pow(0.5, minutes / halfLife);
            return (float)decay;
        }

        private static float CosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null)
            {
                return 0f;
            }

            int len = Math.Min(a.Length, b.Length);
            if (len == 0)
            {
                return 0f;
            }

            double dot = 0;
            double magA = 0;
            double magB = 0;
            for (int i = 0; i < len; i++)
            {
                float va = a[i];
                float vb = b[i];
                dot += va * vb;
                magA += va * va;
                magB += vb * vb;
            }

            if (magA <= 0 || magB <= 0)
            {
                return 0f;
            }

            return (float)(dot / (Math.Sqrt(magA) * Math.Sqrt(magB)));
        }

        private async Task EnsureSeededAsync(CancellationToken ct)
        {
            if (seeded || settings == null || settings.memorySeeds == null || settings.memorySeeds.Count == 0)
            {
                seeded = true;
                return;
            }

            seeded = true;
            for (int i = 0; i < settings.memorySeeds.Count; i++)
            {
                MemorySeed seed = settings.memorySeeds[i];
                if (seed == null || string.IsNullOrWhiteSpace(seed.text))
                {
                    continue;
                }

                string npcId = seed.scope != MemoryScopeMode.GlobalOnly ? seed.npcId : string.Empty;
                await AddMemoryAsync(seed.text, npcId, MemorySourceType.DesignerNote, seed.scope, ct).ConfigureAwait(false);
            }
        }

        private static string BuildChoiceMemory(string choice, string npcLine)
        {
            if (string.IsNullOrWhiteSpace(npcLine))
            {
                return "Player chose: " + choice.Trim();
            }

            return "Player chose: " + choice.Trim() + " | NPC replied: " + npcLine.Trim();
        }

        private static string BuildNpcReplyMemory(string npcLine)
        {
            return "NPC said: " + npcLine.Trim();
        }
    }
}
