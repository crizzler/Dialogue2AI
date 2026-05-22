using System;
using System.Collections.Generic;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Result of coherence validation.
    /// </summary>
    public class CoherenceValidationResult
    {
        public bool isCoherent;
        public List<string> issues = new List<string>();
        public List<MentionedEntity> unmatchedEntities = new List<MentionedEntity>();
        public List<string> suggestedOptions = new List<string>();
        public List<string> correctedOptions;
    }

    /// <summary>
    /// Validates and corrects option coherence with NPC dialogue.
    /// Ensures that actionable items mentioned in NPC lines have corresponding options.
    /// </summary>
    public static class AIOptionCoherenceValidator
    {
        /// <summary>
        /// Minimum confidence threshold for entities to require an option.
        /// </summary>
        public const float MinEntityConfidence = 0.75f;

        /// <summary>
        /// Validate that options are coherent with the NPC line.
        /// </summary>
        public static CoherenceValidationResult Validate(string npcLine, List<string> options)
        {
            CoherenceValidationResult result = new CoherenceValidationResult
            {
                isCoherent = true
            };

            if (string.IsNullOrWhiteSpace(npcLine) || options == null || options.Count == 0)
            {
                return result;
            }

            // Extract entities from NPC line
            List<MentionedEntity> entities = AIEntityExtractor.ExtractEntities(npcLine);
            if (entities.Count == 0)
            {
                return result;
            }

            // Check each high-confidence entity for a matching option
            foreach (MentionedEntity entity in entities)
            {
                if (entity.confidence < MinEntityConfidence)
                {
                    continue;
                }

                bool hasMatch = false;
                foreach (string option in options)
                {
                    if (AIEntityExtractor.OptionMatchesEntity(option, entity))
                    {
                        hasMatch = true;
                        break;
                    }
                }

                if (!hasMatch)
                {
                    result.isCoherent = false;
                    result.unmatchedEntities.Add(entity);
                    result.issues.Add($"NPC mentions '{entity.text}' but no option addresses it");
                    
                    string suggested = AIEntityExtractor.GenerateOptionForEntity(entity);
                    if (!string.IsNullOrWhiteSpace(suggested))
                    {
                        result.suggestedOptions.Add(suggested);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Validate and automatically correct options if incoherent.
        /// Replaces generic/weak options with entity-specific ones.
        /// </summary>
        public static CoherenceValidationResult ValidateAndCorrect(string npcLine, List<string> options, int maxOptions)
        {
            CoherenceValidationResult result = Validate(npcLine, options);
            
            if (result.isCoherent || result.suggestedOptions.Count == 0)
            {
                result.correctedOptions = options;
                return result;
            }

            // Create corrected options list
            result.correctedOptions = new List<string>(options);

            // Find weak options to replace
            List<int> weakOptionIndices = FindWeakOptions(options);

            // Replace weak options with suggested entity options
            int suggestIdx = 0;
            foreach (int weakIdx in weakOptionIndices)
            {
                if (suggestIdx >= result.suggestedOptions.Count)
                {
                    break;
                }

                // Don't replace if we'd create a duplicate
                string suggested = result.suggestedOptions[suggestIdx];
                bool isDuplicate = false;
                foreach (string existing in result.correctedOptions)
                {
                    if (AreSimilarOptions(existing, suggested))
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate && weakIdx < result.correctedOptions.Count)
                {
                    result.correctedOptions[weakIdx] = suggested;
                    suggestIdx++;
                }
            }

            // If still have unmatched high-priority entities and room for more options
            while (suggestIdx < result.suggestedOptions.Count && result.correctedOptions.Count < maxOptions)
            {
                string suggested = result.suggestedOptions[suggestIdx];
                bool isDuplicate = false;
                foreach (string existing in result.correctedOptions)
                {
                    if (AreSimilarOptions(existing, suggested))
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    result.correctedOptions.Add(suggested);
                }
                suggestIdx++;
            }

            // Ensure we have exactly maxOptions
            while (result.correctedOptions.Count > maxOptions)
            {
                result.correctedOptions.RemoveAt(result.correctedOptions.Count - 1);
            }

            return result;
        }

        /// <summary>
        /// Find indices of "weak" options that could be replaced.
        /// Weak options are generic continuations like "Continue", "Ask more", etc.
        /// </summary>
        private static List<int> FindWeakOptions(List<string> options)
        {
            List<int> weak = new List<int>();
            
            string[] weakPatterns = new string[]
            {
                "continue",
                "ask more",
                "tell me more",
                "go on",
                "keep talking",
                "say more",
                "what else",
                "anything else",
                "never mind",
                "leave",
                "goodbye",
                "farewell",
                "end conversation",
                "stay here",
                "wait",
                "do nothing"
            };

            for (int i = 0; i < options.Count; i++)
            {
                string optionLower = options[i].ToLowerInvariant().Trim();
                
                foreach (string pattern in weakPatterns)
                {
                    if (optionLower.Contains(pattern) || optionLower == pattern)
                    {
                        weak.Add(i);
                        break;
                    }
                }
            }

            // If no weak options found, consider the last option as replaceable
            if (weak.Count == 0 && options.Count > 0)
            {
                weak.Add(options.Count - 1);
            }

            return weak;
        }

        /// <summary>
        /// Check if two options are similar enough to be considered duplicates.
        /// </summary>
        private static bool AreSimilarOptions(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            string aLower = a.ToLowerInvariant().Trim();
            string bLower = b.ToLowerInvariant().Trim();

            if (aLower == bLower)
            {
                return true;
            }

            // Check if one contains the other
            if (aLower.Contains(bLower) || bLower.Contains(aLower))
            {
                return true;
            }

            // Check word overlap
            HashSet<string> aWords = new HashSet<string>(aLower.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries));
            HashSet<string> bWords = new HashSet<string>(bLower.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries));
            
            // Remove common words
            string[] commonWords = { "the", "a", "an", "to", "for", "and", "or", "at", "in", "on" };
            foreach (string common in commonWords)
            {
                aWords.Remove(common);
                bWords.Remove(common);
            }

            if (aWords.Count == 0 || bWords.Count == 0)
            {
                return false;
            }

            int overlap = 0;
            foreach (string word in aWords)
            {
                if (bWords.Contains(word))
                {
                    overlap++;
                }
            }

            float similarity = (float)overlap / Math.Min(aWords.Count, bWords.Count);
            return similarity >= 0.6f;
        }

        /// <summary>
        /// Enhanced validation that also considers session context.
        /// </summary>
        public static CoherenceValidationResult ValidateWithContext(
            string npcLine, 
            List<string> options, 
            int maxOptions,
            AISessionMemory sessionMemory,
            string npcId)
        {
            CoherenceValidationResult result = ValidateAndCorrect(npcLine, options, maxOptions);

            if (sessionMemory == null)
            {
                return result;
            }

            // Get unresolved entities from previous turns
            List<MentionedEntity> unresolved = sessionMemory.GetUnresolvedEntities(npcId);
            if (unresolved.Count == 0)
            {
                return result;
            }

            // Check if any current options address previously unresolved entities
            List<MentionedEntity> stillUnresolved = new List<MentionedEntity>();
            foreach (MentionedEntity entity in unresolved)
            {
                bool addressed = false;
                foreach (string option in result.correctedOptions)
                {
                    if (AIEntityExtractor.OptionMatchesEntity(option, entity))
                    {
                        addressed = true;
                        break;
                    }
                }
                if (!addressed)
                {
                    stillUnresolved.Add(entity);
                }
            }

            // If there are still unresolved entities from before and we have room,
            // consider adding options for them (but with lower priority)
            if (stillUnresolved.Count > 0 && result.correctedOptions.Count < maxOptions)
            {
                foreach (MentionedEntity entity in stillUnresolved)
                {
                    if (result.correctedOptions.Count >= maxOptions)
                    {
                        break;
                    }

                    string suggested = AIEntityExtractor.GenerateOptionForEntity(entity);
                    if (string.IsNullOrWhiteSpace(suggested))
                    {
                        continue;
                    }

                    bool isDuplicate = false;
                    foreach (string existing in result.correctedOptions)
                    {
                        if (AreSimilarOptions(existing, suggested))
                        {
                            isDuplicate = true;
                            break;
                        }
                    }

                    if (!isDuplicate)
                    {
                        result.correctedOptions.Add(suggested);
                        result.issues.Add($"Added option for previously mentioned '{entity.text}'");
                    }
                }
            }

            return result;
        }
    }
}
