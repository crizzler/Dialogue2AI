using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ImmersiveNPCs
{
    public static class AIOutputValidator
    {
        [Serializable]
        private class TurnResultJson
        {
            public string npc_line;
            public string[] options;
            public string mood;
            public string memory_delta;
        }

        public static bool TryParse(string json, out TurnResult result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                TurnResultJson parsed = JsonUtility.FromJson<TurnResultJson>(json);
                if (parsed == null)
                {
                    return false;
                }

                result = new TurnResult
                {
                    npcLine = parsed.npc_line ?? string.Empty,
                    options = new List<string>(parsed.options ?? Array.Empty<string>()),
                    mood = parsed.mood ?? string.Empty,
                    memoryDelta = parsed.memory_delta ?? string.Empty
                };
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static TurnResult Sanitize(TurnResult result, int slots, AIConversationSettings settings)
        {
            TurnResult sanitized = result ?? new TurnResult();
            sanitized.npcLine = TrimToLength(sanitized.npcLine, settings.maxLineLength);
            sanitized.options = BuildOptions(sanitized.options, slots, settings.maxOptionLength);
            sanitized.mood = sanitized.mood ?? string.Empty;
            sanitized.memoryDelta = sanitized.memoryDelta ?? string.Empty;
            
            // Check if this looks like a confused response and mark it
            if (IsConfusedResponse(sanitized.npcLine))
            {
                sanitized.isFallback = true;
            }
            
            return sanitized;
        }

        /// <summary>
        /// Detects if the new response is too similar to a previous one (repetition loop).
        /// </summary>
        public static bool IsRepeatedResponse(string newLine, List<AIConversationTurn> recentTurns)
        {
            if (string.IsNullOrWhiteSpace(newLine) || recentTurns == null || recentTurns.Count == 0)
            {
                return false;
            }

            string normalizedNew = NormalizeForComparison(newLine);
            
            // Check against last 3 NPC lines for repetition
            int checkCount = Math.Min(3, recentTurns.Count);
            for (int i = recentTurns.Count - 1; i >= recentTurns.Count - checkCount; i--)
            {
                if (i < 0) break;
                string normalizedOld = NormalizeForComparison(recentTurns[i].npcLine);
                
                // Exact match or very similar
                if (normalizedNew == normalizedOld)
                {
                    return true;
                }
                
                // Check similarity (simple Jaccard-like check)
                if (IsTooSimilar(normalizedNew, normalizedOld, 0.85f))
                {
                    return true;
                }
            }
            
            return false;
        }

        private static string NormalizeForComparison(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.ToLowerInvariant().Trim();
        }

        private static bool IsTooSimilar(string a, string b, float threshold)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            
            // Simple word overlap check
            var wordsA = new HashSet<string>(a.Split(new[] { ' ', ',', '.', '!' }, StringSplitOptions.RemoveEmptyEntries));
            var wordsB = new HashSet<string>(b.Split(new[] { ' ', ',', '.', '!' }, StringSplitOptions.RemoveEmptyEntries));
            
            if (wordsA.Count == 0 || wordsB.Count == 0) return false;
            
            int intersection = 0;
            foreach (var word in wordsA)
            {
                if (wordsB.Contains(word)) intersection++;
            }
            
            int union = wordsA.Count + wordsB.Count - intersection;
            float similarity = (float)intersection / union;
            
            return similarity >= threshold;
        }

        /// <summary>
        /// Detects if player is insisting on the same action.
        /// </summary>
        public static bool IsPlayerInsisting(string currentChoice, List<AIConversationTurn> recentTurns, out int insistCount)
        {
            insistCount = 0;
            if (string.IsNullOrWhiteSpace(currentChoice) || recentTurns == null || recentTurns.Count == 0)
            {
                return false;
            }

            string normalizedCurrent = NormalizeForComparison(currentChoice);
            
            // Check how many recent turns have similar player choices
            for (int i = recentTurns.Count - 1; i >= 0 && i >= recentTurns.Count - 5; i--)
            {
                string normalizedPrev = NormalizeForComparison(recentTurns[i].playerChoice);
                if (IsTooSimilar(normalizedCurrent, normalizedPrev, 0.7f))
                {
                    insistCount++;
                }
                else
                {
                    break; // Stop counting if player changed topic
                }
            }
            
            return insistCount >= 2;
        }

        /// <summary>
        /// Detects confused/broken NPC responses that indicate model lost context.
        /// </summary>
        public static bool IsConfusedResponse(string npcLine)
        {
            if (string.IsNullOrWhiteSpace(npcLine))
            {
                return true;
            }

            // Normalize apostrophes (curly → straight) and lowercase
            string lower = NormalizeApostrophes(npcLine.ToLowerInvariant());
            
            // Debug: Log what we're checking
            AILogger.Log($"[ConfusedCheck] Checking: \"{lower}\"");
            
            // Detect confused/lost context patterns
            string[] confusedPatterns = new string[]
            {
                "i don't understand",
                "i do not understand",
                "could you please clarify",
                "could you clarify",
                "what do you mean",
                "i'm not sure what you're asking",
                "i am not sure what",
                "please clarify",
                "can you explain",
                "i don't know what you"
            };
            
            foreach (var pattern in confusedPatterns)
            {
                if (lower.Contains(pattern))
                {
                    AILogger.Log($"[ConfusedCheck] MATCHED pattern: \"{pattern}\"");
                    return true;
                }
            }
            
            if (npcLine == "...")
                return true;
            
            // Detect responses that are too short to be meaningful
            if (npcLine.Length < 15)
                return true;
                
            return false;
        }

        /// <summary>
        /// Normalize curly apostrophes to straight apostrophes for consistent matching.
        /// </summary>
        private static string NormalizeApostrophes(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Replace curly apostrophes with straight (using Unicode: U+2018, U+2019, U+201C, U+201D)
            return text
                .Replace('\u2018', '\'')  // Left single quote
                .Replace('\u2019', '\'')  // Right single quote (apostrophe)
                .Replace('\u201C', '"')   // Left double quote
                .Replace('\u201D', '"');  // Right double quote
        }

        /// <summary>
        /// Detects if the NPC response is completely unrelated to the player's choice.
        /// This catches cases where the model hallucinates an entirely different scenario.
        /// </summary>
        public static bool IsIncoherentResponse(string npcLine, string playerChoice)
        {
            if (string.IsNullOrWhiteSpace(npcLine) || string.IsNullOrWhiteSpace(playerChoice))
            {
                AILogger.Log($"[IncoherentCheck] Skipped - empty npcLine or playerChoice");
                return false;
            }

            string lowerLine = NormalizeApostrophes(npcLine.ToLowerInvariant());
            string lowerChoice = NormalizeApostrophes(playerChoice.ToLowerInvariant());

            AILogger.Log($"[IncoherentCheck] NPC: \"{lowerLine}\"");
            AILogger.Log($"[IncoherentCheck] Player choice: \"{lowerChoice}\"");

            // Extract key words from player choice (remove common words)
            var stopWords = new HashSet<string> { "the", "a", "an", "to", "go", "check", "ask", "about", "for", "at", "in", "on", "i", "me", "my" };
            var choiceWords = lowerChoice.Split(new[] { ' ', ',', '.', '!' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !stopWords.Contains(w))
                .ToList();

            AILogger.Log($"[IncoherentCheck] Choice keywords: [{string.Join(", ", choiceWords)}]");

            // If player mentioned a specific location/thing, the response should reference it OR explain why not
            bool mentionsChoice = choiceWords.Count == 0 || choiceWords.Any(w => lowerLine.Contains(w));
            
            // Check for hallucinated scenario elements that typically appear in bad generations
            // These are red flags when the player is asking about something mundane like "market" or "lighthouse"
            var scenarioHallucinations = new[] 
            { 
                "gates are locked", "path is sealed", "old map", "ancient artifact",
                "prophecy", "chosen one", "dark forces", "sealed away", "must first"
            };
            
            // Check for topic switching - response introduces completely new concepts
            var topicSwitches = new[]
            {
                "but first", "however, there is", "before you can", "you must find"
            };
            
            bool hasHallucination = scenarioHallucinations.Any(h => lowerLine.Contains(h));
            bool hasTopicSwitch = topicSwitches.Any(t => lowerLine.Contains(t));

            AILogger.Log($"[IncoherentCheck] mentionsChoice={mentionsChoice}, hasHallucination={hasHallucination}, hasTopicSwitch={hasTopicSwitch}");

            // If response has hallucinations/topic switches AND doesn't address the player's choice
            if ((hasHallucination || hasTopicSwitch) && !mentionsChoice)
            {
                AILogger.Log($"[IncoherentCheck] DETECTED INCOHERENT RESPONSE");
                return true;
            }

            return false;
        }

        public static TurnResult CreateFallback(int slots, AIConversationSettings settings)
        {
            TurnResult result = new TurnResult
            {
                npcLine = "...",
                options = new List<string>(),
                isFallback = true
            };

            for (int i = 0; i < slots; i++)
            {
                result.options.Add("Continue");
            }

            return Sanitize(result, slots, settings);
        }

        public static string ExtractJsonSubstring(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            // Strip <think>...</think> blocks (Qwen3 reasoning output)
            text = StripThinkTags(text);

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return string.Empty;
            }

            return text.Substring(start, end - start + 1);
        }

        /// <summary>
        /// Strips <think>...</think> reasoning blocks from model output.
        /// Qwen3 and similar models output chain-of-thought reasoning in these tags.
        /// </summary>
        public static string StripThinkTags(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            // Handle multiple think blocks and nested content
            while (true)
            {
                int thinkStart = text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
                if (thinkStart < 0)
                {
                    break;
                }

                int thinkEnd = text.IndexOf("</think>", thinkStart, StringComparison.OrdinalIgnoreCase);
                if (thinkEnd < 0)
                {
                    // Unclosed think tag - remove from start to end
                    text = text.Substring(0, thinkStart);
                    break;
                }

                // Remove the think block including tags
                text = text.Substring(0, thinkStart) + text.Substring(thinkEnd + 8);
            }

            return text.Trim();
        }

        private static List<string> BuildOptions(List<string> rawOptions, int slots, int maxLength)
        {
            List<string> options = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (rawOptions != null)
            {
                for (int i = 0; i < rawOptions.Count; i++)
                {
                    string option = TrimToLength(rawOptions[i], maxLength);
                    if (string.IsNullOrWhiteSpace(option))
                    {
                        continue;
                    }
                    if (!seen.Add(option))
                    {
                        continue;
                    }
                    options.Add(option);
                    if (options.Count >= slots)
                    {
                        break;
                    }
                }
            }

            while (options.Count < slots)
            {
                options.Add("Continue");
            }

            return options;
        }

        private static string TrimToLength(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            string trimmed = input.Trim();
            if (trimmed.Length <= maxLength)
            {
                return trimmed;
            }

            return trimmed.Substring(0, maxLength);
        }
    }
}
