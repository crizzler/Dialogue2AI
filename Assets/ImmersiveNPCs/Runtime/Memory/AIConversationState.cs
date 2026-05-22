using System;
using System.Collections.Generic;

namespace ImmersiveNPCs
{
    public sealed class AIConversationState
    {
        private readonly List<AIConversationTurn> recentTurns = new List<AIConversationTurn>();
        private TurnResult lastGenerated;
        private string summary = string.Empty;

        public IReadOnlyList<AIConversationTurn> RecentTurns => recentTurns;
        public string Summary => summary;
        public TurnResult LastGenerated => lastGenerated;

        public string LastPlayerChoice
        {
            get
            {
                if (recentTurns.Count == 0)
                {
                    return string.Empty;
                }
                return recentTurns[recentTurns.Count - 1].playerChoice ?? string.Empty;
            }
        }

        public void SetGeneratedTurn(TurnResult result)
        {
            lastGenerated = result;
        }

        public void RecordChoice(string choiceText, string mood)
        {
            if (lastGenerated == null)
            {
                return;
            }

            AIConversationTurn turn = new AIConversationTurn
            {
                npcLine = lastGenerated.npcLine,
                playerChoice = choiceText,
                mood = mood,
                timestampUtc = DateTime.UtcNow
            };
            recentTurns.Add(turn);
        }

        public void AppendMemoryDelta(string delta)
        {
            if (string.IsNullOrWhiteSpace(delta))
            {
                return;
            }

            if (summary.Length > 0)
            {
                summary += "\n";
            }
            summary += delta.Trim();
        }

        public void SummarizeIfNeeded(AIConversationSettings settings)
        {
            if (!settings.summarizationEnabled)
            {
                return;
            }

            int maxTurns = Math.Max(1, settings.maxRecentTurns);
            if (recentTurns.Count <= maxTurns)
            {
                return;
            }

            int overflow = recentTurns.Count - maxTurns;
            if (overflow <= 0)
            {
                return;
            }

            for (int i = 0; i < overflow; i++)
            {
                var turn = recentTurns[i];
                if (turn == null)
                {
                    continue;
                }

                if (summary.Length > 0)
                {
                    summary += "\n";
                }

                summary += "NPC: " + turn.npcLine + " | Player: " + turn.playerChoice;
            }

            recentTurns.RemoveRange(0, overflow);
            TrimSummary(settings);
        }

        private void TrimSummary(AIConversationSettings settings)
        {
            int maxChars = Math.Max(64, settings.summaryTokenBudget * 4);
            if (summary.Length <= maxChars)
            {
                return;
            }

            summary = summary.Substring(summary.Length - maxChars, maxChars);
        }

        public AIConversationState Clone()
        {
            AIConversationState clone = new AIConversationState();
            clone.summary = summary;
            clone.recentTurns.AddRange(recentTurns);
            clone.lastGenerated = lastGenerated;
            return clone;
        }
    }
}
