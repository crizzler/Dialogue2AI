using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Validates NPC responses against world state snapshot.
    /// Extracts claims and checks them against known facts.
    /// </summary>
    public sealed class ResponseValidator
    {
        /// <summary>
        /// Strictness level for validation.
        /// </summary>
        public enum StrictnessLevel
        {
            /// <summary>Only check obvious contradictions.</summary>
            Lenient,
            
            /// <summary>Check locations, NPCs, items.</summary>
            Moderate,
            
            /// <summary>Check all claims including implied facts.</summary>
            Strict
        }
        
        /// <summary>
        /// Result of validation with specific issues found.
        /// </summary>
        public sealed class ValidationResult
        {
            public bool isValid = true;
            public List<string> issues = new List<string>();
            public List<ClaimViolation> violations = new List<ClaimViolation>();
            public bool shouldRetry;
            public string repairHint;
        }
        
        /// <summary>
        /// A specific claim that violates world state.
        /// </summary>
        public sealed class ClaimViolation
        {
            public string claim;
            public string reason;
            public string correctValue;
        }
        
        private static readonly Regex LocationMentionPattern = new Regex(
            @"\b(?:go to|visit|head to|travel to|at the|in the|near the)\s+([A-Za-z][A-Za-z\s]{2,20})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex NpcMentionPattern = new Regex(
            @"\b(?:talk to|speak with|ask|find|see)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex ItemMentionPattern = new Regex(
            @"\b(?:give you|take this|here's a|you have|use the|need the)\s+([A-Za-z][A-Za-z\s]{2,20})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        private static readonly Regex QuestFlagPattern = new Regex(
            @"\b(?:already|have|completed|finished|done)\s+([A-Za-z\s]+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        /// <summary>
        /// Current strictness level.
        /// </summary>
        public StrictnessLevel Strictness { get; set; } = StrictnessLevel.Moderate;
        
        /// <summary>
        /// Validates the response against the world state snapshot.
        /// </summary>
        public ValidationResult Validate(string npcLine, WorldStateSnapshot snapshot, IntentPlan plan)
        {
            var result = new ValidationResult();
            
            if (string.IsNullOrWhiteSpace(npcLine) || snapshot == null)
            {
                return result;
            }
            
            // Extract claims from the response
            var locationClaims = ExtractLocationClaims(npcLine);
            var npcClaims = ExtractNpcClaims(npcLine);
            var itemClaims = ExtractItemClaims(npcLine);
            
            // Validate location claims
            foreach (var loc in locationClaims)
            {
                if (!IsValidLocation(loc, snapshot))
                {
                    result.violations.Add(new ClaimViolation
                    {
                        claim = $"Mentioned location: {loc}",
                        reason = "Location not in valid locations list",
                        correctValue = GetClosestValidLocation(loc, snapshot)
                    });
                }
            }
            
            // Validate NPC claims
            foreach (var npc in npcClaims)
            {
                if (!IsValidNpc(npc, snapshot))
                {
                    result.violations.Add(new ClaimViolation
                    {
                        claim = $"Mentioned NPC: {npc}",
                        reason = "NPC not known to player",
                        correctValue = GetClosestKnownNpc(npc, snapshot)
                    });
                }
            }
            
            // Validate item claims
            if (Strictness >= StrictnessLevel.Moderate)
            {
                foreach (var item in itemClaims)
                {
                    if (!IsValidItem(item, snapshot))
                    {
                        result.violations.Add(new ClaimViolation
                        {
                            claim = $"Mentioned item: {item}",
                            reason = "Item not in valid items list"
                        });
                    }
                }
            }
            
            // Check for quest state contradictions
            if (Strictness >= StrictnessLevel.Strict)
            {
                ValidateQuestClaims(npcLine, snapshot, result);
            }
            
            // Determine overall validity
            if (result.violations.Count > 0)
            {
                result.isValid = false;
                result.shouldRetry = result.violations.Count <= 2; // Only retry if few issues
                result.repairHint = BuildRepairHint(result.violations);
                
                foreach (var v in result.violations)
                {
                    result.issues.Add(v.reason + ": " + v.claim);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Attempts to repair a response by replacing invalid claims.
        /// Only one repair attempt is allowed.
        /// </summary>
        public string AttemptRepair(string npcLine, ValidationResult validation, WorldStateSnapshot snapshot)
        {
            if (validation == null || validation.violations.Count == 0)
            {
                return npcLine;
            }
            
            string repaired = npcLine;
            
            foreach (var violation in validation.violations)
            {
                if (!string.IsNullOrEmpty(violation.correctValue))
                {
                    // Extract the original claim text and replace with corrected value
                    string original = ExtractClaimText(violation.claim);
                    if (!string.IsNullOrEmpty(original))
                    {
                        repaired = repaired.Replace(original, violation.correctValue);
                    }
                }
            }
            
            return repaired;
        }
        
        private List<string> ExtractLocationClaims(string text)
        {
            var locations = new List<string>();
            var matches = LocationMentionPattern.Matches(text);
            
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    locations.Add(match.Groups[1].Value.Trim());
                }
            }
            
            return locations;
        }
        
        private List<string> ExtractNpcClaims(string text)
        {
            var npcs = new List<string>();
            var matches = NpcMentionPattern.Matches(text);
            
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    npcs.Add(match.Groups[1].Value.Trim());
                }
            }
            
            return npcs;
        }
        
        private List<string> ExtractItemClaims(string text)
        {
            var items = new List<string>();
            var matches = ItemMentionPattern.Matches(text);
            
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    items.Add(match.Groups[1].Value.Trim());
                }
            }
            
            return items;
        }
        
        private bool IsValidLocation(string location, WorldStateSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(location))
            {
                return true;
            }
            
            // If no locations defined, allow everything (lenient)
            if (snapshot.validLocations.Count == 0)
            {
                return true;
            }
            
            // Check exact match
            if (snapshot.validLocations.Contains(location))
            {
                return true;
            }
            
            // Check partial match
            string lower = location.ToLowerInvariant();
            foreach (var valid in snapshot.validLocations)
            {
                if (valid.ToLowerInvariant().Contains(lower) || lower.Contains(valid.ToLowerInvariant()))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        private bool IsValidNpc(string npc, WorldStateSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(npc))
            {
                return true;
            }
            
            // If no NPCs defined, allow everything (lenient)
            if (snapshot.knownNpcs.Count == 0)
            {
                return true;
            }
            
            return snapshot.knownNpcs.Contains(npc);
        }
        
        private bool IsValidItem(string item, WorldStateSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(item))
            {
                return true;
            }
            
            // If no items defined, allow everything (lenient)
            if (snapshot.validItems.Count == 0)
            {
                return true;
            }
            
            return snapshot.validItems.Contains(item);
        }
        
        private void ValidateQuestClaims(string text, WorldStateSnapshot snapshot, ValidationResult result)
        {
            // Check for claims about completed quests
            var matches = QuestFlagPattern.Matches(text.ToLowerInvariant());
            
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    string claimed = match.Groups[1].Value.Trim();
                    
                    // Check if claiming something is complete that isn't
                    if (claimed.Contains("quest") || claimed.Contains("task") || claimed.Contains("mission"))
                    {
                        // If snapshot has stage info, validate
                        if (!string.IsNullOrEmpty(snapshot.currentQuestBeat))
                        {
                            // This is a simplified check - full implementation would need quest graph
                            if (text.ToLowerInvariant().Contains("already completed") && 
                                !snapshot.completedStages.Any(s => s.ToLowerInvariant().Contains(claimed)))
                            {
                                result.violations.Add(new ClaimViolation
                                {
                                    claim = $"Claimed completion: {claimed}",
                                    reason = "Quest stage not actually completed"
                                });
                            }
                        }
                    }
                }
            }
        }
        
        private string GetClosestValidLocation(string invalid, WorldStateSnapshot snapshot)
        {
            if (snapshot.validLocations.Count == 0)
            {
                return null;
            }
            
            // Simple fuzzy match - find location with most common characters
            string best = null;
            int bestScore = 0;
            string lowerInvalid = invalid.ToLowerInvariant();
            
            foreach (var loc in snapshot.validLocations)
            {
                string lowerLoc = loc.ToLowerInvariant();
                int score = CountCommonChars(lowerInvalid, lowerLoc);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = loc;
                }
            }
            
            return best;
        }
        
        private string GetClosestKnownNpc(string invalid, WorldStateSnapshot snapshot)
        {
            if (snapshot.knownNpcs.Count == 0)
            {
                return null;
            }
            
            string best = null;
            int bestScore = 0;
            string lowerInvalid = invalid.ToLowerInvariant();
            
            foreach (var npc in snapshot.knownNpcs)
            {
                string lowerNpc = npc.ToLowerInvariant();
                int score = CountCommonChars(lowerInvalid, lowerNpc);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = npc;
                }
            }
            
            return best;
        }
        
        private int CountCommonChars(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return 0;
            }
            
            var setA = new HashSet<char>(a);
            var setB = new HashSet<char>(b);
            return setA.Intersect(setB).Count();
        }
        
        private string BuildRepairHint(List<ClaimViolation> violations)
        {
            if (violations.Count == 0)
            {
                return string.Empty;
            }
            
            var hints = new List<string>();
            foreach (var v in violations.Take(3))
            {
                if (!string.IsNullOrEmpty(v.correctValue))
                {
                    hints.Add($"Replace '{ExtractClaimText(v.claim)}' with '{v.correctValue}'");
                }
                else
                {
                    hints.Add($"Remove reference to '{ExtractClaimText(v.claim)}'");
                }
            }
            
            return string.Join("; ", hints);
        }
        
        private string ExtractClaimText(string claim)
        {
            // Extract the actual text from claims like "Mentioned location: foo"
            int colonIndex = claim.IndexOf(':');
            if (colonIndex >= 0 && colonIndex < claim.Length - 1)
            {
                return claim.Substring(colonIndex + 1).Trim();
            }
            return claim;
        }
    }
}
