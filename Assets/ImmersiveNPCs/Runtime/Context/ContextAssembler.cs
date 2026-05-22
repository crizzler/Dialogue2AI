using System.Collections.Generic;
using System.Text;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Assembles the final context from tiered components within budget constraints.
    /// </summary>
    public sealed class ContextAssembler
    {
        private readonly ContextTierBudgets budgets;
        private readonly MemorySummarizer summarizer;
        
        public ContextAssembler(ContextTierBudgets budgets)
        {
            this.budgets = budgets ?? ContextTierBudgets.CreateDefault(4096);
            summarizer = new MemorySummarizer();
        }
        
        /// <summary>
        /// Assembles a complete AIContext from tiered components.
        /// </summary>
        public AIContext Assemble(
            string npcId,
            int slots,
            string language,
            WorldStateSnapshot snapshot,
            NpcProfile profile,
            List<MemoryEvent> memoryEvents,
            List<AIMemorySnippet> retrievalSnippets,
            IReadOnlyList<AIConversationTurn> recentTurns,
            string lastPlayerChoice,
            AIConversationSettings settings)
        {
            var context = new AIContext
            {
                npcId = npcId ?? "",
                slots = slots,
                language = language ?? "en",
                lastPlayerChoice = lastPlayerChoice ?? ""
            };
            
            // Build system prompt (Tier B: NPC Identity)
            context.systemPrompt = BuildSystemPrompt(profile, snapshot, slots, language, settings);
            
            // Build user prompt (Tiers A, C, D + recent conversation)
            context.userPrompt = BuildUserPrompt(snapshot, memoryEvents, retrievalSnippets, recentTurns, lastPlayerChoice, settings);
            
            // Copy recent turns for reference
            if (recentTurns != null)
            {
                context.recentTurns.AddRange(recentTurns);
            }
            
            // Copy retrieval snippets
            if (retrievalSnippets != null)
            {
                context.memorySnippets.AddRange(retrievalSnippets);
            }
            
            return context;
        }
        
        /// <summary>
        /// Builds system prompt with Tier B (NPC identity) content.
        /// </summary>
        private string BuildSystemPrompt(NpcProfile profile, WorldStateSnapshot snapshot, int slots, string language, AIConversationSettings settings)
        {
            var builder = new StringBuilder(budgets.tierBIdentity * 3);
            
            // NPC Persona
            if (profile != null)
            {
                if (!string.IsNullOrWhiteSpace(profile.personaPrompt))
                {
                    builder.AppendLine("NPC Persona:");
                    builder.AppendLine(profile.personaPrompt.Trim());
                }
                
                if (!string.IsNullOrWhiteSpace(profile.speakingStyle))
                {
                    builder.AppendLine();
                    builder.AppendLine("Speaking Style:");
                    builder.AppendLine(profile.speakingStyle.Trim());
                }
            }
            
            // Inject valid entities from snapshot if strict validation
            if (snapshot != null && snapshot.validLocations.Count > 0)
            {
                builder.AppendLine();
                builder.Append("Valid locations you may reference: ");
                builder.AppendLine(string.Join(", ", snapshot.validLocations));
            }
            
            if (snapshot != null && snapshot.knownNpcs.Count > 0)
            {
                builder.AppendLine();
                builder.Append("NPCs the player knows: ");
                builder.AppendLine(string.Join(", ", snapshot.knownNpcs));
            }
            
            // Stay in character
            if (settings != null && settings.stayInCharacter)
            {
                builder.AppendLine();
                builder.AppendLine("Stay in character. Do not mention being an AI or a system.");
            }
            
            // Response rules
            if (settings != null && settings.strictRespondToChoice)
            {
                builder.AppendLine();
                builder.AppendLine("CRITICAL: You MUST directly respond to the player's most recent choice.");
                builder.AppendLine("Do NOT ignore the player's choice or change the subject.");
            }
            
            // Forbidden topics
            if (settings != null && settings.forbiddenTopics != null && settings.forbiddenTopics.Count > 0)
            {
                builder.AppendLine();
                builder.Append("Avoid these topics: ");
                builder.AppendLine(string.Join(", ", settings.forbiddenTopics));
            }
            
            // JSON output format
            builder.AppendLine();
            builder.AppendLine("Return JSON only, matching this schema:");
            builder.AppendLine("{");
            builder.AppendLine("  \"npc_line\": \"string\",");
            builder.AppendLine("  \"options\": [\"string\" ... exactly " + slots + " entries],");
            builder.AppendLine("  \"mood\": \"optional short tag\",");
            builder.AppendLine("  \"memory_delta\": \"optional note to remember\"");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("Do not include any extra text before or after the JSON.");
            builder.AppendLine("Use language: " + language + ".");
            builder.AppendLine("/no_think");
            
            return builder.ToString();
        }
        
        /// <summary>
        /// Builds user prompt with Tiers A, C, D content and recent conversation.
        /// </summary>
        private string BuildUserPrompt(
            WorldStateSnapshot snapshot,
            List<MemoryEvent> memoryEvents,
            List<AIMemorySnippet> retrievalSnippets,
            IReadOnlyList<AIConversationTurn> recentTurns,
            string lastPlayerChoice,
            AIConversationSettings settings)
        {
            var builder = new StringBuilder(budgets.TotalPromptBudget);
            
            // Tier A: Current Scene Facts
            if (snapshot != null)
            {
                builder.AppendLine("=== Current Scene ===");
                
                if (!string.IsNullOrEmpty(snapshot.currentLocation))
                {
                    builder.AppendLine("Location: " + snapshot.currentLocation);
                }
                
                if (!string.IsNullOrEmpty(snapshot.timeOfDay))
                {
                    builder.AppendLine("Time: " + snapshot.timeOfDay);
                }
                
                if (!string.IsNullOrEmpty(snapshot.environment))
                {
                    builder.AppendLine("Environment: " + snapshot.environment);
                }
                
                if (!string.IsNullOrEmpty(snapshot.emotionalTone))
                {
                    builder.AppendLine("Tone: " + snapshot.emotionalTone);
                }
                
                if (snapshot.sceneParticipants.Count > 0)
                {
                    builder.AppendLine("Present: " + string.Join(", ", snapshot.sceneParticipants));
                }
                
                if (!string.IsNullOrEmpty(snapshot.activeQuestId))
                {
                    builder.AppendLine("Active Quest: " + snapshot.activeQuestId);
                    if (!string.IsNullOrEmpty(snapshot.currentQuestBeat))
                    {
                        builder.AppendLine("Current Objective: " + snapshot.currentQuestBeat);
                    }
                }
                
                if (snapshot.inCombat)
                {
                    builder.AppendLine("STATUS: IN COMBAT");
                    builder.AppendLine("Player Health: " + snapshot.playerHealthPercent + "%");
                }
                
                if (snapshot.isVendorMode)
                {
                    builder.AppendLine("STATUS: VENDOR SHOP OPEN");
                    builder.AppendLine("Player Gold: " + snapshot.playerCurrency);
                }
                
                builder.AppendLine();
            }
            
            // Tier C: Compressed Episodic Memory
            if (memoryEvents != null && memoryEvents.Count > 0 && budgets.tierCMemory > 0)
            {
                summarizer.MaxSummaryChars = budgets.tierCMemory * 3;
                string memorySummary = summarizer.Summarize(memoryEvents, budgets.tierCMemory);
                
                if (!string.IsNullOrWhiteSpace(memorySummary))
                {
                    builder.AppendLine("=== What You Remember ===");
                    builder.AppendLine(memorySummary);
                    builder.AppendLine();
                }
            }
            
            // Tier D: Retrieval Snippets
            if (retrievalSnippets != null && retrievalSnippets.Count > 0 && budgets.tierDRetrieval > 0)
            {
                builder.AppendLine("=== Relevant Background ===");
                int charCount = 0;
                int maxChars = budgets.tierDRetrieval * 3;
                
                foreach (var snippet in retrievalSnippets)
                {
                    if (snippet == null || string.IsNullOrWhiteSpace(snippet.text))
                    {
                        continue;
                    }
                    
                    string text = snippet.text.Trim();
                    if (charCount + text.Length > maxChars)
                    {
                        break;
                    }
                    
                    builder.Append("- ");
                    builder.AppendLine(text);
                    charCount += text.Length;
                }
                builder.AppendLine();
            }
            
            // Recent Conversation
            if (recentTurns != null && recentTurns.Count > 0)
            {
                builder.AppendLine("=== Recent Conversation ===");
                int charCount = 0;
                int maxChars = budgets.recentConversation * 3;
                
                // Start from most recent, work backwards
                for (int i = recentTurns.Count - 1; i >= 0; i--)
                {
                    var turn = recentTurns[i];
                    if (turn == null)
                    {
                        continue;
                    }
                    
                    string line = "NPC: " + turn.npcLine + "\nPlayer: " + turn.playerChoice + "\n";
                    if (charCount + line.Length > maxChars)
                    {
                        break;
                    }
                    
                    builder.Insert(builder.ToString().IndexOf("=== Recent Conversation ===") + 28, line);
                    charCount += line.Length;
                }
                builder.AppendLine();
            }
            
            // Max lengths
            if (settings != null)
            {
                builder.AppendLine("Max line length: " + settings.maxLineLength + " characters.");
                builder.AppendLine("Max option length: " + settings.maxOptionLength + " characters.");
            }
            
            // Player's choice (critical)
            if (!string.IsNullOrWhiteSpace(lastPlayerChoice))
            {
                builder.AppendLine();
                builder.AppendLine("=== PLAYER'S CHOICE ===");
                builder.AppendLine("The player chose: \"" + lastPlayerChoice.Trim() + "\"");
                builder.AppendLine();
                builder.AppendLine("IMPORTANT: Your NPC response MUST directly address this choice.");
            }
            
            return builder.ToString();
        }
    }
}
