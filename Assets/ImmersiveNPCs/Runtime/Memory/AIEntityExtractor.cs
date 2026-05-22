using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Represents an actionable entity mentioned in NPC dialogue.
    /// </summary>
    [Serializable]
    public class MentionedEntity
    {
        public string text;
        public EntityType type;
        public float confidence;
        public string suggestedAction;

        public enum EntityType
        {
            Place,
            Object,
            Person,
            Action,
            Direction,
            Time
        }
    }

    /// <summary>
    /// Extracts actionable entities from NPC dialogue lines.
    /// Uses pattern matching and keyword analysis to identify places, objects,
    /// people, and suggested actions that players might want to interact with.
    /// </summary>
    public static class AIEntityExtractor
    {
        // Patterns for detecting actionable phrases
        private static readonly Regex ActionSuggestionPattern = new Regex(
            @"\b(check|visit|go to|head to|see|ask|talk to|speak with|find|look at|examine|inspect|read|use|take|grab|open|enter|explore|search|investigate)\s+(?:the\s+)?([a-z][a-z\s]{2,30})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PlacePattern = new Regex(
            @"\b(?:the\s+)?([A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)\b",
            RegexOptions.Compiled);

        private static readonly Regex DirectionPattern = new Regex(
            @"\b(north|south|east|west|up|down|left|right|ahead|behind|nearby|across|beyond)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PersonReferencePattern = new Regex(
            @"\b(?:the\s+)?(keeper|guard|merchant|smith|innkeeper|tavern keeper|shopkeeper|elder|mayor|captain|sailor|fisherman|farmer|priest|mage|wizard|healer|hunter|guide)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Common non-entity words to filter out
        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "must", "shall", "can", "need", "dare",
            "ought", "used", "to", "of", "in", "for", "on", "with", "at", "by",
            "from", "as", "into", "through", "during", "before", "after",
            "above", "below", "between", "under", "again", "further", "then",
            "once", "here", "there", "when", "where", "why", "how", "all",
            "each", "few", "more", "most", "other", "some", "such", "no",
            "nor", "not", "only", "own", "same", "so", "than", "too", "very",
            "just", "also", "now", "but", "and", "or", "if", "because",
            "until", "while", "although", "though", "even", "both", "either",
            "neither", "whether", "this", "that", "these", "those", "i", "you",
            "he", "she", "it", "we", "they", "me", "him", "her", "us", "them",
            "my", "your", "his", "its", "our", "their", "mine", "yours", "hers",
            "ours", "theirs", "what", "which", "who", "whom", "whose"
        };

        /// <summary>
        /// Extract actionable entities from an NPC dialogue line.
        /// </summary>
        public static List<MentionedEntity> ExtractEntities(string npcLine)
        {
            List<MentionedEntity> entities = new List<MentionedEntity>();
            if (string.IsNullOrWhiteSpace(npcLine))
            {
                return entities;
            }

            // Extract action suggestions (highest confidence)
            ExtractActionSuggestions(npcLine, entities);

            // Extract place names
            ExtractPlaces(npcLine, entities);

            // Extract person references
            ExtractPersonReferences(npcLine, entities);

            // Extract directional references
            ExtractDirections(npcLine, entities);

            // Deduplicate and sort by confidence
            DeduplicateEntities(entities);
            entities.Sort((a, b) => b.confidence.CompareTo(a.confidence));

            return entities;
        }

        /// <summary>
        /// Generate suggested option text for an entity.
        /// </summary>
        public static string GenerateOptionForEntity(MentionedEntity entity)
        {
            if (entity == null || string.IsNullOrWhiteSpace(entity.text))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(entity.suggestedAction))
            {
                return entity.suggestedAction;
            }

            switch (entity.type)
            {
                case MentionedEntity.EntityType.Place:
                    return "Go to " + entity.text;
                case MentionedEntity.EntityType.Object:
                    return "Examine " + entity.text;
                case MentionedEntity.EntityType.Person:
                    return "Talk to " + entity.text;
                case MentionedEntity.EntityType.Direction:
                    return "Head " + entity.text.ToLowerInvariant();
                case MentionedEntity.EntityType.Action:
                    return entity.text;
                default:
                    return "Investigate " + entity.text;
            }
        }

        /// <summary>
        /// Check if an option text matches or addresses an entity.
        /// </summary>
        public static bool OptionMatchesEntity(string optionText, MentionedEntity entity)
        {
            if (string.IsNullOrWhiteSpace(optionText) || entity == null || string.IsNullOrWhiteSpace(entity.text))
            {
                return false;
            }

            string normalizedOption = optionText.ToLowerInvariant();
            string normalizedEntity = entity.text.ToLowerInvariant();

            // Direct containment check
            if (normalizedOption.Contains(normalizedEntity))
            {
                return true;
            }

            // Check for key words from entity
            string[] entityWords = normalizedEntity.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int matchedWords = 0;
            foreach (string word in entityWords)
            {
                if (!StopWords.Contains(word) && normalizedOption.Contains(word))
                {
                    matchedWords++;
                }
            }

            // Consider it a match if most significant words match
            int significantWords = 0;
            foreach (string word in entityWords)
            {
                if (!StopWords.Contains(word))
                {
                    significantWords++;
                }
            }

            return significantWords > 0 && matchedWords >= Math.Ceiling(significantWords * 0.5);
        }

        private static void ExtractActionSuggestions(string text, List<MentionedEntity> entities)
        {
            MatchCollection matches = ActionSuggestionPattern.Matches(text);
            foreach (Match match in matches)
            {
                if (match.Groups.Count >= 3)
                {
                    string action = match.Groups[1].Value.Trim();
                    string target = match.Groups[2].Value.Trim();

                    if (IsValidEntityText(target))
                    {
                        string capitalizedAction = char.ToUpper(action[0]) + action.Substring(1).ToLower();
                        entities.Add(new MentionedEntity
                        {
                            text = target,
                            type = ClassifyEntityType(target, action),
                            confidence = 0.9f,
                            suggestedAction = capitalizedAction + " " + target
                        });
                    }
                }
            }
        }

        private static void ExtractPlaces(string text, List<MentionedEntity> entities)
        {
            // Look for proper nouns (capitalized words)
            MatchCollection matches = PlacePattern.Matches(text);
            foreach (Match match in matches)
            {
                string placeName = match.Groups[1].Value.Trim();
                if (IsValidEntityText(placeName) && !StopWords.Contains(placeName) && placeName.Length > 2)
                {
                    // Skip if it's at the start of a sentence (might just be capitalized word)
                    int idx = match.Index;
                    bool isStartOfSentence = idx == 0 || (idx > 0 && ".!?".Contains(text[idx - 1].ToString()));
                    
                    if (!isStartOfSentence || placeName.Contains(" "))
                    {
                        entities.Add(new MentionedEntity
                        {
                            text = placeName,
                            type = MentionedEntity.EntityType.Place,
                            confidence = 0.6f,
                            suggestedAction = "Visit " + placeName
                        });
                    }
                }
            }
        }

        private static void ExtractPersonReferences(string text, List<MentionedEntity> entities)
        {
            MatchCollection matches = PersonReferencePattern.Matches(text);
            foreach (Match match in matches)
            {
                string person = match.Groups[1].Value.Trim();
                if (IsValidEntityText(person))
                {
                    entities.Add(new MentionedEntity
                    {
                        text = "the " + person.ToLowerInvariant(),
                        type = MentionedEntity.EntityType.Person,
                        confidence = 0.75f,
                        suggestedAction = "Ask the " + person.ToLowerInvariant()
                    });
                }
            }
        }

        private static void ExtractDirections(string text, List<MentionedEntity> entities)
        {
            MatchCollection matches = DirectionPattern.Matches(text);
            foreach (Match match in matches)
            {
                string direction = match.Groups[1].Value.Trim();
                entities.Add(new MentionedEntity
                {
                    text = direction,
                    type = MentionedEntity.EntityType.Direction,
                    confidence = 0.5f,
                    suggestedAction = "Head " + direction.ToLowerInvariant()
                });
            }
        }

        private static MentionedEntity.EntityType ClassifyEntityType(string target, string action)
        {
            string actionLower = action.ToLowerInvariant();
            string targetLower = target.ToLowerInvariant();

            if (actionLower == "ask" || actionLower == "talk to" || actionLower == "speak with")
            {
                return MentionedEntity.EntityType.Person;
            }

            if (actionLower == "go to" || actionLower == "head to" || actionLower == "visit" || actionLower == "enter" || actionLower == "explore")
            {
                return MentionedEntity.EntityType.Place;
            }

            if (actionLower == "check" || actionLower == "examine" || actionLower == "inspect" || 
                actionLower == "read" || actionLower == "look at" || actionLower == "use" ||
                actionLower == "take" || actionLower == "grab" || actionLower == "open")
            {
                return MentionedEntity.EntityType.Object;
            }

            // Guess based on target content
            if (targetLower.Contains("map") || targetLower.Contains("book") || targetLower.Contains("key") ||
                targetLower.Contains("chest") || targetLower.Contains("door") || targetLower.Contains("lantern") ||
                targetLower.Contains("scroll") || targetLower.Contains("letter") || targetLower.Contains("note"))
            {
                return MentionedEntity.EntityType.Object;
            }

            if (targetLower.Contains("tavern") || targetLower.Contains("inn") || targetLower.Contains("shop") ||
                targetLower.Contains("market") || targetLower.Contains("harbor") || targetLower.Contains("lighthouse") ||
                targetLower.Contains("tower") || targetLower.Contains("castle") || targetLower.Contains("house"))
            {
                return MentionedEntity.EntityType.Place;
            }

            return MentionedEntity.EntityType.Object;
        }

        private static bool IsValidEntityText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            if (text.Length < 2 || text.Length > 50)
            {
                return false;
            }

            // Must contain at least one letter
            foreach (char c in text)
            {
                if (char.IsLetter(c))
                {
                    return true;
                }
            }

            return false;
        }

        private static void DeduplicateEntities(List<MentionedEntity> entities)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = entities.Count - 1; i >= 0; i--)
            {
                string key = entities[i].text.ToLowerInvariant().Trim();
                if (seen.Contains(key))
                {
                    entities.RemoveAt(i);
                }
                else
                {
                    seen.Add(key);
                }
            }
        }
    }
}
