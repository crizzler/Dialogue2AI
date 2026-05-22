using System.Text;

namespace ImmersiveNPCs
{
    public static class AIContextBuilder
    {
        public static AIContext BuildContext(AIConversationSettings settings, string npcId, int slots, string language, AIConversationState state, NpcProfile profile, PerceptionSnapshot perception)
        {
            AIContext context = new AIContext
            {
                npcId = npcId,
                slots = slots,
                language = language,
                summary = state != null ? state.Summary : string.Empty,
                lastPlayerChoice = state != null ? state.LastPlayerChoice : string.Empty,
                perception = perception
            };

            if (state != null)
            {
                context.recentTurns.AddRange(state.RecentTurns);
            }

            context.systemPrompt = BuildSystemPrompt(settings, profile, slots, language);
            context.userPrompt = BuildUserPrompt(context, settings);
            return context;
        }

        public static string BuildSystemPrompt(AIConversationSettings settings, NpcProfile profile, int slots, string language)
        {
            StringBuilder builder = new StringBuilder(512);

            if (profile != null && !string.IsNullOrWhiteSpace(profile.personaPrompt))
            {
                builder.AppendLine("NPC Persona:");
                builder.AppendLine(profile.personaPrompt.Trim());
            }

            if (profile != null && !string.IsNullOrWhiteSpace(profile.speakingStyle))
            {
                builder.AppendLine();
                builder.AppendLine("NPC Speaking Style:");
                builder.AppendLine(profile.speakingStyle.Trim());
            }

            if (settings.stayInCharacter)
            {
                builder.AppendLine();
                builder.AppendLine("Stay in character. Do not mention being an AI or a system.");
            }

            if (settings.strictRespondToChoice)
            {
                builder.AppendLine();
                builder.AppendLine("CRITICAL: You MUST directly respond to the player's most recent choice.");
                builder.AppendLine("- If the player wants to go somewhere, guide them or explain why they can't go there.");
                builder.AppendLine("- If the player asks a question, answer it.");
                builder.AppendLine("- If the player makes a statement, acknowledge and respond to it.");
                builder.AppendLine("- NEVER ignore the player's choice or talk about unrelated things.");
                builder.AppendLine("- NEVER say 'I don't understand' - instead, improvise a logical in-character response.");
                builder.AppendLine("- Only mention items, places, or people that exist in your lore constraints.");
                builder.AppendLine();
                builder.AppendLine("STORY PROGRESSION RULES:");
                builder.AppendLine("- NEVER repeat the same response twice. Each response must be unique.");
                builder.AppendLine("- If the player insists on going somewhere, let them go (describe arriving there).");
                builder.AppendLine("- If you already warned about danger, either let them proceed or give a firm 'no' with a reason.");
                builder.AppendLine("- Move the conversation forward, don't stall with repeated warnings.");
            }

            if (settings.forbiddenTopics != null && settings.forbiddenTopics.Count > 0)
            {
                builder.AppendLine();
                builder.Append("Avoid these topics: ");
                for (int i = 0; i < settings.forbiddenTopics.Count; i++)
                {
                    if (i > 0) builder.Append(", ");
                    builder.Append(settings.forbiddenTopics[i]);
                }
                builder.AppendLine(".");
            }

            builder.AppendLine();
            builder.AppendLine("Return JSON only, matching this schema:");
            builder.AppendLine("{");
            builder.AppendLine("  \"npc_line\": \"string\",");
            builder.AppendLine("  \"options\": [\"string\" ... exactly " + slots + " entries],");
            builder.AppendLine("  \"mood\": \"optional short tag\",");
            builder.AppendLine("  \"memory_delta\": \"optional note to remember\"");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("OPTION COHERENCE RULES:");
            builder.AppendLine("- If your npc_line mentions something actionable (a place, object, person, or action), one option MUST let the player pursue it.");
            builder.AppendLine("- Example: If you mention 'check the old map', include an option like 'Check the old map'.");
            builder.AppendLine("- Options must be logical follow-ups to what you just said, not random unrelated choices.");
            builder.AppendLine("- Each option should be distinct and lead to different outcomes.");
            builder.AppendLine();
            builder.AppendLine("Do not include any extra text before or after the JSON. Do not use <think> tags or reasoning blocks.");
            builder.AppendLine("Use language: " + language + ".");
            builder.AppendLine("/no_think");
            return builder.ToString();
        }

        public static string BuildUserPrompt(AIContext context, AIConversationSettings settings)
        {
            StringBuilder builder = new StringBuilder(512);

            if (!string.IsNullOrWhiteSpace(context.summary))
            {
                builder.AppendLine("Summary:");
                builder.AppendLine(context.summary.Trim());
                builder.AppendLine();
            }

            if (context.memorySnippets != null && context.memorySnippets.Count > 0)
            {
                builder.AppendLine("Memory:");
                for (int i = 0; i < context.memorySnippets.Count; i++)
                {
                    AIMemorySnippet snippet = context.memorySnippets[i];
                    if (snippet == null || string.IsNullOrWhiteSpace(snippet.text))
                    {
                        continue;
                    }

                    builder.Append("- ");
                    builder.AppendLine(snippet.text.Trim());
                }
                builder.AppendLine();
            }

            if (context.recentTurns != null && context.recentTurns.Count > 0)
            {
                builder.AppendLine("Recent Turns:");
                for (int i = 0; i < context.recentTurns.Count; i++)
                {
                    var turn = context.recentTurns[i];
                    builder.AppendLine("NPC: " + turn.npcLine);
                    builder.AppendLine("Player: " + turn.playerChoice);
                }
                builder.AppendLine();
            }

            if (context.perception != null && !string.IsNullOrWhiteSpace(context.perception.summary))
            {
                builder.AppendLine("Perception:");
                builder.AppendLine(context.perception.summary.Trim());
                builder.AppendLine();
            }

            // Include entity context from session memory
            if (!string.IsNullOrWhiteSpace(context.entityContext))
            {
                builder.AppendLine("Entity Context:");
                builder.AppendLine(context.entityContext.Trim());
                builder.AppendLine();
            }

            builder.AppendLine("Max line length: " + settings.maxLineLength + " characters.");
            builder.AppendLine("Max option length: " + settings.maxOptionLength + " characters.");

            if (settings.injectChoiceAsLastUserMessage && !string.IsNullOrWhiteSpace(context.lastPlayerChoice))
            {
                builder.AppendLine();
                builder.AppendLine("=== PLAYER'S CHOICE ===");
                builder.AppendLine("The player chose: \"" + context.lastPlayerChoice.Trim() + "\"");
                builder.AppendLine();
                builder.AppendLine("IMPORTANT: Your NPC response MUST directly address this choice.");
                builder.AppendLine("If the player wants to go somewhere, either help them or explain why they can't.");
                builder.AppendLine("If the player asks something, answer their question.");
                builder.AppendLine("Do NOT ignore the player's choice or change the subject.");
                
                // Check for player insistence and add extra guidance
                if (AIOutputValidator.IsPlayerInsisting(context.lastPlayerChoice, context.recentTurns, out int insistCount) && insistCount >= 2)
                {
                    builder.AppendLine();
                    builder.AppendLine("*** PLAYER IS INSISTING (asked " + (insistCount + 1) + " times) ***");
                    builder.AppendLine("The player keeps asking for the same thing. You MUST progress:");
                    builder.AppendLine("- Either LET THEM DO IT (describe them succeeding)");
                    builder.AppendLine("- Or give a FINAL 'no' with a solid reason");
                    builder.AppendLine("Do NOT repeat warnings or stall.");
                }
                
                builder.AppendLine("=======================");
            }

            builder.AppendLine();
            builder.AppendLine("Now generate the NPC's response and new player options as JSON.");
            return builder.ToString();
        }
    }
}
